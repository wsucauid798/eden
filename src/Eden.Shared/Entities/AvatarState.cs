using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Shared.Entities;

/// <summary>
/// A wire-ready snapshot of an avatar's visible state. Sent from server to
/// viewers that can see this avatar. Immutable — a new snapshot per update.
/// Heavy data (full appearance, attachments) is identified by hash; viewers
/// fetch the detail separately when the hash changes.
/// </summary>
public readonly record struct AvatarState(
    EdenId<UserTag>    UserId,
    EdenId<SessionTag> SessionId,
    string             DisplayName,
    Transform          Transform,
    Vector3            Velocity,
    uint               AppearanceHash);
