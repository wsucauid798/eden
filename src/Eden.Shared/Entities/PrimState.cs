using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Shared.Entities;

/// <summary>
/// A wire-ready snapshot of a single prim. Sent from server to viewers that
/// can see the prim. Scale is carried here (unlike <see cref="Transform"/>)
/// because prim geometry is scaled independently of the pose.
/// </summary>
public readonly record struct PrimState(
    EdenId<PrimTag>  Id,
    EdenId<UserTag>  OwnerId,
    Transform        Transform,
    Vector3          Scale,
    EdenId<AssetTag> ShapeAssetId,
    Color            TintColor,
    PrimFlags        Flags);

[Flags]
public enum PrimFlags : uint
{
    None      = 0,
    Physical  = 1 << 0,   // Simulated by physics
    Phantom   = 1 << 1,   // No collision
    Hidden    = 1 << 2,   // Invisible to other avatars
    Locked    = 1 << 3,   // Cannot be moved/edited
    TempOnRez = 1 << 4,   // Auto-deletes after time
}
