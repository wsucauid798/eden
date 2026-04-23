namespace Eden.Shared.World;

/// <summary>
/// Shared world-space conventions for Eden domain transforms.
/// </summary>
/// <remarks>
/// Eden is not trying to make every renderer invent its own coordinate or
/// ground rules. Server, physics, scripting, networking, and viewers should
/// treat these values as the base contract unless a future terrain service
/// supplies a more specific surface height for a position.
/// </remarks>
public static class WorldConventions
{
    /// <summary>One Eden world unit is one metre.</summary>
    public const float MetresPerUnit = 1f;

    /// <summary>
    /// Eden world transforms are Y-up. X and Z are the horizontal axes.
    /// Godot already uses this shape; OpenSim-compatible import/export code
    /// should map OpenSim's vertical Z into Eden's Y at the boundary.
    /// </summary>
    public const int UpAxisIndex = 1;

    /// <summary>
    /// Flat worlds use Y=0 as the terrain datum. Once terrain heightmaps land,
    /// "ground" becomes TerrainHeightAt(x, z); this value remains the default
    /// height for an empty flat region.
    /// </summary>
    public const float DefaultGroundPlaneY = 0f;

    /// <summary>
    /// Default water datum for flat regions. Disabled worlds still carry this
    /// value so enabling water later has a stable baseline.
    /// </summary>
    public const float DefaultWaterHeightY = 0f;

    /// <summary>
    /// Compatibility-minded default region edge length in metres. OpenSim and
    /// Second Life style regions commonly use 256m as the default local region
    /// span; larger worlds should compose or resize regions deliberately.
    /// </summary>
    public const float DefaultRegionSizeMetres = 256f;

    /// <summary>
    /// Temporary flat-terrain visualization size for viewers before terrain
    /// streaming exists. This is presentation scaffolding, not a simulation
    /// boundary.
    /// </summary>
    public const float FlatTerrainPreviewSizeMetres = DefaultRegionSizeMetres * 16f;

    /// <summary>Default checker size used by the developer flat-ground material.</summary>
    public const float FlatTerrainCheckerSizeMetres = 5f;

    /// <summary>Number of checker tiles baked into the generated texture.</summary>
    public const int FlatTerrainChecksPerTextureSide = 4;

    /// <summary>
    /// MVP avatar capsule/cube height in metres. The avatar transform position
    /// is the avatar root/feet point, not the visual centre.
    /// </summary>
    public const float DefaultAvatarHeightMetres = 1f;

    /// <summary>Visual/collision centre offset for the default avatar body.</summary>
    public const float DefaultAvatarCentreOffsetY = DefaultAvatarHeightMetres * 0.5f;
}
