namespace Importer.Core.Validation;

public sealed record ImportValidationSettings
{
    public double MinimumSectionLength { get; init; } = 4.0;

    public double MaximumSectionLength { get; init; } = 95.0;

    public double MaximumOrientationChange { get; init; } = 45.0;

    public int MaximumSectionCount { get; init; } = 10_000;
}
