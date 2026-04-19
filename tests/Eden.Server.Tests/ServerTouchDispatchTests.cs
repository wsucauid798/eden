using Eden.Client;
using Eden.Launcher;
using Eden.Shared.Math;

namespace Eden.Server.Tests;

/// <summary>
/// End-to-end: a viewer sends a <c>ClientTouchPrim</c> frame, the server
/// looks up the prim, builds an <c>Avatar</c> from the session, and
/// dispatches <c>[OnTouch]</c> onto the prim's behavior. Proves the whole
/// chain wire → server → BehaviorHost → script → ISelfContext is live.
/// </summary>
public class ServerTouchDispatchTests
{
    [Fact]
    public async Task Viewer_Touch_Dispatches_OnTouch_On_Server_Door()
    {
        await using var host = EdenLauncher.StartSolo();

        var door = new TouchableBox();
        var (primId, _) = await host.Server.SpawnPrimWithBehaviorAsync(door);

        Assert.Equal(Transform.Identity, host.Server.GetPrimPose(primId));

        await using var alice = new ViewerClient(host.Transport);
        await alice.ConnectAsync("Alice");

        await alice.TouchPrimAsync(primId);

        // Wait (up to 1s) for the server to apply the dispatched rotation.
        var rotated = await WaitForAsync(
            () => host.Server.GetPrimPose(primId).Rotation != Quaternion.Identity,
            TimeSpan.FromSeconds(1));

        Assert.True(rotated, "Server prim pose never changed after touch.");
    }

    [Fact]
    public async Task Touch_On_Unknown_Prim_Is_Ignored_Without_Error()
    {
        await using var host = EdenLauncher.StartSolo();
        await using var alice = new ViewerClient(host.Transport);
        await alice.ConnectAsync("Alice");

        await alice.TouchPrimAsync(Eden.Shared.Ids.EdenId<Eden.Shared.Ids.PrimTag>.New());

        // Server stays up, the handshake works, no crash.
        Assert.NotNull(alice.Session);
    }

    private static async Task<bool> WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return true;
            await Task.Delay(10);
        }
        return predicate();
    }
}
