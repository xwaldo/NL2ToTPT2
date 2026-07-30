namespace Importer.Core.Geometry;

public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 Zero => new(0, 0, 0);

    public double Length => Math.Sqrt(LengthSquared);

    public double LengthSquared => Dot(this, this);

    public bool IsFinite =>
        double.IsFinite(X) &&
        double.IsFinite(Y) &&
        double.IsFinite(Z);

    public Vec3 Normalized()
    {
        var length = Length;
        if (length <= 1e-12)
        {
            throw new InvalidOperationException("A zero-length vector cannot be normalized.");
        }

        return this / length;
    }

    public static double Dot(Vec3 left, Vec3 right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    public static Vec3 Cross(Vec3 left, Vec3 right) =>
        new(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));

    public static Vec3 Lerp(Vec3 start, Vec3 end, double amount) =>
        start + ((end - start) * amount);

    public static Vec3 operator +(Vec3 left, Vec3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static Vec3 operator -(Vec3 left, Vec3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static Vec3 operator -(Vec3 value) =>
        new(-value.X, -value.Y, -value.Z);

    public static Vec3 operator *(Vec3 value, double scale) =>
        new(value.X * scale, value.Y * scale, value.Z * scale);

    public static Vec3 operator /(Vec3 value, double divisor) =>
        new(value.X / divisor, value.Y / divisor, value.Z / divisor);
}

