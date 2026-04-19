using System.Collections.Concurrent;
using Godot;
using Eden.Client;
using Eden.Shared.Entities;

namespace Eden.Viewer;

/// <summary>
/// Godot scene binder for <see cref="Eden.Shared.Entities.WorldState"/>.
/// Owns the sun and the <c>WorldEnvironment</c>; reads world-state pushes
/// from a <see cref="ViewerClient"/> and applies them to the scene
/// (sun rotation from time of day, sky tint from weather).
/// </summary>
/// <remarks>
/// <see cref="ViewerClient.WorldStateChanged"/> fires on the client's
/// receive-loop thread. We queue incoming states and drain them on the
/// main thread inside <see cref="_Process"/> — the same pattern
/// <see cref="Main"/> uses for remote-avatar events.
/// </remarks>
public partial class WorldRenderer : Node3D
{
    private DirectionalLight3D? _sun;
    private WorldEnvironment?   _worldEnv;
    private readonly ConcurrentQueue<WorldState> _pending = new();
    private WorldState? _lastApplied;

    /// <summary>The sun light node, created in <see cref="_Ready"/>. Exposed
    /// for tests and for callers that want to read/tweak the sun without
    /// going through the world-state pipeline.</summary>
    public DirectionalLight3D? Sun => _sun;

    /// <summary>The latest world state this renderer has applied. Null until
    /// the first <see cref="ViewerClient.WorldStateChanged"/> fires.</summary>
    public WorldState? LastApplied => _lastApplied;

    public override void _Ready()
    {
        _sun = new DirectionalLight3D
        {
            LightEnergy   = 1.2f,
            ShadowEnabled = true,
        };
        AddChild(_sun);

        var env = new Godot.Environment
        {
            BackgroundMode     = Godot.Environment.BGMode.Sky,
            Sky                = new Sky { SkyMaterial = new PhysicalSkyMaterial() },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            TonemapMode        = Godot.Environment.ToneMapper.Filmic,
            FogEnabled         = true,
            FogDensity         = 0.002f,
        };
        _worldEnv = new WorldEnvironment { Environment = env };
        AddChild(_worldEnv);
    }

    /// <summary>Subscribe to a client's world-state feed. Safe to call
    /// after <see cref="_Ready"/>; if the client already has state, it's
    /// applied on the next <see cref="_Process"/> tick.</summary>
    public void Bind(ViewerClient client)
    {
        client.WorldStateChanged += state => _pending.Enqueue(state);
        if (client.RemoteWorldState is { } initial)
            _pending.Enqueue(initial);
    }

    public override void _Process(double delta)
    {
        while (_pending.TryDequeue(out var state))
            ApplyWorldState(state);
    }

    /// <summary>Apply a world-state snapshot to the scene. Called on the
    /// main thread by <see cref="_Process"/>, or directly by tests.</summary>
    public void ApplyWorldState(WorldState state)
    {
        if (_sun is not null)
        {
            _sun.Rotation = new Vector3(
                x: SunMath.SunRotationX(state.TimeOfDayHours),
                y: 0f,
                z: 0f);
            // Dim the sun at night so ambient sky lighting dominates.
            _sun.LightEnergy = SunMath.IsDaytime(state.TimeOfDayHours) ? 1.2f : 0.15f;
        }

        if (_worldEnv?.Environment is { Sky.SkyMaterial: PhysicalSkyMaterial sky } env)
        {
            sky.GroundColor = GroundTintFor(state.Weather);
            env.FogDensity  = state.Weather switch
            {
                Weather.Rain   => 0.010f,
                Weather.Snow   => 0.008f,
                Weather.Cloudy => 0.005f,
                _              => 0.002f,
            };
        }

        _lastApplied = state;
    }

    private static Color GroundTintFor(Weather weather) => weather switch
    {
        Weather.Clear  => new Color(0.35f, 0.35f, 0.30f),
        Weather.Cloudy => new Color(0.30f, 0.30f, 0.32f),
        Weather.Rain   => new Color(0.22f, 0.24f, 0.28f),
        Weather.Snow   => new Color(0.70f, 0.72f, 0.78f),
        _              => new Color(0.35f, 0.35f, 0.30f),
    };
}
