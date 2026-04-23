using Eden.Shared.Entities;

namespace Eden.Shared.World;

/// <summary>
/// Authoritative logical environment settings for a world.
/// </summary>
/// <remarks>
/// This is intentionally renderer-neutral: it describes the world's sky and
/// weather conditions, not Godot-specific tonemapping or material knobs.
/// Viewers translate these values into their local rendering implementation.
/// </remarks>
public readonly record struct EnvironmentState(
    EnvironmentProfile Profile,
    float CloudCover,
    float Haze,
    float FogDensity,
    float PrecipitationIntensity,
    float SunIntensity,
    float SkyBrightness,
    float AmbientBrightness,
    float MoonVisibility,
    float StarVisibility)
{
    public static EnvironmentState Clear => FromWeather(Weather.Clear);

    public static EnvironmentState FromWeather(Weather weather) => weather switch
    {
        Weather.Cloudy => new(
            Profile: EnvironmentProfile.Cloudy,
            CloudCover: 0.58f,
            Haze: 0.12f,
            FogDensity: 0.0009f,
            PrecipitationIntensity: 0.00f,
            SunIntensity: 2.0f,
            SkyBrightness: 0.80f,
            AmbientBrightness: 0.20f,
            MoonVisibility: 0.58f,
            StarVisibility: 0.45f),

        Weather.Rain => new(
            Profile: EnvironmentProfile.Rain,
            CloudCover: 0.88f,
            Haze: 0.08f,
            FogDensity: 0.0028f,
            PrecipitationIntensity: 1.00f,
            SunIntensity: 1.2f,
            SkyBrightness: 0.64f,
            AmbientBrightness: 0.22f,
            MoonVisibility: 0.18f,
            StarVisibility: 0.10f),

        Weather.Snow => new(
            Profile: EnvironmentProfile.Snow,
            CloudCover: 0.72f,
            Haze: 0.10f,
            FogDensity: 0.0015f,
            PrecipitationIntensity: 0.65f,
            SunIntensity: 2.3f,
            SkyBrightness: 0.88f,
            AmbientBrightness: 0.18f,
            MoonVisibility: 0.36f,
            StarVisibility: 0.24f),

        _ => new(
            Profile: EnvironmentProfile.Clear,
            CloudCover: 0.26f,
            Haze: 0.08f,
            FogDensity: 0.00018f,
            PrecipitationIntensity: 0.00f,
            SunIntensity: 2.8f,
            SkyBrightness: 0.92f,
            AmbientBrightness: 0.14f,
            MoonVisibility: 0.92f,
            StarVisibility: 1.00f),
    };

    public EnvironmentState NormalizedFor(Weather weather)
    {
        if (SunIntensity <= 0f &&
            SkyBrightness <= 0f &&
            AmbientBrightness <= 0f &&
            CloudCover <= 0f &&
            FogDensity <= 0f)
        {
            return FromWeather(weather);
        }

        return this with
        {
            CloudCover = Clamp01(CloudCover),
            Haze = Clamp01(Haze),
            FogDensity = global::System.MathF.Max(0f, FogDensity),
            PrecipitationIntensity = Clamp01(PrecipitationIntensity),
            SunIntensity = global::System.MathF.Max(0f, SunIntensity),
            SkyBrightness = global::System.MathF.Max(0f, SkyBrightness),
            AmbientBrightness = global::System.MathF.Max(0f, AmbientBrightness),
            MoonVisibility = Clamp01(MoonVisibility),
            StarVisibility = Clamp01(StarVisibility),
        };
    }

    private static float Clamp01(float value) => global::System.Math.Clamp(value, 0f, 1f);
}

public enum EnvironmentProfile : byte
{
    Clear = 0,
    Cloudy = 1,
    Rain = 2,
    Snow = 3,
}
