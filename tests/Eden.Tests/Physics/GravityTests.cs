using Eden.Launcher;
using Eden.Shared.Entities;
using Eden.Shared.Math;

namespace Eden.Tests.Physics;

/// <summary>
/// Smallest meaningful physics test: a prim marked
/// <see cref="PrimFlags.Physical"/> falls under gravity. Proves the Jolt
/// integration is actually stepping the simulation.
/// </summary>
public class GravityTests
{
    [Fact]
    public async Task Physical_Prim_Falls_Under_Gravity()
    {
        await using var host = EdenLauncher.StartSolo();

        var (primId, _) = await host.Server.SpawnPrimAsync(
            pose:  new Transform(new Vector3(0f, 10f, 0f), Quaternion.Identity),
            scale: Vector3.One,
            flags: PrimFlags.Physical);

        // Sample immediately — the physics loop may have already ticked once
        // between spawn returning and this read, so don't assume 10.
        var initialY = host.Server.GetPrimPose(primId).Position.Y;

        // 500 ms at 60 Hz = ~30 steps. Under -9.81 m/s² gravity,
        // s = ½gt² ≈ 1.22 m. Allow headroom for timer jitter.
        await Task.Delay(500);

        var finalY = host.Server.GetPrimPose(primId).Position.Y;
        Assert.True(finalY < initialY - 0.5f,
            $"Expected prim to fall; initialY={initialY} finalY={finalY}");
    }

    [Fact]
    public async Task Static_Prim_Does_Not_Move()
    {
        await using var host = EdenLauncher.StartSolo();

        var (primId, _) = await host.Server.SpawnPrimAsync(
            pose:  new Transform(new Vector3(0f, 10f, 0f), Quaternion.Identity),
            scale: Vector3.One,
            flags: PrimFlags.None);

        var start = host.Server.GetPrimPose(primId);
        await Task.Delay(200);

        Assert.Equal(start, host.Server.GetPrimPose(primId));
    }
}
