using Importer.Core.Geometry;
using Importer.Core.Models;

namespace Importer.Core.Processing;

public sealed class TrackResampler
{
    public IReadOnlyList<NoLimitsSample> Resample(
        IReadOnlyList<NoLimitsSample> samples,
        double targetSpacing)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count < 2)
        {
            throw new ArgumentException("At least two samples are required.", nameof(samples));
        }

        if (!double.IsFinite(targetSpacing) || targetSpacing <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetSpacing),
                "Target spacing must be a finite positive number.");
        }

        var cumulativeDistances = new double[samples.Count];
        for (var index = 1; index < samples.Count; index++)
        {
            var segmentLength = (samples[index].Position - samples[index - 1].Position).Length;
            cumulativeDistances[index] = cumulativeDistances[index - 1] + segmentLength;
        }

        var totalLength = cumulativeDistances[^1];
        if (totalLength <= 1e-9)
        {
            throw new InvalidDataException("The track has no measurable length.");
        }

        var result = new List<NoLimitsSample>
        {
            samples[0],
        };

        var segmentIndex = 1;
        for (var distance = targetSpacing; distance < totalLength; distance += targetSpacing)
        {
            while (segmentIndex < cumulativeDistances.Length - 1 &&
                   cumulativeDistances[segmentIndex] < distance)
            {
                segmentIndex++;
            }

            var segmentStartDistance = cumulativeDistances[segmentIndex - 1];
            var segmentLength = cumulativeDistances[segmentIndex] - segmentStartDistance;
            if (segmentLength <= 1e-12)
            {
                continue;
            }

            var amount = (distance - segmentStartDistance) / segmentLength;
            result.Add(Interpolate(samples[segmentIndex - 1], samples[segmentIndex], amount));
        }

        if ((result[^1].Position - samples[^1].Position).Length > 1e-9)
        {
            result.Add(samples[^1]);
        }

        return result;
    }

    private static NoLimitsSample Interpolate(
        NoLimitsSample start,
        NoLimitsSample end,
        double amount) =>
        new(
            amount < 0.5 ? start.SourceIndex : end.SourceIndex,
            Vec3.Lerp(start.Position, end.Position, amount),
            Vec3.Lerp(start.Front, end.Front, amount).Normalized(),
            Vec3.Lerp(start.Left, end.Left, amount).Normalized(),
            Vec3.Lerp(start.Up, end.Up, amount).Normalized());
}

