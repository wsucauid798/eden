using Eden.Scripting;
using Eden.Shared.Entities;
using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Server.Prims;

/// <summary>
/// Server-side <see cref="IWorldContext"/>. The world snapshot comes from a
/// callback supplied by <c>EdenServer</c> so scripts always read the latest
/// authoritative state. Inventory / avatars / physics services still stub
/// with <see cref="NotImplementedException"/> — they fail loudly rather
/// than silently, which is preferable while Phase 3 fills them in.
/// </summary>
internal sealed class ServerWorldContext(Func<WorldState> worldSnapshot) : IWorldContext
{
    public IInventoryApi Inventory { get; } = new NotYetImplementedInventory();
    public IAvatarApi    Avatars   { get; } = new NotYetImplementedAvatars();
    public IPhysicsApi   Physics   { get; } = new NotYetImplementedPhysics();

    public WorldState World => worldSnapshot();

    public Task<Prim?> FindPrim(EdenId<PrimTag> id) => Task.FromResult<Prim?>(null);

    public async IAsyncEnumerable<Prim> PrimsNear(Vector3 center, float radius)
    {
        await Task.CompletedTask;
        yield break;
    }

    private sealed class NotYetImplementedInventory : IInventoryApi
    {
        public Task GiveAsync(Avatar recipient, EdenId<AssetTag> assetId) =>
            throw new NotImplementedException("Inventory service not yet wired on server");
    }

    private sealed class NotYetImplementedAvatars : IAvatarApi
    {
        public Task<Avatar?> Find(EdenId<UserTag> userId) =>
            throw new NotImplementedException("Avatar directory not yet wired on server");

        public Task EjectAsync(Avatar who, string? reason = null) =>
            throw new NotImplementedException("Avatar eject not yet wired on server");
    }

    private sealed class NotYetImplementedPhysics : IPhysicsApi
    {
        public Task<ICollision?> Raycast(Vector3 origin, Vector3 direction, float maxDistance) =>
            throw new NotImplementedException("Physics raycast not yet wired on server");
    }
}
