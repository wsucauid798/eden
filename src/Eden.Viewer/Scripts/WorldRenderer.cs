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
        // Sun — pitched down-and-forward so the scene is lit from the start,
        // before any WorldStateUpdate arrives from the server.
        _sun = new DirectionalLight3D
        {
            Rotation              = new Vector3(-Mathf.Pi / 4f, -Mathf.Pi / 6f, 0f),
            LightEnergy           = 2.5f,
            LightColor            = new Color(1.0f, 0.97f, 0.92f),
            ShadowEnabled         = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 200f,
            ShadowBias            = 0.05f,
            ShadowNormalBias      = 2.0f,
        };
        AddChild(_sun);

        // Procedural sky — simpler + more forgiving than PhysicalSkyMaterial,
        // and always produces a visible blue dome regardless of sun position.
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor     = new Color(0.35f, 0.54f, 0.82f),
            SkyHorizonColor = new Color(0.75f, 0.80f, 0.88f),
            GroundBottomColor = new Color(0.18f, 0.18f, 0.18f),
            GroundHorizonColor = new Color(0.55f, 0.55f, 0.52f),
            SunAngleMax     = 30f,
            SunCurve        = 0.15f,
            EnergyMultiplier = 1.0f,
        };

        var env = new Godot.Environment
        {
            BackgroundMode      = Godot.Environment.BGMode.Sky,
            Sky                 = new Sky { SkyMaterial = sky },

            // Constant-colour ambient — independent of sky/sun alignment,
            // so the world is always visible. Think of it as "baseline fill
            // light from everywhere." Weather and time-of-day tint this
            // in ApplyWorldState.
            AmbientLightSource  = Godot.Environment.AmbientSource.Color,
            AmbientLightColor   = new Color(0.55f, 0.60f, 0.70f),
            AmbientLightEnergy  = 1.3f,

            TonemapMode         = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure     = 1.0f,
            TonemapWhite        = 6.0f,

            // Subtle distance fog.
            FogEnabled          = true,
            FogMode             = Godot.Environment.FogModeEnum.Exponential,
            FogDensity          = 0.0015f,
            FogLightColor       = new Color(0.78f, 0.82f, 0.90f),

            // Ambient occlusion — cube corners, pillar bases catch shadow.
            SsaoEnabled         = true,
            SsaoIntensity       = 1.5f,
            SsaoRadius          = 1.2f,

            // Subtle bloom.
            GlowEnabled         = true,
            GlowIntensity       = 0.3f,
            GlowStrength        = 0.8f,
            GlowBloom           = 0.05f,
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
            // Tilt the sun to a plausible direction for the hour. We keep
            // the yaw slightly off-north so shadows are never perfectly
            // axis-aligned with the scene.
            _sun.Rotation = new Vector3(
                x: SunMath.SunRotationX(state.TimeOfDayHours),
                y: -Mathf.Pi / 6f,
                z: 0f);
            _sun.LightEnergy = SunMath.IsDaytime(state.TimeOfDayHours) ? 2.5f : 0.8f;
        }

        if (_worldEnv?.Environment is { Sky.SkyMaterial: ProceduralSkyMaterial sky } env)
        {
            sky.SkyTopColor     = SkyTopFor(state.Weather);
            sky.SkyHorizonColor = HorizonFor(state.Weather);
            env.FogDensity      = state.Weather switch
            {
                Weather.Rain   => 0.008f,
                Weather.Snow   => 0.006f,
                Weather.Cloudy => 0.004f,
                _              => 0.0015f,
            };
        }

        _lastApplied = state;
    }

    private static Color SkyTopFor(Weather weather) => weather switch
    {
        Weather.Clear  => new Color(0.35f, 0.54f, 0.82f),
        Weather.Cloudy => new Color(0.55f, 0.58f, 0.62f),
        Weather.Rain   => new Color(0.40f, 0.44f, 0.50f),
        Weather.Snow   => new Color(0.78f, 0.82f, 0.86f),
        _              => new Color(0.35f, 0.54f, 0.82f),
    };

    private static Color HorizonFor(Weather weather) => weather switch
    {
        Weather.Clear  => new Color(0.75f, 0.80f, 0.88f),
        Weather.Cloudy => new Color(0.72f, 0.74f, 0.78f),
        Weather.Rain   => new Color(0.55f, 0.58f, 0.62f),
        Weather.Snow   => new Color(0.88f, 0.90f, 0.92f),
        _              => new Color(0.75f, 0.80f, 0.88f),
    };
}
