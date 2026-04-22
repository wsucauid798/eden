using System;
using System.Collections.Concurrent;
using Godot;
using Eden.Client;
using Eden.Shared.Entities;

namespace Eden.Viewer;

/// <summary>
/// Godot scene binder for <see cref="Eden.Shared.Entities.WorldState"/>.
/// Owns the sun, atmosphere, and sky layers; reads world-state pushes
/// from a <see cref="ViewerClient"/> and applies them to the scene.
/// </summary>
/// <remarks>
/// <see cref="ViewerClient.WorldStateChanged"/> fires on the client's
/// receive-loop thread. We queue incoming states and drain them on the
/// main thread inside <see cref="_Process"/> — the same pattern
/// <see cref="Main"/> uses for remote-avatar events.
/// </remarks>
[Tool]
public partial class WorldRenderer : Node3D
{
	private const float DefaultSunYaw = -Mathf.Pi / 6f;
	private const float CloudLayerRadius = 1200f;
	private const float StarLayerRadius = 1500f;
	private const float SunBodyDistance = 1400f;
	private const float MoonBodyDistance = 1380f;
	private const int CloudTextureWidth = 1024;
	private const int CloudTextureHeight = 512;
	private const int StarTextureWidth = 1024;
	private const int StarTextureHeight = 512;
	private const int DiscTextureSize = 256;

	private DirectionalLight3D? _sun;
	private WorldEnvironment? _worldEnv;
	private PhysicalSkyMaterial? _skyMaterial;
	private Node3D? _skyAnchor;
	private MeshInstance3D? _sunDisc;
	private MeshInstance3D? _moonDisc;
	private MeshInstance3D? _cloudLayer;
	private MeshInstance3D? _starLayer;
	private StandardMaterial3D? _sunDiscMaterial;
	private StandardMaterial3D? _moonDiscMaterial;
	private StandardMaterial3D? _cloudLayerMaterial;
	private StandardMaterial3D? _starLayerMaterial;
	private readonly ConcurrentQueue<WorldState> _pending = new();
	private WorldState? _lastApplied;
	private RenderDebugState? _lastDebugState;

	/// <summary>The sun light node, created in <see cref="_Ready"/>. Exposed
	/// for tests and for callers that want to read/tweak the sun without
	/// going through the world-state pipeline.</summary>
	public DirectionalLight3D? Sun => _sun;

	/// <summary>The latest world state this renderer has applied. Null until
	/// the first <see cref="ViewerClient.WorldStateChanged"/> fires.</summary>
	public WorldState? LastApplied => _lastApplied;

	/// <summary>Latest lighting/debug values derived from the applied world state.
	/// Intended for HUD overlays and Godot output logging.</summary>
	public RenderDebugState? LastDebugState => _lastDebugState;

	/// <summary>Raised whenever a new world-state snapshot has been turned into
	/// renderable lighting/environment values on the main thread.</summary>
	public event Action<RenderDebugState>? DebugStateChanged;

	public readonly record struct RenderDebugState(
		float TimeOfDayHours,
		Weather Weather,
		float DaylightFactor,
		float SunEnergy,
		float SkyEnergy,
		float Exposure,
		float FogDensity,
		float AmbientSkyContribution,
		float AmbientEnergy,
		bool NightSkyEnabled,
		float CloudCover,
		float StarVisibility,
		float MoonVisibility);

	public override void _Ready()
	{
		EnsureBuilt();
	}

