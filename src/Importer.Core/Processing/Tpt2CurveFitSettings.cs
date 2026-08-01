namespace Importer.Core.Processing;

public sealed record Tpt2CurveFitSettings
{
    public double MaximumDeviation { get; init; } = 0.5;

    public double MaximumRollDeviation { get; init; } = 2.0;

    public double MaximumRollChange { get; init; } = 15.0;

    public double HandleLengthFactor { get; init; } = 0.39;

    public double MaximumSectionLength { get; init; } = 95.0;

    public double MinimumSectionLength { get; init; } = 4.0;

    public double PreferredMinimumSectionLength { get; init; } = 6.0;

    public double TargetSectionLength { get; init; } = 15.0;

    public double PreferredMaximumAdjacentLengthRatio { get; init; } = 1.5;

    public double MinimumStraightRunLength { get; init; } = 45.0;

    public double StraightDirectionRateTolerance { get; init; } = 0.01;

    public double StraightRollRateTolerance { get; init; } = 0.01;

    public double StraightAlignmentTolerance { get; init; } = 0.25;

    public void Validate()
    {
        ValidatePositive(MaximumDeviation, nameof(MaximumDeviation));
        ValidatePositive(MaximumRollDeviation, nameof(MaximumRollDeviation));
        ValidatePositive(MaximumRollChange, nameof(MaximumRollChange));
        ValidatePositive(HandleLengthFactor, nameof(HandleLengthFactor));
        ValidatePositive(MaximumSectionLength, nameof(MaximumSectionLength));
        ValidatePositive(MinimumSectionLength, nameof(MinimumSectionLength));
        ValidatePositive(
            PreferredMinimumSectionLength,
            nameof(PreferredMinimumSectionLength));
        ValidatePositive(TargetSectionLength, nameof(TargetSectionLength));
        ValidatePositive(
            PreferredMaximumAdjacentLengthRatio,
            nameof(PreferredMaximumAdjacentLengthRatio));
        ValidatePositive(MinimumStraightRunLength, nameof(MinimumStraightRunLength));
        ValidatePositive(
            StraightDirectionRateTolerance,
            nameof(StraightDirectionRateTolerance));
        ValidatePositive(StraightRollRateTolerance, nameof(StraightRollRateTolerance));
        ValidatePositive(StraightAlignmentTolerance, nameof(StraightAlignmentTolerance));

        if (TargetSectionLength > MaximumSectionLength)
        {
            throw new InvalidOperationException(
                "The target section length cannot exceed the maximum section length.");
        }

        if (MinimumSectionLength > PreferredMinimumSectionLength ||
            PreferredMinimumSectionLength > TargetSectionLength)
        {
            throw new InvalidOperationException(
                "Section lengths must satisfy minimum <= preferred minimum <= target.");
        }

        if (PreferredMaximumAdjacentLengthRatio < 1.0)
        {
            throw new InvalidOperationException(
                "The preferred adjacent section-length ratio cannot be less than 1.");
        }
    }

    private static void ValidatePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new InvalidOperationException($"{name} must be a finite positive number.");
        }
    }
}
