using Eden.Shared.Entities;
using Eden.Shared.Ids;
using Eden.Shared.Math;
using Eden.Shared.Wire;

namespace Eden.Shared.Tests;

public class WireRoundTripTests
{
    [Fact]
    public void Vector3_RoundTrip()
    {
        var v = new Vector3(1.5f, -2.25f, 3.75f);
        var bytes = WireFormat.Serialize(v);
        var back  = WireFormat.Deserialize<Vector3>(bytes);
        Assert.Equal(v, back);
    }

    [Fact]
    public void Quaternion_RoundTrip()
    {
        var q = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
        var bytes = WireFormat.Serialize(q);
        var back  = WireFormat.Deserialize<Quaternion>(bytes);
        Assert.Equal(q, back);
    }

    [Fact]
    public void EdenId_RoundTrip()
    {
        var id = EdenId<UserTag>.New();
        var bytes = WireFormat.Serialize(id);
        var back  = WireFormat.Deserialize<EdenId<UserTag>>(bytes);
        Assert.Equal(id, back);
    }

    [Fact]
    public void AvatarState_RoundTrip()
    {
        var state = new AvatarState(
            UserId:         EdenId<UserTag>.New(),
            SessionId:      EdenId<SessionTag>.New(),
            DisplayName:    "Eve",
            Transform:      new Transform(new Vector3(10f, 20f, 30f), Quaternion.Identity),
            Velocity:       new Vector3(0.5f, 0f, 0f),
            AppearanceHash: 0xDEADBEEF);

        var bytes = WireFormat.Serialize(state);
        var back  = WireFormat.Deserialize<AvatarState>(bytes);

        Assert.Equal(state, back);
    }

    [Fact]
    public void PrimState_RoundTrip()
    {
        var state = new PrimState(
            Id:           EdenId<PrimTag>.New(),
            OwnerId:      EdenId<UserTag>.New(),
            Transform:    Transform.Identity,
            Scale:        new Vector3(2f, 2f, 2f),
            ShapeAssetId: EdenId<AssetTag>.New(),
            TintColor:    new Color(200, 100, 50, 255),
            Flags:        PrimFlags.Physical | PrimFlags.Locked);

        var bytes = WireFormat.Serialize(state);
        var back  = WireFormat.Deserialize<PrimState>(bytes);

        Assert.Equal(state, back);
        Assert.True(back.Flags.HasFlag(PrimFlags.Physical));
        Assert.True(back.Flags.HasFlag(PrimFlags.Locked));
    }

    [Fact]
    public void Serialisation_Is_Bounded()
    {
        // Loose sanity bound. The contractless resolver encodes property names
        // (inflates the payload); a future custom-formatter pass should cut
        // typical AvatarState below ~120 bytes. If this fails, someone
        // introduced something expensive (a big string, untrimmed arrays).
        var state = new AvatarState(
            EdenId<UserTag>.New(),
            EdenId<SessionTag>.New(),
            "Eve",
            Transform.Identity,
            Vector3.Zero,
            0);

        var bytes = WireFormat.Serialize(state);
        Assert.InRange(bytes.Length, 1, 400);
    }
}
