using Eden.Scripting;
using Eden.Shared.Ids;
using Eden.Shared.Math;
using Microsoft.Extensions.Logging;

namespace Eden.Shared.Tests;

// The *fact that this file compiles* is the test. If the Eden.Scripting API
// drifts from the shape _docs/_design/scripting-model.md locked in, these
// sample behaviors stop compiling and the build catches it.
//
// No [Fact] here — no runtime host exists yet. Phase 3's Roslyn host will
// exercise these same shapes through a real behavior loader.

public class DoorScript : EdenBehavior
{
    [Configurable] public float OpenSwingRadians { get; set; } = MathF.PI / 2f;

    private bool _open;

    [OnTouch]
    public async Task OnTouched(Avatar who)
    {
        Log.LogInformation("{User} touched the door", who.DisplayName);
        if (_open) await Close(); else await Open();
    }

    private async Task Open()
    {
        await Self.RotateBy(Quaternion.FromAxisAngle(Vector3.UnitZ, OpenSwingRadians));
        await Self.PlaySound("door-open");
        _open = true;
    }

    private async Task Close()
    {
        await Self.RotateBy(Quaternion.FromAxisAngle(Vector3.UnitZ, -OpenSwingRadians));
        await Self.PlaySound("door-close");
        _open = false;
    }
}

public class VendingMachine : EdenBehavior
{
    [Configurable] public EdenId<AssetTag> Product    { get; set; }
    [Configurable] public int              PriceCoins { get; set; } = 10;
    [Persistent]   public int              StockRemaining { get; set; } = 50;

    protected override Task OnEnable() =>
        Self.SetHoverText($"${PriceCoins} — {StockRemaining} left");

    [OnPayment]
    public async Task OnPaid(Avatar buyer, int coins)
    {
        if (coins < PriceCoins || StockRemaining <= 0)
        {
            await Self.RefundAsync(buyer, coins);
            await Self.Say(StockRemaining <= 0
                ? "Out of stock."
                : $"Sorry {buyer.DisplayName}, that's {PriceCoins} coins.");
            return;
        }

        if (coins > PriceCoins)
            await Self.RefundAsync(buyer, coins - PriceCoins);

        await World.Inventory.GiveAsync(buyer, Product);
        StockRemaining--;
        await Self.SetHoverText($"${PriceCoins} — {StockRemaining} left");
        Log.LogInformation("{Buyer} bought {Product}; {Remaining} left",
            buyer.DisplayName, Product, StockRemaining);
    }
}

[SerializeHandlers]
public class SerialDemo : EdenBehavior
{
    [OnTick(seconds: 1.0)]
    public Task Tick(TimeSpan delta) => Task.CompletedTask;

    [OnChat(Channel = 5)]
    public Task Whisper(Avatar from, string text) => Task.CompletedTask;

    [OnCollisionStart]
    public Task Bumped(ICollision hit) => Task.CompletedTask;

    [OnTimer(seconds: 30.0)]
    public Task HalfMinute() => Task.CompletedTask;
}
