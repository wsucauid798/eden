namespace Eden.Shared.Math;

/// <summary>
/// A 3-component single-precision vector.
/// Eden world-space transforms use metres with Y as the up axis; see
/// <see cref="Eden.Shared.World.WorldConventions"/> for the domain contract.
/// </summary>
public readonly record struct Vector3(float X, float Y, float Z)
{
    public static readonly Vector3 Zero  = new(0f, 0f, 0f);
    public static readonly Vector3 One   = new(1f, 1f, 1f);
    public static readonly Vector3 UnitX = new(1f, 0f, 0f);
    public static readonly Vector3 UnitY = new(0f, 1f, 0f);
    public static readonly Vector3 UnitZ = new(0f, 0f, 1f);

    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3 operator -(Vector3 v)            => new(-v.X, -v.Y, -v.Z);
    public static Vector3 operator *(Vector3 v, float s)   => new(v.X * s, v.Y * s, v.Z * s);
    public static Vector3 operator *(float s, Vector3 v)   => v * s;
    public static Vector3 operator /(Vector3 v, float s)   => new(v.X / s, v.Y / s, v.Z / s);

    public float LengthSquared => X * X + Y * Y + Z * Z;
    public float Length        => MathF.Sqrt(LengthSquared);

    public static float Dot(Vector3 a, Vector3 b)   => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vector3 Cross(Vector3 a, Vector3 b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);
}
