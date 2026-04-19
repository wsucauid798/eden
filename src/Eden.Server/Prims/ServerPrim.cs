using Eden.Scripting.Host;
using Eden.Shared.Entities;
using Eden.Shared.Ids;
using Eden.Shared.Math;
using JoltPhysicsSharp;

namespace Eden.Server.Prims;

/// <summary>
/// A prim living in the server's world registry. Carries everything
/// <see cref="PrimState"/> needs so the server can broadcast updates whenever
/// a field changes, plus an optional attached behavior handle.
/// </summary>
internal sealed class ServerPrim(EdenId<PrimTag> id, EdenId<UserTag> ownerId)
{
    private readonly object _lock = new();
    private Transform        _pose      = Transform.Identity;
    private Vector3          _scale     = Vector3.One;
    private EdenId<AssetTag> _shape     = EdenId<AssetTag>.Empty;
    private Color            _tint      = Color.White;
    private PrimFlags        _flags     = PrimFlags.None;

    public EdenId<PrimTag> Id      { get; } = id;
    public EdenId<UserTag> OwnerId { get; } = ownerId;

    public Transform Pose
    {
        get { lock (_lock) return _pose; }
        set { lock (_lock) _pose = value; }
    }

    public Vector3 Scale
    {
        get { lock (_lock) return _scale; }
        set { lock (_lock) _scale = value; }
    }

    public EdenId<AssetTag> ShapeAssetId
    {
        get { lock (_lock) return _shape; }
        set { lock (_lock) _shape = value; }
    }

    public Color TintColor
    {
        get { lock (_lock) return _tint; }
        set { lock (_lock) _tint = value; }
    }

    public PrimFlags Flags
    {
        get { lock (_lock) return _flags; }
        set { lock (_lock) _flags = value; }
    }

    public BehaviorHandle? Behavior { get; set; }

    /// <summary>The Jolt body ID backing this prim. <c>null</c> until the
    /// prim is registered with the physics world (currently all spawns
    /// register a body; kept nullable to leave room for phantom/no-physics
    /// prims later).</summary>
    public BodyID? BodyId { get; set; }

    public PrimState ToPrimState()
    {
        lock (_lock)
            return new PrimState(Id, OwnerId, _pose, _scale, _shape, _tint, _flags);
    }
}
