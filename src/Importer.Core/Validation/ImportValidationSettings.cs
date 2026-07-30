namespace Importer.Core.Validation;

public sealed record ImportValidationSettings
{
    public double MinimumSectionLength { get; init; } = 0.01;

    public double MaximumSectionLength { get; init; } = 10.0;

    public double MaximumOrientationChange { get; init; } = 45.0;

    public int MaximumSectionCount { get; init; } = 10_000;
}

