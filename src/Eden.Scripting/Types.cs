using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Scripting;

/// <summary>Script-facing view of an avatar. Distinct from the wire-level
/// <c>AvatarState</c>: this is what handlers receive, trimmed to what
/// scripts commonly need.</summary>
public readonly record struct Avatar(
    EdenId<UserTag> UserId,
    string          DisplayName,
    Transform       Pose);

/// <summary>Script-facing view of a prim. Scripts get this from
/// <c>World.FindPrim</c> / <c>World.PrimsNear</c>.</summary>
public readonly record struct Prim(
    EdenId<PrimTag> Id,
    EdenId<UserTag> OwnerId,
    Transform       Pose,
    Vector3         Scale);

/// <summary>A collision event passed to <c>[OnCollisionStart]</c> /
/// <c>[OnCollisionEnd]</c>. Exactly one of <see cref="Avatar"/> /
/// <see cref="Prim"/> is non-null depending on what the prim collided with.</summary>
public interface ICollision
{
    Avatar? Avatar { get; }
    Prim?   Prim   { get; }
    Vector3 Point  { get; }
    Vector3 Normal { get; }
}
