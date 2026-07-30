using Importer.Windows.Windowing;

namespace Importer.Windows.Input;

public static class MouseTrajectory
{
    public static TimeSpan CalculateResponsiveDuration(
        PixelPoint start,
        PixelPoint end,
        TimeSpan minimumDuration,
        double preferredPixelsPerSecond,
        double maximumPixelsPerSecond,
        TimeSpan maximumPreferredDuration)
    {
        if (maximumPreferredDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPreferredDuration));
        }

        if (preferredPixelsPerSecond > maximumPixelsPerSecond)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preferredPixelsPerSecond),
                "The preferred mouse speed cannot exceed the maximum mouse speed.");
        }

        var maximumSpeedDuration = CalculateDuration(
            start,
            end,
            minimumDuration,
            maximumPixelsPerSecond);
        var preferredSpeedDuration = CalculateDuration(
            start,
            end,
            minimumDuration,
            preferredPixelsPerSecond);
        var cappedPreferredDuration = preferredSpeedDuration < maximumPreferredDuration
            ? preferredSpeedDuration
            : maximumPreferredDuration;

        return maximumSpeedDuration > cappedPreferredDuration
            ? maximumSpeedDuration
            : cappedPreferredDuration;
    }

    public static TimeSpan CalculateDuration(
        PixelPoint start,
        PixelPoint end,
        TimeSpan minimumDuration,
        double maximumPixelsPerSecond)
    {
        if (minimumDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumDuration));
        }

        if (!double.IsFinite(maximumPixelsPerSecond) ||
            maximumPixelsPerSecond <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPixelsPerSecond));
        }

        var deltaX = (double)end.X - start.X;
        var deltaY = (double)end.Y - start.Y;
        var distanceDuration = TimeSpan.FromSeconds(
            Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY)) /
            maximumPixelsPerSecond);
        return distanceDuration > minimumDuration
            ? distanceDuration
            : minimumDuration;
    }

    public static IReadOnlyList<TimedMousePoint> Create(
        PixelPoint start,
        PixelPoint end,
        TimeSpan duration,
        TimeSpan sampleInterval)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        if (sampleInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleInterval));
        }

        if (duration == TimeSpan.Zero)
        {
            return [new TimedMousePoint(TimeSpan.Zero, end)];
        }

        var sampleCountValue = Math.Ceiling(duration.Ticks / (double)sampleInterval.Ticks);
        if (sampleCountValue > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "The mouse movement duration is too large.");
        }

        var sampleCount = Math.Max(1, (int)sampleCountValue);
        var points = new TimedMousePoint[sampleCount];
        for (var index = 1; index <= sampleCount; index++)
        {
            var progress = index / (double)sampleCount;
            var easedProgress = SmoothStep(progress);
            points[index - 1] = new TimedMousePoint(
                TimeSpan.FromTicks(
                    (long)Math.Round(duration.Ticks * progress)),
                new PixelPoint(
                    Interpolate(start.X, end.X, easedProgress),
                    Interpolate(start.Y, end.Y, easedProgress)));
        }

        points[^1] = new TimedMousePoint(duration, end);
        return points;
    }

    private static double SmoothStep(double progress) =>
        progress * progress * (3.0 - (2.0 * progress));

    private static int Interpolate(int start, int end, double progress) =>
        (int)Math.Round(start + ((end - start) * progress));
}

public readonly record struct TimedMousePoint(TimeSpan Offset, PixelPoint Point);