	/// <summary>Ensure the editor/runtime scene nodes owned by this renderer
	/// exist exactly once. Safe to call before or after <see cref="_Ready"/>.</summary>
	public void EnsureBuilt()
	{
		if (_sun is null)
		{
			_sun = new DirectionalLight3D
			{
				Rotation = new Vector3(-Mathf.Pi / 4f, DefaultSunYaw, 0f),
				LightEnergy = 2.6f,
				LightColor = new Color(1.00f, 0.97f, 0.93f),
				ShadowEnabled = true,
				DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
				DirectionalShadowMaxDistance = 240f,
				ShadowBias = 0.05f,
				ShadowNormalBias = 1.8f,
			};
			AddChild(_sun);
		}

		if (_skyAnchor is null)
		{
			_skyAnchor = new Node3D { Name = "SkyAnchor" };
			AddChild(_skyAnchor);
			BuildSkyLayers(_skyAnchor);
		}

		if (_worldEnv is not null) return;

		// Use Godot's built-in physically-inspired sky for the atmospheric
		// base. Visual sky objects (sun, moon, stars, clouds) are then
		// handled as normal sky layers, which is the conventional setup.
		_skyMaterial = new PhysicalSkyMaterial
		{
			RayleighCoefficient = 2.0f,
			RayleighColor = new Color(0.30f, 0.44f, 0.78f),
			MieCoefficient = 0.005f,
			MieEccentricity = 0.80f,
			MieColor = new Color(0.98f, 0.96f, 0.93f),
			Turbidity = 2.2f,
			SunDiskScale = 1.2f,
			GroundColor = new Color(0.38f, 0.37f, 0.35f),
			EnergyMultiplier = 0.92f,
			UseDebanding = true,
		};

		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			Sky = new Sky
			{
				SkyMaterial = _skyMaterial,
				RadianceSize = Sky.RadianceSizeEnum.Size256,
			},
			AmbientLightSource = Godot.Environment.AmbientSource.Sky,
			AmbientLightSkyContribution = 0.58f,
			AmbientLightColor = new Color(0.42f, 0.46f, 0.52f),
			AmbientLightEnergy = 0.16f,
			ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
			TonemapMode = Godot.Environment.ToneMapper.Filmic,
			TonemapExposure = 0.96f,
			TonemapWhite = 8.0f,
			FogEnabled = true,
			FogMode = Godot.Environment.FogModeEnum.Exponential,
			FogDensity = 0.00018f,
			FogLightColor = new Color(0.74f, 0.81f, 0.94f),
			FogAerialPerspective = 0.10f,
			SsaoEnabled = true,
			SsaoIntensity = 1.2f,
			SsaoRadius = 1.0f,
			GlowEnabled = false,
		};

		_worldEnv = new WorldEnvironment { Environment = env };
		AddChild(_worldEnv);
		SyncSkyAnchorToCamera();
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
		SyncSkyAnchorToCamera();

