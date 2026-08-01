namespace Importer.Core.Processing;

public sealed record Tpt2CurveFitMetrics(
    double MaximumDeviation,
    double RootMeanSquareDeviation,
    double MaximumRollDeviation,
    double MinimumSectionLength,
    double MaximumSectionLength,
    double AverageSectionLength,
    int SimplifiedStraightSectionCount);
