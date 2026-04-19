using Eden.Client;
using Eden.Launcher;
using Eden.Shared.Entities;
using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Shared.Tests.Scripting;

/// <summary>
/// Closes the visible loop: a viewer touches a scripted prim, the server
/// dispatches <c>OnTouch</c>, the script's <c>Self.RotateBy</c> fans out
/// a <see cref="PrimUpdate"/> to every connected viewer, and each viewer's
/// <see cref="ViewerClient.RemotePrims"/> sees the rotation.
/// </summary>
public class PrimBroadcastTests
{
    [Fact]
    public async Task Spawn_Broadcasts_Initial_PrimUpdate()
    {
        await using var host = EdenLauncher.StartSolo();

        // Connect Alice first, then spawn the prim — the broadcast happens
        // because Alice is already listening.
        await using var alice = new ViewerClient(host.Transport);
        await alice.ConnectAsync("Alice");

        var door = new DoorScript();
        var (primId, _) = await host.Server.SpawnPrimWithBehaviorAsync(door);

        var seen = await WaitForAsync(
            () => alice.RemotePrims.ContainsKey(primId),
            TimeSpan.FromSeconds(1));
        Assert.True(seen, "Viewer never received the spawn PrimUpdate.");
    }

    [Fact]
    public async Task Late_Joiner_Receives_Existing_Prims()
    {
        await using var host = EdenLauncher.StartSolo();

        var door = new DoorScript();
        var (primId, _) = await host.Server.SpawnPrimWithBehaviorAsync(door);

        var lateTransport = host.Connect();
        await using var late = new ViewerClient(lateTransport);
        await late.ConnectAsync("Late");

        Assert.True(
            late.RemotePrims.ContainsKey(primId),
            "Late-joining viewer did not receive existing prim on hello.");
    }

    [Fact]
    public async Task Touch_Broadcasts_Rotated_PrimUpdate_To_Viewer()
    {
        await using var host = EdenLauncher.StartSolo();
        var door = new DoorScript();
        var (primId, _) = await host.Server.SpawnPrimWithBehaviorAsync(door);

        await using var alice = new ViewerClient(host.Transport);
        await alice.ConnectAsync("Alice");

        // Wait for initial spawn broadcast to arrive at Alice.
        Assert.True(await WaitForAsync(
            () => alice.RemotePrims.ContainsKey(primId),
            TimeSpan.FromSeconds(1)));

        await alice.TouchPrimAsync(primId);

        // Wait for the rotated state to arrive.
        var rotated = await WaitForAsync(
            () => alice.RemotePrims.TryGetValue(primId, out var s) &&
                  s.Transform.Rotation != Quaternion.Identity,
            TimeSpan.FromSeconds(2));

        Assert.True(rotated, "Viewer's RemotePrims never showed the rotated door.");
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
