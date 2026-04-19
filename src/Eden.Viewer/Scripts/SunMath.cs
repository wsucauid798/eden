namespace Eden.Viewer;

/// <summary>
/// Pure math for the sun's position from the server's world clock. No
/// Godot dependency — fully unit-testable without the Godot runtime.
/// </summary>
internal static class SunMath
{
	/// <summary>
	/// X-axis rotation (radians) to apply to a <c>DirectionalLight3D</c> so
	/// that its light vector matches the sun at the given time of day.
	/// Convention: Godot <c>DirectionalLight3D</c> shines along local -Z,
	/// so rotating about X by this value moves the sun across the sky.
	/// </summary>
	/// <remarks>
	/// Mapping (time → angle):
	/// <list type="bullet">
	///   <item>06:00 dawn → 0 rad (light horizontal, sun on east horizon)</item>
	///   <item>12:00 noon → -π/2 (light straight down, sun overhead)</item>
	///   <item>18:00 dusk → -π (light horizontal the other way, sun on west horizon)</item>
	///   <item>00:00 midnight → -3π/2 ≡ π/2 (light straight up, sun below horizon)</item>
	/// </list>
	/// Linear interpolation is intentional — good enough for MVP; real solar
	/// elevation is a sine curve that can replace this later without any
	/// callsite change.
	/// </remarks>
	public static float SunRotationX(float timeOfDayHours)
	{
		var normalised = WrapHours(timeOfDayHours);
		return -(normalised - 6f) / 24f * System.MathF.PI * 2f;
	}

	/// <summary>Clamp hours into [0, 24) regardless of wrap direction.</summary>
	public static float WrapHours(float timeOfDayHours)
	{
		var t = timeOfDayHours % 24f;
		return t < 0f ? t + 24f : t;
	}

	/// <summary>True when the sun is above the horizon at the given time.
	/// Trivially: daytime is 06:00–18:00 under the linear model.</summary>
	public static bool IsDaytime(float timeOfDayHours)
	{
		var t = WrapHours(timeOfDayHours);
		return t >= 6f && t < 18f;
	}
}
