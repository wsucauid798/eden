namespace Eden.Shared.Math;

/// <summary>
/// An 8-bit-per-channel RGBA colour. For HDR / linear work use a separate
/// float-channel type (not yet defined).
/// </summary>
public readonly record struct Color(byte R, byte G, byte B, byte A)
{
    public static readonly Color White       = new(255, 255, 255, 255);
    public static readonly Color Black       = new(0,   0,   0,   255);
    public static readonly Color Transparent = new(0,   0,   0,   0);

    /// <summary>Pack as 0xRRGGBBAA.</summary>
    public uint ToUInt32() => ((uint)R << 24) | ((uint)G << 16) | ((uint)B << 8) | A;

    public static Color FromUInt32(uint packed) => new(
        (byte)((packed >> 24) & 0xFF),
        (byte)((packed >> 16) & 0xFF),
        (byte)((packed >>  8) & 0xFF),
        (byte)( packed        & 0xFF));
}
