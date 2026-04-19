using GdUnit4;
using Eden.Viewer;
using static GdUnit4.Assertions;

namespace Eden.Viewer.Tests;

/// <summary>
/// Pure maths — no Godot runtime needed, no <c>[RequireGodotRuntime]</c>.
/// Verifies the time-of-day → sun-rotation mapping and the daytime/
/// night boundary. If these change the renderer will light the world
/// differently, so lock them down.
/// </summary>
[TestSuite]
public class SunMathTests
{
    [TestCase]
    public void WrapHours_Normalises_Into_24_Hour_Range()
    {
        AssertThat(SunMath.WrapHours(0f)).IsEqual(0f);
        AssertThat(SunMath.WrapHours(12f)).IsEqual(12f);
        AssertThat(SunMath.WrapHours(24f)).IsEqual(0f);
        AssertThat(SunMath.WrapHours(25f)).IsEqual(1f);
        AssertThat(SunMath.WrapHours(-1f)).IsEqual(23f);
    }

    [TestCase]
    public void Is_Daytime_Between_6_And_18()
    {
        AssertThat(SunMath.IsDaytime(0f)).IsFalse();
        AssertThat(SunMath.IsDaytime(5.99f)).IsFalse();
        AssertThat(SunMath.IsDaytime(6f)).IsTrue();
        AssertThat(SunMath.IsDaytime(12f)).IsTrue();
        AssertThat(SunMath.IsDaytime(17.99f)).IsTrue();
        AssertThat(SunMath.IsDaytime(18f)).IsFalse();
    }

    [TestCase]
    public void Sun_Rotation_At_Key_Times()
    {
        // 06:00 dawn → 0 (horizontal)
        AssertFloat(SunMath.SunRotationX(6f)).IsEqualApprox(0f, 1e-4f);
        // 12:00 noon → -π/2 (overhead)
        AssertFloat(SunMath.SunRotationX(12f)).IsEqualApprox(-System.MathF.PI / 2f, 1e-4f);
        // 18:00 dusk → -π
        AssertFloat(SunMath.SunRotationX(18f)).IsEqualApprox(-System.MathF.PI, 1e-4f);
    }
}
