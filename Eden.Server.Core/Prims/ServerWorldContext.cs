using Eden.Scripting;
using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Server.Core.Prims;

/// <summary>
/// Placeholder <see cref="IWorldContext"/> for MVP. Inventory / avatars /
/// physics services throw <see cref="NotImplementedException"/> — a script
/// that touches them on the server right now will fail loudly, which is
/// preferable to silent no-ops. Flesh these out as Phase 3+ services arrive.
/// </summary>
internal sealed class ServerWorldContext : IWorldContext
{
    public IInventoryApi Inventory { get; } = new NotYetImplementedInventory();
    public IAvatarApi    Avatars   { get; } = new NotYetImplementedAvatars();
    public IPhysicsApi   Physics   { get; } = new NotYetImplementedPhysics();

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
