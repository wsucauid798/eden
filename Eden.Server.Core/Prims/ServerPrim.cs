using Eden.Scripting.Host;
using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Server.Core.Prims;

/// <summary>
/// A prim living in the server's world registry. Holds its mutable pose
/// and, optionally, a handle to an attached <see cref="EdenBehavior"/>
/// (that's how touches, collisions, etc. reach script code).
/// </summary>
internal sealed class ServerPrim(EdenId<PrimTag> id, EdenId<UserTag> ownerId)
{
    private readonly object _lock = new();
    private Transform _pose = Transform.Identity;

    public EdenId<PrimTag> Id      { get; } = id;
    public EdenId<UserTag> OwnerId { get; } = ownerId;

    public Transform Pose
    {
        get { lock (_lock) return _pose; }
        set { lock (_lock) _pose = value; }
    }

    public BehaviorHandle? Behavior { get; set; }
}
