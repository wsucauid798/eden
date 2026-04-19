using Eden.Scripting;
using Eden.Shared.Entities;
using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Server.Core.Prims;

/// <summary>
/// Server-side <see cref="ISelfContext"/>. Mutates the underlying
/// <see cref="ServerPrim"/> and notifies the server via
/// <paramref name="broadcast"/> so viewers see the change on the wire.
/// </summary>
internal sealed class ServerSelfContext(
    ServerPrim              prim,
    Func<PrimState, Task>   broadcast) : ISelfContext
{
    public EdenId<PrimTag> Id   => prim.Id;
    public Transform       Pose => prim.Pose;

    public async Task MoveTo(Vector3 position, TimeSpan? duration = null)
    {
        prim.Pose = prim.Pose with { Position = position };
        await broadcast(prim.ToPrimState()).ConfigureAwait(false);
    }

    public async Task RotateBy(Quaternion delta, TimeSpan? duration = null)
    {
        prim.Pose = prim.Pose with { Rotation = delta * prim.Pose.Rotation };
        await broadcast(prim.ToPrimState()).ConfigureAwait(false);
    }

    public async Task SetScale(Vector3 factor)
    {
        prim.Scale = new Vector3(
            prim.Scale.X * factor.X,
            prim.Scale.Y * factor.Y,
            prim.Scale.Z * factor.Z);
        await broadcast(prim.ToPrimState()).ConfigureAwait(false);
    }

    // Verbs that need their own wire surface (chat, sound, hover text,
    // refund) stay as no-ops until those broadcasts land.
    public Task SetHoverText(string text)      => Task.CompletedTask;
    public Task Say(string message, int c = 0) => Task.CompletedTask;
    public Task PlaySound(string assetName)    => Task.CompletedTask;
    public Task RefundAsync(Avatar to, int c)  => Task.CompletedTask;
}
