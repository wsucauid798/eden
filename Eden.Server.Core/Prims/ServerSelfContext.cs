using Eden.Scripting;
using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Server.Core.Prims;

/// <summary>
/// Server-side <see cref="ISelfContext"/> implementation. Mutates the
/// underlying <see cref="ServerPrim"/> directly. Verbs that notify viewers
/// (<c>Say</c>, <c>PlaySound</c>, <c>SetHoverText</c>) are stubbed for MVP
/// — they become broadcasts once the prim/chat wire surface catches up.
/// </summary>
internal sealed class ServerSelfContext(ServerPrim prim) : ISelfContext
{
    public EdenId<PrimTag> Id   => prim.Id;
    public Transform       Pose => prim.Pose;

    public Task MoveTo(Vector3 position, TimeSpan? duration = null)
    {
        prim.Pose = prim.Pose with { Position = position };
        return Task.CompletedTask;
    }

    public Task RotateBy(Quaternion delta, TimeSpan? duration = null)
    {
        prim.Pose = prim.Pose with { Rotation = delta * prim.Pose.Rotation };
        return Task.CompletedTask;
    }

    public Task SetScale(Vector3 factor)       => Task.CompletedTask;
    public Task SetHoverText(string text)      => Task.CompletedTask;
    public Task Say(string message, int c = 0) => Task.CompletedTask;
    public Task PlaySound(string assetName)    => Task.CompletedTask;
    public Task RefundAsync(Avatar to, int c)  => Task.CompletedTask;
}
