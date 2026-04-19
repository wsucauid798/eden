using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Scripting;

/// <summary>Capabilities the script has on the prim it's attached to.
/// Every verb a script can execute on its own prim lives here — testing
/// a behavior is just passing in a mock <c>ISelfContext</c>.</summary>
public interface ISelfContext
{
    /// <summary>The prim the behavior is attached to.</summary>
    EdenId<PrimTag> Id { get; }

    /// <summary>Current pose of the prim.</summary>
    Transform Pose { get; }

    /// <summary>Move the prim to an absolute position.
    /// If <paramref name="duration"/> is set, the move is smoothed over time;
    /// otherwise it snaps.</summary>
    Task MoveTo(Vector3 position, TimeSpan? duration = null);

    /// <summary>Rotate the prim by a relative quaternion.
    /// <paramref name="duration"/> smooths the rotation over time when set.</summary>
    Task RotateBy(Quaternion delta, TimeSpan? duration = null);

    /// <summary>Scale the prim's geometry by a per-axis factor.</summary>
    Task SetScale(Vector3 factor);

    /// <summary>Set the floating text above the prim. Empty clears it.</summary>
    Task SetHoverText(string text);

    /// <summary>Speak in chat from this prim on the given channel.</summary>
    Task Say(string message, int channel = 0);

    /// <summary>Play a sound asset at the prim's location.</summary>
    Task PlaySound(string assetName);

    /// <summary>Pay coins back to an avatar who paid this prim.</summary>
    Task RefundAsync(Avatar to, int coins);
}

/// <summary>Capabilities the script has on the surrounding world. Narrow
/// sub-APIs make the sandbox boundary explicit — when scripts move behind
/// WASM later, each sub-API can be trimmed or gated independently.</summary>
public interface IWorldContext
{
    IInventoryApi Inventory { get; }
    IAvatarApi    Avatars   { get; }
    IPhysicsApi   Physics   { get; }

    /// <summary>Look up a prim by id. Returns null if it no longer exists.</summary>
    Task<Prim?> FindPrim(EdenId<PrimTag> id);

    /// <summary>Stream every prim within <paramref name="radius"/> metres of
    /// <paramref name="center"/>.</summary>
    IAsyncEnumerable<Prim> PrimsNear(Vector3 center, float radius);
}

public interface IInventoryApi
{
    /// <summary>Give an inventory item (identified by asset id) to an avatar.</summary>
    Task GiveAsync(Avatar recipient, EdenId<AssetTag> assetId);
}

public interface IAvatarApi
{
    /// <summary>Look up an avatar by user id. Null if they're not online in this world.</summary>
    Task<Avatar?> Find(EdenId<UserTag> userId);

    /// <summary>Eject the avatar from the region. Requires appropriate permissions.</summary>
    Task EjectAsync(Avatar who, string? reason = null);
}

public interface IPhysicsApi
{
    /// <summary>Cast a ray and return the first hit, if any. Placeholder —
    /// actual shape lands with Phase 3's Bullet integration.</summary>
    Task<ICollision?> Raycast(Vector3 origin, Vector3 direction, float maxDistance);
}