		while (_pending.TryDequeue(out var state))
			ApplyWorldState(state);
	}

	/// <summary>Apply a world-state snapshot to the scene. Called on the
	/// main thread by <see cref="_Process"/>, or directly by tests.</summary>
	public void ApplyWorldState(WorldState state)
	{
		EnsureBuilt();

		var daylight = DaylightFactor(state.TimeOfDayHours);

		if (_sun is not null)
		{
			_sun.Rotation = new Vector3(
				x: SunMath.SunRotationX(state.TimeOfDayHours),
				y: DefaultSunYaw,
				z: 0f);
			_sun.LightEnergy = SunEnergyFor(daylight, state.Weather);
			_sun.LightColor = SunLightColorFor(daylight, state.Weather);
		}

		if (_worldEnv?.Environment is { } env)
			ApplyAtmosphere(env, state.Weather, daylight);

		UpdateSkyVisuals(state, daylight);

		_lastApplied = state;
		_lastDebugState = CreateDebugState(state, daylight);
		DebugStateChanged?.Invoke(_lastDebugState.Value);
	}

	private void BuildSkyLayers(Node3D parent)
	{
		_starLayer = BuildSkySphere(
			name: "Stars",
			radius: StarLayerRadius,
			texture: BuildStarTexture(),
			out _starLayerMaterial);
		parent.AddChild(_starLayer);

		_cloudLayer = BuildSkySphere(
			name: "Clouds",
			radius: CloudLayerRadius,
			texture: BuildCloudTexture(),
			out _cloudLayerMaterial);
		parent.AddChild(_cloudLayer);

		_sunDisc = BuildSkyDisc(
			name: "SunDisc",
			texture: BuildSunDiscTexture(),
			out _sunDiscMaterial);
		parent.AddChild(_sunDisc);

		_moonDisc = BuildSkyDisc(
			name: "MoonDisc",
			texture: BuildMoonDiscTexture(),
			out _moonDiscMaterial);
		parent.AddChild(_moonDisc);
	}

	private static MeshInstance3D BuildSkySphere(
		string name,
		float radius,
		Texture2D texture,
		out StandardMaterial3D material)
	{
		material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Front,
			TextureRepeat = true,
			AlbedoTexture = texture,
			AlbedoColor = new Color(1f, 1f, 1f, 0f),
			Roughness = 1.0f,
		};

		var mesh = new MeshInstance3D
		{
			Name = name,
			Mesh = new SphereMesh
			{
				Radius = 1f,
				Height = 2f,
				RadialSegments = 64,
				Rings = 32,
			},
			Scale = Vector3.One * radius,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		mesh.SetSurfaceOverrideMaterial(0, material);
		return mesh;
	}

	private static MeshInstance3D BuildSkyDisc(
		string name,
		Texture2D texture,
		out StandardMaterial3D material)
	{
		material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
			AlbedoTexture = texture,
			AlbedoColor = new Color(1f, 1f, 1f, 0f),
			Roughness = 1.0f,
		};

		var mesh = new MeshInstance3D
		{
			Name = name,
			Mesh = new QuadMesh { Size = new Vector2(1f, 1f) },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		mesh.SetSurfaceOverrideMaterial(0, material);
		return mesh;
	}

	private void SyncSkyAnchorToCamera()
	{
		if (_skyAnchor is null) return;

		var camera = GetViewport().GetCamera3D();
		_skyAnchor.GlobalPosition = camera?.GlobalPosition ?? GlobalPosition;
	}

	private void ApplyAtmosphere(Godot.Environment env, Weather weather, float daylight)
	{
		if (_skyMaterial is null) return;

		_skyMaterial.Turbidity = TurbidityFor(weather);
		_skyMaterial.RayleighCoefficient = Mathf.Lerp(0.03f, RayleighFor(weather), daylight);
		_skyMaterial.RayleighColor = NightBaseColor().Lerp(RayleighColorFor(weather), daylight);
		_skyMaterial.MieCoefficient = Mathf.Lerp(0.001f, MieCoefficientFor(weather), daylight);
		_skyMaterial.MieColor = NightHazeColor().Lerp(MieColorFor(weather), daylight);
		_skyMaterial.MieEccentricity = 0.80f;
		_skyMaterial.GroundColor = GroundColorFor(weather).Darkened((1f - daylight) * 0.52f);
		_skyMaterial.SunDiskScale = SunDiskScaleFor(weather);
		_skyMaterial.EnergyMultiplier = SkyEnergyFor(daylight, weather);

		env.AmbientLightSkyContribution = AmbientSkyContributionFor(daylight, weather);
		env.AmbientLightColor = NightAmbientColor().Lerp(AmbientFillFor(weather), daylight);
		env.AmbientLightEnergy = AmbientFillEnergyFor(daylight, weather);
		env.TonemapExposure = ExposureFor(daylight, weather);
		env.FogDensity = FogDensityFor(weather, daylight);
		env.FogLightColor = FogColorFor(daylight, weather);
		env.FogAerialPerspective = FogAerialPerspectiveFor(weather);
	}

	private void UpdateSkyVisuals(WorldState state, float daylight)
	{
		var sunDirection = SunDirectionFor(state.TimeOfDayHours);
		var moonDirection = -sunDirection;
		var cloudCover = CloudCoverFor(state.Weather);
		var starVisibility = StarVisibilityFor(daylight, state.Weather);
		var moonVisibility = MoonVisibilityFor(daylight, state.Weather);

		if (_sunDisc is not null)
		{
			_sunDisc.Position = sunDirection * SunBodyDistance;
			_sunDisc.Scale = Vector3.One * SunDiscVisualSize(state.Weather);
			_sunDisc.Visible = daylight > 0.02f && sunDirection.Y > -0.10f;
		}

		if (_sunDiscMaterial is not null)
		{
			var color = SunDiscColorFor(daylight, state.Weather);
			var alpha = Mathf.Lerp(0.35f, 0.95f, daylight);
			_sunDiscMaterial.AlbedoColor = new Color(color.R, color.G, color.B, alpha);
		}

		if (_moonDisc is not null)
		{
			_moonDisc.Position = moonDirection * MoonBodyDistance;
			_moonDisc.Scale = Vector3.One * 16f;
			_moonDisc.Visible = moonVisibility > 0.02f && moonDirection.Y > -0.16f;
		}

		if (_moonDiscMaterial is not null)
			_moonDiscMaterial.AlbedoColor = new Color(0.82f, 0.87f, 0.98f, moonVisibility);

		if (_starLayerMaterial is not null)
		{
			_starLayerMaterial.AlbedoColor = new Color(0.92f, 0.96f, 1.00f, starVisibility);
			if (_starLayer is not null)
				_starLayer.Rotation = new Vector3(0f, state.TimeOfDayHours * 0.01f, 0f);
		}

		if (_cloudLayerMaterial is not null)
		{
			var tint = CloudTintFor(state.Weather, daylight);
			var alpha = CloudLayerOpacityFor(state.Weather, daylight);
			_cloudLayerMaterial.AlbedoColor = new Color(tint.R, tint.G, tint.B, alpha);
			if (_cloudLayer is not null)
				_cloudLayer.Rotation = new Vector3(Mathf.DegToRad(7f), state.TimeOfDayHours * 0.018f, 0f);
		}
	}

	private static ImageTexture BuildSunDiscTexture()
	{
		var image = Image.CreateEmpty(DiscTextureSize, DiscTextureSize, false, Image.Format.Rgba8);
		var center = new Vector2(DiscTextureSize * 0.5f, DiscTextureSize * 0.5f);
		var maxRadius = DiscTextureSize * 0.5f;

		for (var y = 0; y < DiscTextureSize; y++)
		for (var x = 0; x < DiscTextureSize; x++)
		{
			var uv = new Vector2(x + 0.5f, y + 0.5f);
			var d = uv.DistanceTo(center) / maxRadius;
			if (d >= 1f)
			{
				image.SetPixel(x, y, new Color(1f, 1f, 1f, 0f));
				continue;
			}

			var core = 1f - SmoothRange(d, 0.08f, 0.38f);
			var halo = 1f - SmoothRange(d, 0.16f, 0.92f);
			var alpha = Mathf.Clamp(core * 0.95f + halo * 0.35f, 0f, 1f);
			var color = new Color(
				r: Mathf.Lerp(1.00f, 1.00f, core),
				g: Mathf.Lerp(0.78f, 0.94f, core),
				b: Mathf.Lerp(0.46f, 0.80f, core),
				a: alpha);
			image.SetPixel(x, y, color);
		}

		return ImageTexture.CreateFromImage(image);
	}

	private static ImageTexture BuildMoonDiscTexture()
	{
		var image = Image.CreateEmpty(DiscTextureSize, DiscTextureSize, false, Image.Format.Rgba8);
		var center = new Vector2(DiscTextureSize * 0.5f, DiscTextureSize * 0.5f);
		var maxRadius = DiscTextureSize * 0.5f;

		for (var y = 0; y < DiscTextureSize; y++)
		for (var x = 0; x < DiscTextureSize; x++)
		{
			var uv = new Vector2(x + 0.5f, y + 0.5f);
			var d = uv.DistanceTo(center) / maxRadius;
			if (d >= 1f)
			{
				image.SetPixel(x, y, new Color(1f, 1f, 1f, 0f));
				continue;
			}

			var disc = 1f - SmoothRange(d, 0.28f, 0.58f);
			var halo = 1f - SmoothRange(d, 0.42f, 0.90f);
			var alpha = Mathf.Clamp(disc * 0.90f + halo * 0.18f, 0f, 1f);
			var color = new Color(
				r: Mathf.Lerp(0.54f, 0.82f, disc),
				g: Mathf.Lerp(0.60f, 0.88f, disc),
				b: Mathf.Lerp(0.72f, 0.98f, disc),
				a: alpha);
			image.SetPixel(x, y, color);
		}

		return ImageTexture.CreateFromImage(image);
	}

	private static ImageTexture BuildCloudTexture()
	{
		var image = Image.CreateEmpty(CloudTextureWidth, CloudTextureHeight, false, Image.Format.Rgba8);

		for (var y = 0; y < CloudTextureHeight; y++)
		{
			var v = (y + 0.5f) / CloudTextureHeight;
			var latitude = (0.5f - v) * Mathf.Pi;
			var hemisphereFade = SmoothRange(0.70f - v, 0.03f, 0.30f);
			var polarFade = 1f - SmoothRange(MathF.Abs(latitude), 1.12f, 1.42f);

			for (var x = 0; x < CloudTextureWidth; x++)
			{
				var u = CloudTextureWidth <= 1 ? 0f : (float)x / (CloudTextureWidth - 1);
				var longitude = (u - 0.5f) * Mathf.Tau;
				var cosLatitude = MathF.Cos(latitude);
				var direction = new Vector3(
					x: cosLatitude * MathF.Cos(longitude),
					y: MathF.Sin(latitude),
					z: cosLatitude * MathF.Sin(longitude));

				// Sample noise in 3D spherical direction space instead of
				// cylindrical UV space so the cloud field reads as a large sky
				// volume rather than a visibly wrapped 2D strip.
				var warp = (FractalNoise3D(
					direction.X * 1.2f + 3.8f,
					direction.Y * 0.9f + 7.4f,
					direction.Z * 1.2f + 11.6f,
					2) - 0.5f) * 0.75f;

				var macro = FractalNoise3D(
					direction.X * 0.95f + 12.1f + warp,
					direction.Y * 0.60f + 18.4f + warp * 0.15f,
					direction.Z * 0.95f + 6.3f - warp * 0.25f,
					4);
				var body = FractalNoise3D(
					direction.X * 1.9f + 7.3f + warp * 0.35f,
					direction.Y * 1.20f + 11.9f,
					direction.Z * 1.9f + 14.7f - warp * 0.20f,
					3);
				var breakup = FractalNoise3D(
					direction.X * 4.2f + 5.1f,
					direction.Y * 1.75f + 17.6f,
					direction.Z * 4.2f + 3.4f,
					2);
				var shape = macro * 0.60f + body * 0.27f + breakup * 0.13f;

				var alpha = SmoothThreshold(shape, 0.58f, 0.09f) * hemisphereFade * polarFade;
				alpha = Mathf.Pow(alpha, 1.35f) * 0.88f;

				var color = new Color(1f, 1f, 1f, alpha);
				image.SetPixel(x, y, color);
			}
		}

		SealHorizontalSeam(image, blendWidth: 4);
		return ImageTexture.CreateFromImage(image);
	}

	private static ImageTexture BuildStarTexture()
	{
		var image = Image.CreateEmpty(StarTextureWidth, StarTextureHeight, false, Image.Format.Rgba8);
		image.Fill(new Color(0f, 0f, 0f, 0f));

		var rng = new Random(61123);
		for (var i = 0; i < 1800; i++)
		{
			var x = rng.Next(StarTextureWidth);
			var y = rng.Next((int)(StarTextureHeight * 0.58f));
			var brightness = 0.12f + (float)rng.NextDouble() * 0.28f;
			var star = new Color(
				r: brightness * 0.88f,
				g: brightness * 0.92f,
				b: Mathf.Min(brightness + 0.08f, 1f),
				a: brightness);

			PlotPixel(image, x, y, star);
			if (brightness < 0.18f) continue;

			var glow = new Color(star.R * 0.24f, star.G * 0.24f, star.B * 0.26f, star.A * 0.40f);
			PlotPixel(image, x - 1, y, glow);
			PlotPixel(image, x + 1, y, glow);
			PlotPixel(image, x, y - 1, glow);
			PlotPixel(image, x, y + 1, glow);
		}

		SealHorizontalSeam(image, blendWidth: 2);
		return ImageTexture.CreateFromImage(image);
	}

	private static float DaylightFactor(float timeOfDayHours)
	{
		var t = SunMath.WrapHours(timeOfDayHours);
		var daylight = MathF.Sin(((t - 6f) / 12f) * Mathf.Pi);
		return Mathf.Clamp(daylight, 0f, 1f);
	}

	private static Vector3 SunDirectionFor(float timeOfDayHours)
	{
		var rotation = new Vector3(SunMath.SunRotationX(timeOfDayHours), DefaultSunYaw, 0f);
		return (Basis.FromEuler(rotation) * Vector3.Back).Normalized();
	}

	private static float SunEnergyFor(float daylight, Weather weather)
	{
		var noonStrength = weather switch
		{
			Weather.Clear => 2.8f,
			Weather.Cloudy => 2.0f,
			Weather.Rain => 1.2f,
			Weather.Snow => 2.3f,
			_ => 2.6f,
		};

		return Mathf.Lerp(0.05f, noonStrength, daylight);
	}

	private static Color SunLightColorFor(float daylight, Weather weather)
	{
		var night = new Color(0.56f, 0.62f, 0.78f);
		var day = new Color(
			1.00f,
			Mathf.Lerp(0.80f, 0.97f, daylight),
			Mathf.Lerp(0.62f, 0.93f, daylight));

		var baseColor = night.Lerp(day, daylight);
		return weather switch
		{
			Weather.Cloudy => baseColor.Lerp(new Color(0.92f, 0.94f, 0.98f), 0.35f),
			Weather.Rain => baseColor.Lerp(new Color(0.82f, 0.86f, 0.92f), 0.65f),
			Weather.Snow => baseColor.Lerp(new Color(0.97f, 0.98f, 1.00f), 0.30f),
			_ => baseColor,
		};
	}

	private static Color SunDiscColorFor(float daylight, Weather weather) => weather switch
	{
		Weather.Clear => new Color(1.00f, 0.94f, 0.72f),
		Weather.Cloudy => new Color(0.96f, 0.92f, 0.80f),
		Weather.Rain => new Color(0.84f, 0.88f, 0.96f),
		Weather.Snow => new Color(0.98f, 0.96f, 0.88f),
		_ => new Color(1.00f, 0.94f, 0.72f),
	};

	private static float SunDiscVisualSize(Weather weather) => weather switch
	{
		Weather.Clear => 26f,
		Weather.Cloudy => 30f,
		Weather.Rain => 34f,
		Weather.Snow => 28f,
		_ => 26f,
	};

	private static float SkyEnergyFor(float daylight, Weather weather)
	{
		var dayValue = weather switch
		{
			Weather.Clear => 0.92f,
			Weather.Cloudy => 0.80f,
			Weather.Rain => 0.64f,
			Weather.Snow => 0.88f,
			_ => 0.86f,
		};

		return Mathf.Lerp(0.18f, dayValue, daylight);
	}

	private static float ExposureFor(float daylight, Weather weather)
	{
		var baseExposure = weather switch
		{
			Weather.Clear => 0.94f,
			Weather.Cloudy => 0.96f,
			Weather.Rain => 0.90f,
			Weather.Snow => 1.02f,
			_ => 0.96f,
		};

		return Mathf.Lerp(0.70f, baseExposure, daylight);
	}

	private static float FogDensityFor(Weather weather, float daylight)
	{
		var baseDensity = weather switch
		{
			Weather.Clear => 0.00018f,
			Weather.Cloudy => 0.0009f,
			Weather.Rain => 0.0028f,
			Weather.Snow => 0.0015f,
			_ => 0.0006f,
		};

		return baseDensity + (1f - daylight) * 0.00025f;
	}

	private static Color FogColorFor(float daylight, Weather weather)
	{
		var night = new Color(0.03f, 0.04f, 0.07f);
		var day = weather switch
		{
			Weather.Clear => new Color(0.72f, 0.80f, 0.93f),
			Weather.Cloudy => new Color(0.70f, 0.73f, 0.80f),
			Weather.Rain => new Color(0.52f, 0.56f, 0.64f),
			Weather.Snow => new Color(0.86f, 0.88f, 0.92f),
			_ => new Color(0.76f, 0.82f, 0.90f),
		};

		return night.Lerp(day, daylight);
	}

	private static float FogAerialPerspectiveFor(Weather weather) => weather switch
	{
		Weather.Clear => 0.08f,
		Weather.Cloudy => 0.12f,
		Weather.Rain => 0.08f,
		Weather.Snow => 0.10f,
		_ => 0.10f,
	};

	private static float AmbientSkyContributionFor(float daylight, Weather weather)
	{
		var dayValue = weather switch
		{
			Weather.Clear => 0.52f,
			Weather.Cloudy => 0.60f,
			Weather.Rain => 0.56f,
			Weather.Snow => 0.60f,
			_ => 0.56f,
		};

		return Mathf.Lerp(0.14f, dayValue, daylight);
	}

	private static Color AmbientFillFor(Weather weather) => weather switch
	{
		Weather.Clear => new Color(0.40f, 0.45f, 0.54f),
		Weather.Cloudy => new Color(0.46f, 0.47f, 0.50f),
		Weather.Rain => new Color(0.38f, 0.42f, 0.48f),
		Weather.Snow => new Color(0.60f, 0.62f, 0.68f),
		_ => new Color(0.44f, 0.48f, 0.54f),
	};

	private static float AmbientFillEnergyFor(float daylight, Weather weather)
	{
		var dayValue = weather switch
		{
			Weather.Clear => 0.14f,
			Weather.Cloudy => 0.20f,
			Weather.Rain => 0.22f,
			Weather.Snow => 0.18f,
			_ => 0.18f,
		};

		return Mathf.Lerp(0.06f, dayValue, daylight);
	}

	private static float TurbidityFor(Weather weather) => weather switch
	{
		Weather.Clear => 2.2f,
		Weather.Cloudy => 4.8f,
		Weather.Rain => 7.2f,
		Weather.Snow => 3.2f,
		_ => 2.6f,
	};

	private static float RayleighFor(Weather weather) => weather switch
	{
		Weather.Clear => 2.0f,
		Weather.Cloudy => 1.3f,
		Weather.Rain => 0.9f,
		Weather.Snow => 1.6f,
		_ => 1.8f,
	};

	private static Color RayleighColorFor(Weather weather) => weather switch
	{
		Weather.Clear => new Color(0.36f, 0.56f, 0.98f),
		Weather.Cloudy => new Color(0.58f, 0.65f, 0.80f),
		Weather.Rain => new Color(0.54f, 0.60f, 0.72f),
		Weather.Snow => new Color(0.72f, 0.78f, 0.96f),
		_ => new Color(0.36f, 0.56f, 0.98f),
	};

	private static float MieCoefficientFor(Weather weather) => weather switch
	{
		Weather.Clear => 0.005f,
		Weather.Cloudy => 0.016f,
		Weather.Rain => 0.032f,
		Weather.Snow => 0.013f,
		_ => 0.008f,
	};

	private static Color MieColorFor(Weather weather) => weather switch
	{
		Weather.Clear => new Color(0.98f, 0.96f, 0.93f),
		Weather.Cloudy => new Color(0.92f, 0.93f, 0.95f),
		Weather.Rain => new Color(0.82f, 0.86f, 0.91f),
		Weather.Snow => new Color(0.95f, 0.97f, 1.00f),
		_ => new Color(0.98f, 0.96f, 0.93f),
	};

	private static float SunDiskScaleFor(Weather weather) => weather switch
	{
		Weather.Clear => 1.2f,
		Weather.Cloudy => 1.5f,
		Weather.Rain => 1.9f,
		Weather.Snow => 1.4f,
		_ => 1.2f,
	};

	private static Color GroundColorFor(Weather weather) => weather switch
	{
		Weather.Clear => new Color(0.38f, 0.37f, 0.35f),
		Weather.Cloudy => new Color(0.42f, 0.42f, 0.42f),
		Weather.Rain => new Color(0.30f, 0.32f, 0.35f),
		Weather.Snow => new Color(0.72f, 0.74f, 0.78f),
		_ => new Color(0.38f, 0.37f, 0.35f),
	};

	private static float CloudCoverFor(Weather weather) => weather switch
	{
		Weather.Clear => 0.26f,
		Weather.Cloudy => 0.58f,
		Weather.Rain => 0.88f,
		Weather.Snow => 0.72f,
		_ => 0.30f,
	};

	private static Color CloudTintFor(Weather weather, float daylight)
	{
		var night = new Color(0.14f, 0.16f, 0.22f);
		var day = weather switch
		{
			Weather.Clear => new Color(0.97f, 0.98f, 1.00f),
			Weather.Cloudy => new Color(0.82f, 0.84f, 0.88f),
			Weather.Rain => new Color(0.58f, 0.62f, 0.70f),
			Weather.Snow => new Color(0.94f, 0.96f, 1.00f),
			_ => new Color(0.90f, 0.92f, 0.96f),
		};

		return night.Lerp(day, daylight);
	}

	private static float CloudLayerOpacityFor(Weather weather, float daylight)
	{
		var weatherOpacity = weather switch
		{
			Weather.Clear => 0.34f,
			Weather.Cloudy => 0.56f,
			Weather.Rain => 0.70f,
			Weather.Snow => 0.62f,
			_ => 0.38f,
		};

		return weatherOpacity * Mathf.Lerp(0.42f, 1.00f, daylight);
	}

	private static float StarVisibilityFor(float daylight, Weather weather)
	{
		var nightFactor = 1f - SmoothRange(daylight, 0.05f, 0.28f);
		var weatherFactor = weather switch
		{
			Weather.Clear => 1.00f,
			Weather.Cloudy => 0.45f,
			Weather.Rain => 0.10f,
			Weather.Snow => 0.24f,
			_ => 0.80f,
		};

		return nightFactor * weatherFactor;
	}

	private static float MoonVisibilityFor(float daylight, Weather weather)
	{
		var twilightFactor = 1f - SmoothRange(daylight, 0.08f, 0.42f);
		var weatherFactor = weather switch
		{
			Weather.Clear => 0.92f,
			Weather.Cloudy => 0.58f,
			Weather.Rain => 0.18f,
			Weather.Snow => 0.36f,
			_ => 0.76f,
		};

		return twilightFactor * weatherFactor;
	}

	private static Color NightBaseColor() => new(0.05f, 0.09f, 0.18f);

	private static Color NightHazeColor() => new(0.18f, 0.22f, 0.30f);

	private static Color NightAmbientColor() => new(0.05f, 0.07f, 0.11f);

	private static float FractalNoise(float x, float y, int octaves)
	{
		var value = 0f;
		var amplitude = 0.5f;
		var frequency = 1f;
		var totalAmplitude = 0f;

		for (var i = 0; i < octaves; i++)
		{
			value += ValueNoise(x * frequency, y * frequency) * amplitude;
			totalAmplitude += amplitude;
			frequency *= 2f;
			amplitude *= 0.5f;
		}

		return totalAmplitude <= 0f ? 0f : value / totalAmplitude;
	}

	private static float FractalNoise3D(float x, float y, float z, int octaves)
	{
		var value = 0f;
		var amplitude = 0.5f;
		var frequency = 1f;
		var totalAmplitude = 0f;

		for (var i = 0; i < octaves; i++)
		{
			value += ValueNoise3D(x * frequency, y * frequency, z * frequency) * amplitude;
			totalAmplitude += amplitude;
			frequency *= 2f;
			amplitude *= 0.5f;
		}

		return totalAmplitude <= 0f ? 0f : value / totalAmplitude;
	}

	private static float ValueNoise(float x, float y)
	{
		var ix = MathF.Floor(x);
		var iy = MathF.Floor(y);
		var fx = x - ix;
		var fy = y - iy;

		var a = Hash(ix, iy);
		var b = Hash(ix + 1f, iy);
		var c = Hash(ix, iy + 1f);
		var d = Hash(ix + 1f, iy + 1f);

		var ux = Smooth01(fx);
		var uy = Smooth01(fy);
		return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
	}

	private static float ValueNoise3D(float x, float y, float z)
	{
		var ix = MathF.Floor(x);
		var iy = MathF.Floor(y);
		var iz = MathF.Floor(z);
		var fx = x - ix;
		var fy = y - iy;
		var fz = z - iz;

		var c000 = Hash(ix, iy, iz);
		var c100 = Hash(ix + 1f, iy, iz);
		var c010 = Hash(ix, iy + 1f, iz);
		var c110 = Hash(ix + 1f, iy + 1f, iz);
		var c001 = Hash(ix, iy, iz + 1f);
		var c101 = Hash(ix + 1f, iy, iz + 1f);
		var c011 = Hash(ix, iy + 1f, iz + 1f);
		var c111 = Hash(ix + 1f, iy + 1f, iz + 1f);

		var ux = Smooth01(fx);
		var uy = Smooth01(fy);
		var uz = Smooth01(fz);
		var x00 = Mathf.Lerp(c000, c100, ux);
		var x10 = Mathf.Lerp(c010, c110, ux);
		var x01 = Mathf.Lerp(c001, c101, ux);
		var x11 = Mathf.Lerp(c011, c111, ux);
		var y0 = Mathf.Lerp(x00, x10, uy);
		var y1 = Mathf.Lerp(x01, x11, uy);
		return Mathf.Lerp(y0, y1, uz);
	}

	private static float Hash(float x, float y)
	{
		var value = MathF.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
		return value - MathF.Floor(value);
	}

	private static float Hash(float x, float y, float z)
	{
		var value = MathF.Sin(x * 127.1f + y * 311.7f + z * 74.7f) * 43758.5453f;
		return value - MathF.Floor(value);
	}

	private static float SmoothThreshold(float value, float threshold, float softness)
	{
		return SmoothRange(value, threshold - softness, threshold + softness);
	}

	private static float SmoothRange(float value, float edge0, float edge1)
	{
		if (Mathf.IsEqualApprox(edge0, edge1))
			return value < edge0 ? 0f : 1f;

		var t = Saturate((value - edge0) / (edge1 - edge0));
		return Smooth01(t);
	}

	private static float Smooth01(float value)
	{
		var t = Saturate(value);
		return t * t * (3f - 2f * t);
	}

	private static float Saturate(float value) => Mathf.Clamp(value, 0f, 1f);

	private static void PlotPixel(Image image, int x, int y, Color color)
	{
		if ((uint)x >= image.GetWidth() || (uint)y >= image.GetHeight()) return;

		var existing = image.GetPixel(x, y);
		image.SetPixel(x, y, new Color(
			r: Mathf.Clamp(existing.R + color.R, 0f, 1f),
			g: Mathf.Clamp(existing.G + color.G, 0f, 1f),
			b: Mathf.Clamp(existing.B + color.B, 0f, 1f),
			a: Mathf.Clamp(existing.A + color.A, 0f, 1f)));
	}

	private static void SealHorizontalSeam(Image image, int blendWidth)
	{
		if (blendWidth <= 0) return;

		var width = image.GetWidth();
		var height = image.GetHeight();
		var clampedBlend = Mathf.Min(blendWidth, width / 2);
		for (var y = 0; y < height; y++)
		{
			for (var i = 0; i < clampedBlend; i++)
			{
				var left = i;
				var right = width - clampedBlend + i;
				var a = image.GetPixel(left, y);
				var b = image.GetPixel(right, y);
				var merged = new Color(
					r: (a.R + b.R) * 0.5f,
					g: (a.G + b.G) * 0.5f,
					b: (a.B + b.B) * 0.5f,
					a: (a.A + b.A) * 0.5f);
				image.SetPixel(left, y, merged);
				image.SetPixel(right, y, merged);
			}
		}
	}

	private RenderDebugState CreateDebugState(WorldState state, float daylight)
	{
		var env = _worldEnv?.Environment;
		return new RenderDebugState(
			TimeOfDayHours: state.TimeOfDayHours,
			Weather: state.Weather,
			DaylightFactor: daylight,
			SunEnergy: _sun?.LightEnergy ?? 0f,
			SkyEnergy: _skyMaterial?.EnergyMultiplier ?? 0f,
			Exposure: env?.TonemapExposure ?? 0f,
			FogDensity: env?.FogDensity ?? 0f,
			AmbientSkyContribution: env?.AmbientLightSkyContribution ?? 0f,
			AmbientEnergy: env?.AmbientLightEnergy ?? 0f,
			NightSkyEnabled: StarVisibilityFor(daylight, state.Weather) > 0.05f,
			CloudCover: CloudCoverFor(state.Weather),
			StarVisibility: StarVisibilityFor(daylight, state.Weather),
			MoonVisibility: MoonVisibilityFor(daylight, state.Weather));
	}
}
