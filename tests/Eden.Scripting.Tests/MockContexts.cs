using Eden.Scripting;
using Eden.Shared.Ids;
using Eden.Shared.Math;

namespace Eden.Scripting.Tests;

internal sealed class MockSelf : ISelfContext
{
    public EdenId<PrimTag> Id { get; } = EdenId<PrimTag>.New();
    public Transform Pose { get; set; } = Transform.Identity;

    public List<string> SayLog { get; } = new();
    public List<string> SoundLog { get; } = new();
    public string HoverText { get; private set; } = string.Empty;

    public Task MoveTo(Vector3 position, TimeSpan? duration = null)
    {
        Pose = Pose with { Position = position };
        return Task.CompletedTask;
    }

    public Task RotateBy(Quaternion delta, TimeSpan? duration = null)
    {
        Pose = Pose with { Rotation = delta * Pose.Rotation };
        return Task.CompletedTask;
    }

    public Task SetScale(Vector3 factor) => Task.CompletedTask;

    public Task SetHoverText(string text)
    {
        HoverText = text;
        return Task.CompletedTask;
    }

    public Task Say(string message, int channel = 0)
    {
        SayLog.Add($"[{channel}] {message}");
        return Task.CompletedTask;
    }

    public Task PlaySound(string assetName)
    {
        SoundLog.Add(assetName);
        return Task.CompletedTask;
    }

    public Task RefundAsync(Avatar to, int coins) => Task.CompletedTask;
}

internal sealed class MockWorld : IWorldContext
{
    public IInventoryApi Inventory { get; } = new MockInventory();
    public IAvatarApi    Avatars   { get; } = new MockAvatars();
    public IPhysicsApi   Physics   { get; } = new MockPhysics();

    public Task<Prim?> FindPrim(EdenId<PrimTag> id) => Task.FromResult<Prim?>(null);
    public async IAsyncEnumerable<Prim> PrimsNear(Vector3 center, float radius)
    {
        await Task.CompletedTask;
        yield break;
    }
}

internal sealed class MockInventory : IInventoryApi
{
    public List<(Avatar recipient, EdenId<AssetTag> assetId)> Given { get; } = new();
    public Task GiveAsync(Avatar recipient, EdenId<AssetTag> assetId)
    {
        Given.Add((recipient, assetId));
        return Task.CompletedTask;
    }
}

internal sealed class MockAvatars : IAvatarApi
{
    public Task<Avatar?> Find(EdenId<UserTag> userId)       => Task.FromResult<Avatar?>(null);
    public Task EjectAsync(Avatar who, string? reason = null) => Task.CompletedTask;
}

internal sealed class MockPhysics : IPhysicsApi
{
    public Task<ICollision?> Raycast(Vector3 origin, Vector3 direction, float maxDistance)
        => Task.FromResult<ICollision?>(null);
}
