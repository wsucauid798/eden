using Eden.Shared.Entities;
using Eden.Shared.Ids;
using Eden.Shared.Math;
using Eden.Shared.Wire;

namespace Eden.Tests;

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
    public void Serialisation_Is_Compact()
    {
        // Regression guard: Eden's custom formatters serialize records as
        // positional arrays (no property-name overhead). A typical AvatarState
        // encodes in <120 bytes. If this fails, someone introduced a name-keyed
        // formatter or a heavy field.
        var minimal = new AvatarState(
            EdenId<UserTag>.New(),
            EdenId<SessionTag>.New(),
            "Eve",
            Transform.Identity,
            Vector3.Zero,
            0);
        Assert.InRange(WireFormat.Serialize(minimal).Length, 1, 120);

        var prim = new PrimState(
            EdenId<PrimTag>.New(),
            EdenId<UserTag>.New(),
            Transform.Identity,
            new Vector3(2f, 2f, 2f),
            EdenId<AssetTag>.New(),
            new Color(200, 100, 50, 255),
            PrimFlags.Physical);
        Assert.InRange(WireFormat.Serialize(prim).Length, 1, 140);
    }
}
