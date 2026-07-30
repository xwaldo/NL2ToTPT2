namespace Importer.Windows.Calibration;

public sealed record SavedCalibrationProfile
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.UtcNow;

    public Dictionary<CalibrationTarget, NormalizedPoint> Points { get; init; } = [];
}

