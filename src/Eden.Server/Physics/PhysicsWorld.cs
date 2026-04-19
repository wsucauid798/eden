using JoltPhysicsSharp;
using Microsoft.Extensions.Logging;
using EdenVec3 = Eden.Shared.Math.Vector3;
using EdenQuat = Eden.Shared.Math.Quaternion;
using EdenTransform = Eden.Shared.Math.Transform;
using JoltVec3 = System.Numerics.Vector3;
using JoltQuat = System.Numerics.Quaternion;

namespace Eden.Server.Physics;

/// <summary>
/// Thin wrapper over Jolt's <c>PhysicsSystem</c>. Owns the allocator, job
/// system, and collision-layer tables; exposes a narrow API for adding /
/// removing box bodies and stepping the simulation. Conversion between
/// Eden's math types (<see cref="EdenVec3"/>, <see cref="EdenQuat"/>) and
/// <see cref="System.Numerics"/> — which Jolt uses natively — is private to
/// this class.
/// </summary>
internal sealed class PhysicsWorld : IDisposable
{
    // Two-layer setup: static bodies on 0, dynamic on 1. Static cannot
    // collide with static; everything else collides normally.
    public static class Layers
    {
        public static readonly ObjectLayer Static  = 0;
        public static readonly ObjectLayer Dynamic = 1;
    }

    private static class BroadPhaseLayers
    {
        public static readonly BroadPhaseLayer Static  = 0;
        public static readonly BroadPhaseLayer Dynamic = 1;
    }

    private static int s_refCount;
    private static readonly object s_initLock = new();

    private readonly ILogger<PhysicsWorld> _logger;
    private readonly PhysicsSystem _system;
    private readonly JobSystem _jobSystem;
    private readonly ObjectLayerPairFilterTable _objectLayerFilter;
    private readonly BroadPhaseLayerInterfaceTable _broadPhaseInterface;
    private readonly ObjectVsBroadPhaseLayerFilterTable _objectVsBroadPhaseFilter;
    private bool _disposed;

    public PhysicsWorld(ILogger<PhysicsWorld> logger)
    {
        _logger = logger;

        // Foundation is process-global. Ref-count so multiple servers in one
        // process (as a test fixture might spin up) share init without one
        // stomping another's shutdown.
        lock (s_initLock)
        {
            if (s_refCount++ == 0)
            {
                if (!Foundation.Init(false))
                    throw new InvalidOperationException("Jolt Foundation.Init failed");
            }
        }

        _objectLayerFilter = new ObjectLayerPairFilterTable(2);
        _objectLayerFilter.EnableCollision(Layers.Static,  Layers.Dynamic);
        _objectLayerFilter.EnableCollision(Layers.Dynamic, Layers.Dynamic);

        _broadPhaseInterface = new BroadPhaseLayerInterfaceTable(2, 2);
        _broadPhaseInterface.MapObjectToBroadPhaseLayer(Layers.Static,  BroadPhaseLayers.Static);
        _broadPhaseInterface.MapObjectToBroadPhaseLayer(Layers.Dynamic, BroadPhaseLayers.Dynamic);

        _objectVsBroadPhaseFilter = new ObjectVsBroadPhaseLayerFilterTable(
            _broadPhaseInterface, 2, _objectLayerFilter, 2);

        var settings = new PhysicsSystemSettings
        {
            MaxBodies                   = 65536,
            MaxBodyPairs                = 65536,
            MaxContactConstraints       = 10240,
            NumBodyMutexes              = 0,
            ObjectLayerPairFilter       = _objectLayerFilter,
            BroadPhaseLayerInterface    = _broadPhaseInterface,
            ObjectVsBroadPhaseLayerFilter = _objectVsBroadPhaseFilter,
        };

        _jobSystem = new JobSystemThreadPool();
        _system    = new PhysicsSystem(settings);
    }

    /// <summary>Add a box-shaped body. Returns the Jolt body ID, which the
    /// caller keeps and hands back to <see cref="RemoveBody"/> /
    /// <see cref="GetTransform"/>.</summary>
    public BodyID AddBox(
        EdenVec3      center,
        EdenVec3      halfExtent,
        EdenQuat      rotation,
        MotionType    motion)
    {
        var layer = motion == MotionType.Static ? Layers.Static : Layers.Dynamic;
        var shape = new BoxShape(ToJolt(halfExtent));

        using var settings = new BodyCreationSettings(
            shape, ToJolt(center), ToJolt(rotation), motion, layer);

        var activation = motion == MotionType.Static ? Activation.DontActivate : Activation.Activate;
        return _system.BodyInterface.CreateAndAddBody(settings, activation);
    }

    public void RemoveBody(BodyID id) => _system.BodyInterface.RemoveAndDestroyBody(id);

    public EdenTransform GetTransform(BodyID id) => new(
        ToEden(_system.BodyInterface.GetPosition(id)),
        ToEden(_system.BodyInterface.GetRotation(id)));

    /// <summary>Advance the simulation by <paramref name="deltaSeconds"/>.
    /// Caller decides the timestep; 1/60 s is the recommended upper bound.</summary>
    public void Step(float deltaSeconds, int collisionSteps = 1)
    {
        var error = _system.Update(deltaSeconds, collisionSteps, _jobSystem);
        if (error != PhysicsUpdateError.None)
            _logger.LogWarning("Physics step returned {Error}", error);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _system.Dispose();
        _jobSystem.Dispose();

        lock (s_initLock)
        {
            if (--s_refCount == 0)
                Foundation.Shutdown();
        }
    }

    // --- Math conversions (Eden ↔ System.Numerics) ---

    private static JoltVec3 ToJolt(EdenVec3 v)  => new(v.X, v.Y, v.Z);
    private static JoltQuat ToJolt(EdenQuat q)  => new(q.X, q.Y, q.Z, q.W);
    private static EdenVec3 ToEden(JoltVec3 v)  => new(v.X, v.Y, v.Z);
    private static EdenQuat ToEden(JoltQuat q)  => new(q.X, q.Y, q.Z, q.W);
}
