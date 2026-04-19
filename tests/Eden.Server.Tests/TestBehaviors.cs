using Eden.Scripting;
using Eden.Shared.Math;

namespace Eden.Server.Tests;

/// <summary>
/// Test-only behaviours that exercise the server's dispatch pipeline.
/// Kept minimal — each does exactly the observable side effect its test
/// needs. The design-doc `DoorScript` / `VendingMachine` samples live
/// in <c>tests/Eden.Scripting.Tests</c> where they cover the scripting
/// contract; they don't cross into the server-tests project.
/// </summary>
public class TouchableBox : EdenBehavior
{
    [OnTouch]
    public Task Touched(Avatar who)
        => Self.RotateBy(Quaternion.FromAxisAngle(Vector3.UnitZ, MathF.PI / 2));
}
