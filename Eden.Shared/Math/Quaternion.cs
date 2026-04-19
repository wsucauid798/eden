namespace Eden.Shared.Math;

/// <summary>
/// A unit quaternion rotation (X, Y, Z, W). W is the scalar component.
/// Callers are responsible for keeping it unit-length; no auto-normalisation
/// happens on construction.
/// </summary>
public readonly record struct Quaternion(float X, float Y, float Z, float W)
{
    public static readonly Quaternion Identity = new(0f, 0f, 0f, 1f);

    public float LengthSquared => X * X + Y * Y + Z * Z + W * W;
    public float Length        => MathF.Sqrt(LengthSquared);

    public Quaternion Normalised
    {
        get
        {
            var len = Length;
            return len > 0f
                ? new Quaternion(X / len, Y / len, Z / len, W / len)
                : Identity;
        }
    }

    public Quaternion Conjugate => new(-X, -Y, -Z, W);

    public static Quaternion operator *(Quaternion a, Quaternion b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
}
