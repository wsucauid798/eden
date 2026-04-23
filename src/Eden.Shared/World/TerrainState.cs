namespace Eden.Shared.World;

/// <summary>
/// Authoritative terrain contract for a world or region.
/// </summary>
/// <remarks>
/// Eden currently ships a flat terrain mode, but this record is the place
/// where heightmap, procedural, or imported OpenSim terrain surfaces can be
/// added without every viewer inventing its own ground rules.
/// </remarks>
public readonly record struct TerrainState(
    TerrainKind Kind,
    float BaseHeightY,
    float RegionSizeMetres,
    float WaterHeightY,
    bool WaterEnabled)
{
    public static TerrainState FlatDefault => new(
        Kind: TerrainKind.Flat,
        BaseHeightY: WorldConventions.DefaultGroundPlaneY,
        RegionSizeMetres: WorldConventions.DefaultRegionSizeMetres,
        WaterHeightY: WorldConventions.DefaultWaterHeightY,
        WaterEnabled: false);

    public TerrainState Normalized()
    {
        var defaults = FlatDefault;
        return this with
        {
            BaseHeightY = float.IsFinite(BaseHeightY) ? BaseHeightY : defaults.BaseHeightY,
            RegionSizeMetres = RegionSizeMetres > 0f ? RegionSizeMetres : defaults.RegionSizeMetres,
            WaterHeightY = float.IsFinite(WaterHeightY) ? WaterHeightY : defaults.WaterHeightY,
        };
    }

    public float HeightAt(float x, float z) => Kind switch
    {
        TerrainKind.Flat => BaseHeightY,
        _ => BaseHeightY,
    };
}

public enum TerrainKind : byte
{
    Flat = 0,
}
