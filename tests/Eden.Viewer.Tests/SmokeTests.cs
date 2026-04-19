using GdUnit4;
using static GdUnit4.Assertions;

namespace Eden.Viewer.Tests;

/// <summary>
/// Proves the gdUnit4 pipeline is live: discovery, execution, assertion.
/// If this passes, real viewer-code tests (scene nodes, signals, input
/// events, RemotePrims → MeshInstance3D wiring) slot in below using the
/// same infrastructure.
/// </summary>
[TestSuite]
public class SmokeTests
{
    [TestCase]
    public void Infrastructure_Wired()
    {
        AssertThat(2 + 2).IsEqual(4);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Godot_Types_Available()
    {
        // Godot runtime is reachable — we can construct a Node. If the
        // test adapter didn't spin up a headless Godot, this would fault.
        var node = new Godot.Node { Name = "eden-smoke" };
        AssertThat(node.Name.ToString()).IsEqual("eden-smoke");
        node.Free();
    }
}
