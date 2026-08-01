using Importer.Core.Models;

namespace Importer.Core.Processing;

public sealed record Tpt2CurveFitResult(
    IReadOnlyList<NoLimitsSample> Samples,
    Tpt2CurveFitMetrics Metrics);
