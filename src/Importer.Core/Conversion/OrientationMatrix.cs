using Importer.Core.Geometry;
using Importer.Core.Models;

namespace Importer.Core.Conversion;

/// <summary>
/// An orthonormal target-world orientation represented by right, up, and
/// forward matrix columns.
/// </summary>
public readonly record struct OrientationMatrix(Vec3 Right, Vec3 Up, Vec3 Forward)
{
    public static OrientationMatrix FromRobloxYawPitchRoll(EulerAngles rotation)
    {
        var yaw = DegreesToRadians(rotation.Yaw);
        var pitch = DegreesToRadians(rotation.Pitch);
        var roll = DegreesToRadians(rotation.Roll);
        var cosineYaw = Math.Cos(yaw);
        var sineYaw = Math.Sin(yaw);
        var cosinePitch = Math.Cos(pitch);
        var sinePitch = Math.Sin(pitch);
        var cosineRoll = Math.Cos(roll);
        var sineRoll = Math.Sin(roll);

        var right = new Vec3(
            (cosineYaw * cosineRoll) + (sineYaw * sinePitch * sineRoll),
            cosinePitch * sineRoll,
            (-sineYaw * cosineRoll) + (cosineYaw * sinePitch * sineRoll));
        var up = new Vec3(
            (-cosineYaw * sineRoll) + (sineYaw * sinePitch * cosineRoll),
            cosinePitch * cosineRoll,
            (sineYaw * sineRoll) + (cosineYaw * sinePitch * cosineRoll));
        var back = new Vec3(
            sineYaw * cosinePitch,
            -sinePitch,
            cosineYaw * cosinePitch);

        return new OrientationMatrix(right, up, -back);
    }

    public Vec3 WorldVectorToLocal(Vec3 worldVector) =>
        new(
            Vec3.Dot(worldVector, Right),
            Vec3.Dot(worldVector, Up),
            Vec3.Dot(worldVector, Forward));

    public Vec3 LocalVectorToWorld(Vec3 localVector) =>
        (Right * localVector.X) +
        (Up * localVector.Y) +
        (Forward * localVector.Z);

    public static OrientationMatrix FromNoLimitsBasis(
        Vec3 mappedFront,
        Vec3 mappedLeft,
        Vec3 mappedUp)
    {
        var forward = mappedFront.Normalized();
        var rightCandidate = -mappedLeft.Normalized();
        var projectedRight = rightCandidate - (forward * Vec3.Dot(rightCandidate, forward));

        if (projectedRight.LengthSquared <= 1e-12)
        {
            projectedRight = Vec3.Cross(mappedUp, forward);
        }

        var right = projectedRight.Normalized();
        var up = Vec3.Cross(forward, right).Normalized();

        if (Vec3.Dot(up, mappedUp) < 0.0)
        {
            right = -right;
            up = -up;
        }

        return new OrientationMatrix(right, up, forward);
    }

    /// <summary>
    /// Extracts yaw about world Y, pitch from the forward elevation, and roll
    /// about the forward axis. Calibration can later adjust axis/sign mapping
    /// without changing this matrix-based conversion.
    /// </summary>
    public EulerAngles ToYawPitchRoll()
    {
        var clampedY = Math.Clamp(Forward.Y, -1.0, 1.0);
        var pitch = Math.Asin(clampedY);
        var horizontalLength = Math.Sqrt(
            (Forward.X * Forward.X) +
            (Forward.Z * Forward.Z));

        double yaw;
        Vec3 referenceRight;
        if (horizontalLength <= 1e-10)
        {
            yaw = Math.Atan2(-Right.Z, Right.X);
            referenceRight = new Vec3(Math.Cos(yaw), 0.0, -Math.Sin(yaw));
        }
        else
        {
            yaw = Math.Atan2(Forward.X, Forward.Z);
            referenceRight = new Vec3(Forward.Z / horizontalLength, 0.0, -Forward.X / horizontalLength);
        }

        var referenceUp = Vec3.Cross(Forward, referenceRight).Normalized();
        var roll = Math.Atan2(
            Vec3.Dot(Right, referenceUp),
            Vec3.Dot(Right, referenceRight));

        const double radiansToDegrees = 180.0 / Math.PI;
        return new EulerAngles(
            AngleMath.Normalize(yaw * radiansToDegrees),
            AngleMath.Normalize(pitch * radiansToDegrees),
            AngleMath.Normalize(roll * radiansToDegrees));
    }

    private static double DegreesToRadians(double degrees) =>
        degrees * (Math.PI / 180.0);
}
