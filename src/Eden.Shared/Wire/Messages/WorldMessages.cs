using Eden.Shared.Entities;
using Eden.Shared.Ids;

namespace Eden.Shared.Wire.Messages;

/// <summary>
/// Server → viewer: an avatar's state changed (or entered view). Viewer
/// applies this as an interpolation target.
/// </summary>
public readonly record struct AvatarUpdate(AvatarState State);

/// <summary>
/// Server → viewer: a prim's state changed (or came into view).
/// </summary>
public readonly record struct PrimUpdate(PrimState State);

/// <summary>
/// Server → viewer: an avatar left the world (disconnected). Triggers the
/// viewer to remove the avatar from its scene.
/// </summary>
public readonly record struct AvatarLeft(EdenId<UserTag> UserId);

/// <summary>
/// Bi-directional chat message. Channel 0 = public regional; positive channels
/// are listener-addressed; negative channels are reserved for system use.
/// </summary>
public readonly record struct ChatMessage(
    EdenId<UserTag> From,
    int             Channel,
    string          Text);
