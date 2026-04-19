using Eden.Scripting;
using Eden.Scripting.Host;
using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Tests.Scripting;

/// <summary>
/// End-to-end: take the <see cref="DoorScript"/> from the design-doc samples,
/// run it through the real <see cref="BehaviorHost"/>, and verify it moves
/// the door and plays sounds when touched. If either side of the scripting
/// contract regresses this test catches it.
/// </summary>
public class DoorScriptIntegrationTests
{
    [Fact]
    public async Task Touching_Door_Rotates_It_And_Plays_Sound()
    {
        var host  = new BehaviorHost();
        var door  = new DoorScript();
        var self  = new MockSelf();
        var world = new MockWorld();

        await using var handle = await host.AttachAsync(door, self, world);

        var alice = new Avatar(EdenId<UserTag>.New(), "Alice", Transform.Identity);
        await host.DispatchAsync(typeof(OnTouchAttribute), [alice]);

        // DoorScript.Open rotates by OpenSwingRadians about Z and plays 'door-open'.
        Assert.NotEqual(Quaternion.Identity, self.Pose.Rotation);
        Assert.Contains("door-open", self.SoundLog);
    }

    [Fact]
    public async Task Second_Touch_Closes_The_Door()
    {
        var host = new BehaviorHost();
        var door = new DoorScript();
        var self = new MockSelf();

        await using var _ = await host.AttachAsync(door, self, new MockWorld());

        var alice = new Avatar(EdenId<UserTag>.New(), "Alice", Transform.Identity);
        await host.DispatchAsync(typeof(OnTouchAttribute), [alice]);
        await host.DispatchAsync(typeof(OnTouchAttribute), [alice]);

        Assert.Contains("door-open",  self.SoundLog);
        Assert.Contains("door-close", self.SoundLog);
    }
}
