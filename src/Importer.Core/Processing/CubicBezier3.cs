using Importer.Core.Geometry;

namespace Importer.Core.Processing;

internal readonly record struct CubicBezier3(
    Vec3 Start,
    Vec3 Control1,
    Vec3 Control2,
    Vec3 End)
{
    public Vec3 Evaluate(double amount)
    {
        var t = Math.Clamp(amount, 0.0, 1.0);
        var inverse = 1.0 - t;
        return
            (Start * (inverse * inverse * inverse)) +
            (Control1 * (3.0 * inverse * inverse * t)) +
            (Control2 * (3.0 * inverse * t * t)) +
            (End * (t * t * t));
    }

    public Vec3 StartCurvatureVector() =>
        CurvatureVector(
            (Control1 - Start) * 3.0,
            (Start - (Control1 * 2.0) + Control2) * 6.0);

    public Vec3 EndCurvatureVector() =>
        CurvatureVector(
            (End - Control2) * 3.0,
            (End - (Control2 * 2.0) + Control1) * 6.0);

    public ArcLengthLookup CreateArcLengthLookup(int subdivisionCount = 48)
    {
        if (subdivisionCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(subdivisionCount));
        }

        var parameters = new double[subdivisionCount + 1];
        var distances = new double[subdivisionCount + 1];
        var previous = Start;
        for (var index = 1; index <= subdivisionCount; index++)
        {
            var parameter = index / (double)subdivisionCount;
            var point = Evaluate(parameter);
            parameters[index] = parameter;
            distances[index] = distances[index - 1] + (point - previous).Length;
            previous = point;
        }

        return new ArcLengthLookup(this, parameters, distances);
    }

    private static Vec3 CurvatureVector(Vec3 velocity, Vec3 acceleration)
    {
        var speedSquared = velocity.LengthSquared;
        if (speedSquared <= 1e-12)
        {
            return Vec3.Zero;
        }

        var tangentialAcceleration =
            velocity * (Vec3.Dot(velocity, acceleration) / speedSquared);
        return (acceleration - tangentialAcceleration) / speedSquared;
    }

    internal sealed class ArcLengthLookup(
        CubicBezier3 curve,
        double[] parameters,
        double[] distances)
    {
        public Vec3 EvaluateAtFraction(double arcFraction)
        {
            var fraction = Math.Clamp(arcFraction, 0.0, 1.0);
            var totalLength = distances[^1];
            if (totalLength <= 1e-12)
            {
                return curve.Start;
            }

            var targetDistance = totalLength * fraction;
            var upperIndex = Array.BinarySearch(distances, targetDistance);
            if (upperIndex >= 0)
            {
                return curve.Evaluate(parameters[upperIndex]);
            }

            upperIndex = ~upperIndex;
            if (upperIndex <= 0)
            {
                return curve.Start;
            }

            if (upperIndex >= distances.Length)
            {
                return curve.End;
            }

            var lowerIndex = upperIndex - 1;
            var distanceSpan = distances[upperIndex] - distances[lowerIndex];
            var interpolation = distanceSpan <= 1e-12
                ? 0.0
                : (targetDistance - distances[lowerIndex]) / distanceSpan;
            var parameter =
                parameters[lowerIndex] +
                ((parameters[upperIndex] - parameters[lowerIndex]) * interpolation);
            return curve.Evaluate(parameter);
        }
    }
}
