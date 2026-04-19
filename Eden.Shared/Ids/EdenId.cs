namespace Eden.Shared.Ids;

/// <summary>
/// A 128-bit identifier tagged by its role in the domain. The tag parameter
/// prevents e.g. a <see cref="UserId"/> being silently used where a
/// <see cref="PrimId"/> is expected. Backed by <see cref="Guid"/> for wire
/// compactness and easy debugging.
/// </summary>
/// <typeparam name="TTag">Marker type carrying the role; never instantiated.</typeparam>
public readonly record struct EdenId<TTag>(Guid Value)
{
    public static readonly EdenId<TTag> Empty = new(Guid.Empty);

    public static EdenId<TTag> New() => new(Guid.NewGuid());

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString("N");
}

// Role tags. Empty marker types — never instantiated.
public readonly struct UserTag    { }
public readonly struct WorldTag   { }
public readonly struct RegionTag  { }
public readonly struct PrimTag    { }
public readonly struct ItemTag    { }      // inventory items
public readonly struct AssetTag   { }      // meshes, textures, sounds, etc.
public readonly struct SessionTag { }
