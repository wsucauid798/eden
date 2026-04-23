namespace Eden.Shared.Math;

/// <summary>
/// A rigid-body transform: position + orientation in Eden world space.
/// Positions are metres, Y is up, and scale is intentionally omitted —
/// scale lives on the geometry (prim shape), not the pose.
/// </summary>
public readonly record struct Transform(Vector3 Position, Quaternion Rotation)
{
    public static readonly Transform Identity = new(Vector3.Zero, Quaternion.Identity);
}
