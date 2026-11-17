using Importer.Core.Conversion;
using Importer.Core.Geometry;
using Importer.Core.Models;

namespace Importer.Core.Processing;

public sealed class Tpt2CurveFitter
{
    private const int MinimumUniformComparisonCount = 16;
    private const int MaximumUniformComparisonCount = 64;
    private const int BoundaryRefinementPasses = 3;

    public Tpt2CurveFitResult Fit(
        IReadOnlyList<NoLimitsSample> samples,
        ImportConversionProfile conversionProfile,
        double positionScale,
        Tpt2CurveFitSettings settings,
        InputPrecisionMode inputPrecision = InputPrecisionMode.Full)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(conversionProfile);
        ArgumentNullException.ThrowIfNull(settings);
        if (samples.Count < 2)
        {
            throw new ArgumentException("At least two samples are required.", nameof(samples));
        }

        if (!double.IsFinite(positionScale) || positionScale <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(positionScale),
                "Position scale must be a finite positive number.");
        }

        if (!Enum.IsDefined(inputPrecision))
        {
            throw new ArgumentOutOfRangeException(nameof(inputPrecision));
        }

        conversionProfile.Validate();
        settings.Validate();

        var targetSamples = ConvertSamples(
            samples,
            conversionProfile,
            positionScale,
            inputPrecision);
        if (targetSamples[^1].CumulativeDistance <= 1e-9)
        {
            throw new InvalidDataException("The track has no measurable length after scaling.");
        }

        var straightRuns = DetectStraightRuns(targetSamples, settings, out var straightEdges);
        var expandedSamples = ExpandStraightGaps(samples, targetSamples, straightEdges, straightRuns, settings);
        if (expandedSamples is not null)
        {
            samples = expandedSamples;
            targetSamples = ConvertSamples(samples, conversionProfile, positionScale, inputPrecision);
            straightRuns = DetectStraightRuns(targetSamples, settings, out _);
        }

        var selectedSegments = SelectSegments(targetSamples, straightRuns, settings);
        var selectedIndexes = new List<int>(selectedSegments.Count + 1)
        {
            selectedSegments[0].StartIndex,
        };
        selectedIndexes.AddRange(selectedSegments.Select(segment => segment.EndIndex));
        var straightFlags = selectedSegments.Select(segment => segment.IsStraightRun).ToList();

        EnforceMinimumSectionLengths(
            targetSamples,
            selectedIndexes,
            straightFlags,
            settings);
        RefineBoundaries(targetSamples, selectedIndexes, straightFlags, settings);

        var evaluations = new List<SegmentEvaluation>(straightFlags.Count);
        for (var index = 0; index < straightFlags.Count; index++)
        {
            evaluations.Add(EvaluateSegment(
                targetSamples,
                selectedIndexes[index],
                selectedIndexes[index + 1],
                settings));
        }

        var totalSquaredDeviation = evaluations.Sum(result => result.SumSquaredDeviation);
        var comparisonCount = evaluations.Sum(result => result.ComparisonCount);
        var lengths = evaluations.Select(result => result.ChordLength).ToArray();
        var resultSamples = selectedIndexes.Select(index => samples[index]).ToArray();
        return new Tpt2CurveFitResult(
            resultSamples,
            new Tpt2CurveFitMetrics(
                evaluations.Max(result => result.MaximumDeviation),
                Math.Sqrt(totalSquaredDeviation / Math.Max(1, comparisonCount)),
                evaluations.Max(result => result.MaximumRollDeviation),
                lengths.Min(),
                lengths.Max(),
                lengths.Average(),
                straightFlags.Count(isStraight => isStraight)));
    }

    private static TargetSample[] ConvertSamples(
        IReadOnlyList<NoLimitsSample> samples,
        ImportConversionProfile conversionProfile,
        double positionScale,
        InputPrecisionMode inputPrecision)
    {
        var orientationConverter = new OrientationConverter(
            conversionProfile.OrientationCoordinates,
            conversionProfile.Rotation);
        var sourceOrigin = conversionProfile.PositionCoordinates.Map(samples[0].Position);
        var result = new TargetSample[samples.Count];
        var cumulativeDistance = 0.0;
        var accumulatedTwist = 0.0;
        OrientationMatrix? previousOrientation = null;

        for (var index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            var position =
                (conversionProfile.PositionCoordinates.Map(sample.Position) - sourceOrigin) *
                positionScale;
            var rotation = orientationConverter.Convert(sample);
            if (inputPrecision == InputPrecisionMode.Fixed)
            {
                rotation = new EulerAngles(
                    AngleMath.RoundRotation(rotation.Yaw),
                    AngleMath.RoundRotation(rotation.Pitch),
                    AngleMath.RoundRotation(rotation.Roll));
            }

            var orientation = OrientationMatrix.FromRobloxYawPitchRoll(rotation);
            if (index > 0)
            {
                cumulativeDistance += (position - result[index - 1].Position).Length;
                accumulatedTwist += CalculateFrameTwistDegrees(
                    previousOrientation!.Value,
                    orientation);
            }

            result[index] = new TargetSample(
                sample.SourceIndex,
                position,
                orientation.Forward.Normalized(),
                accumulatedTwist,
                cumulativeDistance);
            previousOrientation = orientation;
        }

        return result;
    }

    private static List<NoLimitsSample>? ExpandStraightGaps(
        IReadOnlyList<NoLimitsSample> samples,
        IReadOnlyList<TargetSample> targetSamples,
        IReadOnlyList<bool> straightEdges,
        IReadOnlyList<StraightRun> straightRuns,
        Tpt2CurveFitSettings settings)
    {
        List<NoLimitsSample>? expanded = null;
        var runIndex = 0;
        for (var index = 0; index < straightEdges.Count; index++)
        {
            while (runIndex < straightRuns.Count && straightRuns[runIndex].EndIndex <= index)
            {
                runIndex++;
            }

            var isInStraightRun = runIndex < straightRuns.Count && straightRuns[runIndex].StartIndex <= index;
            var start = targetSamples[index];
            var end = targetSamples[index + 1];
            var gap = (end.Position - start.Position).Length;
            var maximumLength = MaximumLength(isInStraightRun, settings);
            if (straightEdges[index] && gap > maximumLength + 1e-9 &&
                Math.Abs(end.AccumulatedTwist - start.AccumulatedTwist) <= settings.MaximumRollChange)
            {
                // Use equal pieces so a straight just over the limit doesn't leave a short tail.
                var partCount = Math.Ceiling(gap / maximumLength);
                expanded ??= samples.Take(index + 1).ToList();
                var remainingOriginalSamples = samples.Count - index - 1;
                if (partCount - 1 > Array.MaxLength - expanded.Count - remainingOriginalSamples)
                {
                    throw new InvalidDataException("The straight gap is too large to subdivide. Check the CSV coordinates and position scale.");
                }

                for (var part = 1; part < partCount; part++)
                {
                    expanded.Add(TrackResampler.Interpolate(samples[index], samples[index + 1], part / partCount));
                }
            }

            expanded?.Add(samples[index + 1]);
        }

        return expanded;
    }

    private static List<SelectedSegment> SelectSegments(
        IReadOnlyList<TargetSample> samples,
        IReadOnlyList<StraightRun> straightRuns,
        Tpt2CurveFitSettings settings)
    {
        var result = new List<SelectedSegment>();
        var currentIndex = 0;

        foreach (var run in straightRuns)
        {
            AppendRegionSegments(
                samples,
                currentIndex,
                run.StartIndex,
                isStraightRun: false,
                settings,
                result);
            AppendRegionSegments(
                samples,
                run.StartIndex,
                run.EndIndex,
                isStraightRun: true,
                settings,
                result);
            currentIndex = run.EndIndex;
        }

        AppendRegionSegments(
            samples,
            currentIndex,
            samples.Count - 1,
            isStraightRun: false,
            settings,
            result);

        if (result.Count == 0)
        {
            throw new InvalidDataException("Curve fitting did not produce any sections.");
        }

        return result;
    }

    private static IReadOnlyList<StraightRun> DetectStraightRuns(
        IReadOnlyList<TargetSample> samples,
        Tpt2CurveFitSettings settings,
        out bool[] straightEdges)
    {
        straightEdges = new bool[samples.Count - 1];
        for (var index = 0; index < straightEdges.Length; index++)
        {
            var start = samples[index];
            var end = samples[index + 1];
            var edge = end.Position - start.Position;
            var edgeLength = edge.Length;
            if (edgeLength <= 1e-9)
            {
                continue;
            }

            var edgeDirection = edge / edgeLength;
            var directionRate =
                AngleBetweenDegrees(start.Forward, end.Forward) / edgeLength;
            var rollRate =
                Math.Abs(end.AccumulatedTwist - start.AccumulatedTwist) /
                edgeLength;
            var alignment = Math.Max(
                AngleBetweenDegrees(edgeDirection, start.Forward),
                AngleBetweenDegrees(edgeDirection, end.Forward));
            straightEdges[index] =
                directionRate <= settings.StraightDirectionRateTolerance &&
                rollRate <= settings.StraightRollRateTolerance &&
                alignment <= settings.StraightAlignmentTolerance;
        }

        var runs = new List<StraightRun>();
        var edgeIndex = 0;
        while (edgeIndex < straightEdges.Length)
        {
            if (!straightEdges[edgeIndex])
            {
                edgeIndex++;
                continue;
            }

            var startIndex = edgeIndex;
            while (edgeIndex < straightEdges.Length && straightEdges[edgeIndex])
            {
                edgeIndex++;
            }

            var endIndex = edgeIndex;
            var runLength =
                samples[endIndex].CumulativeDistance -
                samples[startIndex].CumulativeDistance;
            if (runLength >= settings.MinimumStraightRunLength)
            {
                runs.Add(new StraightRun(startIndex, endIndex));
            }
        }

        return runs;
    }

    private static void AppendRegionSegments(
        IReadOnlyList<TargetSample> samples,
        int startIndex,
        int endIndex,
        bool isStraightRun,
        Tpt2CurveFitSettings settings,
        ICollection<SelectedSegment> result)
    {
        if (endIndex <= startIndex)
        {
            return;
        }

        var boundaries = CreateEvenBoundaries(
            samples,
            startIndex,
            endIndex,
            isStraightRun,
            settings);
        for (var index = 1; index < boundaries.Count; index++)
        {
            AppendValidatedSegment(
                samples,
                boundaries[index - 1],
                boundaries[index],
                isStraightRun,
                settings,
                result);
        }
    }

    private static IReadOnlyList<int> CreateEvenBoundaries(
        IReadOnlyList<TargetSample> samples,
        int startIndex,
        int endIndex,
        bool isStraightRun,
        Tpt2CurveFitSettings settings)
    {
        var regionLength =
            samples[endIndex].CumulativeDistance -
            samples[startIndex].CumulativeDistance;
        var maximumNormalLength =
            settings.TargetSectionLength *
            settings.PreferredMaximumAdjacentLengthRatio;
        var sectionCount = isStraightRun
            ? (int)Math.Ceiling(regionLength / settings.MaximumSectionLength)
            : Math.Max(
                1,
                (int)Math.Round(
                    regionLength / settings.TargetSectionLength,
                    MidpointRounding.AwayFromZero));
        if (!isStraightRun)
        {
            sectionCount = Math.Max(
                sectionCount,
                (int)Math.Ceiling(regionLength / maximumNormalLength));
        }

        sectionCount = Math.Clamp(sectionCount, 1, endIndex - startIndex);
        var result = new List<int>(sectionCount + 1) { startIndex };
        var previousIndex = startIndex;
        for (var sectionIndex = 1; sectionIndex < sectionCount; sectionIndex++)
        {
            var targetDistance =
                samples[startIndex].CumulativeDistance +
                (regionLength * sectionIndex / sectionCount);
            var remainingSections = sectionCount - sectionIndex;
            var boundaryIndex = FindNearestIndexAtDistance(
                samples,
                previousIndex + 1,
                endIndex - remainingSections,
                targetDistance);
            result.Add(boundaryIndex);
            previousIndex = boundaryIndex;
        }

        result.Add(endIndex);
        return result;
    }

    private static void AppendValidatedSegment(
        IReadOnlyList<TargetSample> samples,
        int startIndex,
        int endIndex,
        bool isStraightRun,
        Tpt2CurveFitSettings settings,
        ICollection<SelectedSegment> result)
    {
        var evaluation = EvaluateSegment(samples, startIndex, endIndex, settings);
        var maximumLength = MaximumLength(isStraightRun, settings);
        if (IsValid(evaluation, maximumLength, settings))
        {
            result.Add(new SelectedSegment(startIndex, endIndex, isStraightRun));
            return;
        }

        if (endIndex - startIndex <= 1)
        {
            if (evaluation.ChordLength > maximumLength + 1e-9)
            {
                throw new InvalidDataException(
                    $"Adjacent CSV samples {samples[startIndex].SourceIndex:N0} and {samples[endIndex].SourceIndex:N0} " +
                    $"are {evaluation.ChordLength:N3} game units apart, above the " +
                    $"{maximumLength:N3}-unit limit. Export a denser CSV.");
            }

            result.Add(new SelectedSegment(startIndex, endIndex, isStraightRun));
            return;
        }

        var preferredSplitIndex = evaluation.WorstSourceIndex;
        if (preferredSplitIndex <= startIndex || preferredSplitIndex >= endIndex)
        {
            var targetDistance =
                (samples[startIndex].CumulativeDistance +
                 samples[endIndex].CumulativeDistance) /
                2.0;
            preferredSplitIndex = FindNearestIndexAtDistance(
                samples,
                startIndex + 1,
                endIndex - 1,
                targetDistance);
        }

        var splitIndex = FindAllowedSplitIndex(
            samples,
            startIndex,
            endIndex,
            preferredSplitIndex,
            settings);
        if (splitIndex < 0)
        {
            result.Add(new SelectedSegment(startIndex, endIndex, isStraightRun));
            return;
        }

        AppendValidatedSegment(
            samples,
            startIndex,
            splitIndex,
            isStraightRun,
            settings,
            result);
        AppendValidatedSegment(
            samples,
            splitIndex,
            endIndex,
            isStraightRun,
            settings,
            result);
    }

    private static int FindAllowedSplitIndex(
        IReadOnlyList<TargetSample> samples,
        int startIndex,
        int endIndex,
        int preferredIndex,
        Tpt2CurveFitSettings settings)
    {
        var bestIndex = -1;
        var bestScore = double.PositiveInfinity;
        for (var candidateIndex = startIndex + 1;
             candidateIndex < endIndex;
             candidateIndex++)
        {
            var leftLength =
                (samples[candidateIndex].Position - samples[startIndex].Position).Length;
            var rightLength =
                (samples[endIndex].Position - samples[candidateIndex].Position).Length;
            if (leftLength < settings.MinimumSectionLength - 1e-9 ||
                rightLength < settings.MinimumSectionLength - 1e-9)
            {
                continue;
            }

            var score =
                Math.Abs(
                    samples[candidateIndex].CumulativeDistance -
                    samples[preferredIndex].CumulativeDistance) /
                settings.TargetSectionLength;
            score += PreferredMinimumPenalty(leftLength, settings);
            score += PreferredMinimumPenalty(rightLength, settings);
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = candidateIndex;
            }
        }

        return bestIndex;
    }

    private static void EnforceMinimumSectionLengths(
        IReadOnlyList<TargetSample> samples,
        List<int> selectedIndexes,
        List<bool> straightFlags,
        Tpt2CurveFitSettings settings)
    {
        while (TryFindShortSegment(
                   samples,
                   selectedIndexes,
                   settings.MinimumSectionLength,
                   out var shortSegmentIndex))
        {
            if (TryRebalanceShortSegment(
                    samples,
                    selectedIndexes,
                    straightFlags,
                    shortSegmentIndex,
                    settings))
            {
                continue;
            }

            if (TryMergeShortSegment(
                    samples,
                    selectedIndexes,
                    straightFlags,
                    shortSegmentIndex,
                    settings))
            {
                continue;
            }

            var length = SectionLength(samples, selectedIndexes, shortSegmentIndex);
            throw new InvalidDataException(
                $"The fitted track contains a {length:N3}-unit section that cannot be " +
                $"rebalanced to the {settings.MinimumSectionLength:N3}-unit minimum. " +
                "Export a denser CSV or adjust the source track near this transition.");
        }

        var blockedSegments = new HashSet<(int StartIndex, int EndIndex)>();
        var maximumAttempts = selectedIndexes.Count * 3;
        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            var shortSegmentIndex = FindPreferredShortSegment(
                samples,
                selectedIndexes,
                settings.PreferredMinimumSectionLength,
                blockedSegments);
            if (shortSegmentIndex < 0)
            {
                break;
            }

            if (TryRebalanceShortSegment(
                    samples,
                    selectedIndexes,
                    straightFlags,
                    shortSegmentIndex,
                    settings))
            {
                blockedSegments.Clear();
                continue;
            }

            blockedSegments.Add((
                selectedIndexes[shortSegmentIndex],
                selectedIndexes[shortSegmentIndex + 1]));
        }
    }

    private static bool TryFindShortSegment(
        IReadOnlyList<TargetSample> samples,
        IReadOnlyList<int> selectedIndexes,
        double minimumLength,
        out int segmentIndex)
    {
        segmentIndex = -1;
        var shortestLength = double.PositiveInfinity;
        for (var index = 0; index < selectedIndexes.Count - 1; index++)
        {
            var length = SectionLength(samples, selectedIndexes, index);
            if (length < minimumLength - 1e-9 && length < shortestLength)
            {
                shortestLength = length;
                segmentIndex = index;
            }
        }

        return segmentIndex >= 0;
    }

    private static int FindPreferredShortSegment(
        IReadOnlyList<TargetSample> samples,
        IReadOnlyList<int> selectedIndexes,
        double preferredLength,
        ISet<(int StartIndex, int EndIndex)> blockedSegments)
    {
        var result = -1;
        var shortestLength = double.PositiveInfinity;
        for (var index = 0; index < selectedIndexes.Count - 1; index++)
        {
            var key = (selectedIndexes[index], selectedIndexes[index + 1]);
            var length = SectionLength(samples, selectedIndexes, index);
            if (!blockedSegments.Contains(key) &&
                length < preferredLength - 1e-9 &&
                length < shortestLength)
            {
                result = index;
                shortestLength = length;
            }
        }

        return result;
    }

    private static bool TryRebalanceShortSegment(
        IReadOnlyList<TargetSample> samples,
        List<int> selectedIndexes,
        IReadOnlyList<bool> straightFlags,
        int shortSegmentIndex,
        Tpt2CurveFitSettings settings)
    {
        var currentLength = SectionLength(samples, selectedIndexes, shortSegmentIndex);
        BoundaryAdjustment? bestAdjustment = null;

        if (shortSegmentIndex > 0)
        {
            FindBoundaryAdjustment(
                samples,
                selectedIndexes,
                straightFlags,
                boundaryIndex: shortSegmentIndex,
                shortSegmentIndex,
                currentLength,
                settings,
                ref bestAdjustment);
        }

        if (shortSegmentIndex + 1 < straightFlags.Count)
        {
            FindBoundaryAdjustment(
                samples,
                selectedIndexes,
                straightFlags,
                boundaryIndex: shortSegmentIndex + 1,
                shortSegmentIndex,
                currentLength,
                settings,
                ref bestAdjustment);
        }

        if (bestAdjustment is not { } adjustment)
        {
            return false;
        }

        selectedIndexes[adjustment.BoundaryIndex] = adjustment.SampleIndex;
        return true;
    }

    private static void FindBoundaryAdjustment(
        IReadOnlyList<TargetSample> samples,
        IReadOnlyList<int> selectedIndexes,
        IReadOnlyList<bool> straightFlags,
        int boundaryIndex,
        int shortSegmentIndex,
        double currentShortLength,
        Tpt2CurveFitSettings settings,
        ref BoundaryAdjustment? bestAdjustment)
    {
        var outerStartIndex = selectedIndexes[boundaryIndex - 1];
        var outerEndIndex = selectedIndexes[boundaryIndex + 1];
        var leftFlag = straightFlags[boundaryIndex - 1];
        var rightFlag = straightFlags[boundaryIndex];
        var leftMaximum = MaximumLength(leftFlag, settings);
        var rightMaximum = MaximumLength(rightFlag, settings);

        for (var candidateIndex = outerStartIndex + 1;
             candidateIndex < outerEndIndex;
             candidateIndex++)
        {
            var left = EvaluateSegment(samples, outerStartIndex, candidateIndex, settings);
            var right = EvaluateSegment(samples, candidateIndex, outerEndIndex, settings);
            var adjustedShortLength = shortSegmentIndex == boundaryIndex
                ? right.ChordLength
                : left.ChordLength;
            if (adjustedShortLength <= currentShortLength + 1e-9 ||
                left.ChordLength < settings.MinimumSectionLength - 1e-9 ||
                right.ChordLength < settings.MinimumSectionLength - 1e-9 ||
                left.ChordLength > leftMaximum + 1e-9 ||
                right.ChordLength > rightMaximum + 1e-9)
            {
                continue;
            }

            var score =
                PreferredMinimumPenalty(left.ChordLength, settings) +
                PreferredMinimumPenalty(right.ChordLength, settings) +
                (left.RootMeanSquareDeviation + right.RootMeanSquareDeviation) /
                settings.MaximumDeviation +
                (left.MaximumRollDeviation + right.MaximumRollDeviation) /
                settings.MaximumRollDeviation +
                CurvatureMismatch(left.Curve, right.Curve) *
                settings.TargetSectionLength;
            if (!leftFlag)
            {
                score += TargetLengthPenalty(left.ChordLength, settings);
            }

            if (!rightFlag)
            {
                score += TargetLengthPenalty(right.ChordLength, settings);
            }

            if (!IsValid(left, leftMaximum, settings))
            {
                score += 100.0;
            }

            if (!IsValid(right, rightMaximum, settings))
            {
                score += 100.0;
            }

            if (bestAdjustment is null || score < bestAdjustment.Value.Score)
            {
                bestAdjustment = new BoundaryAdjustment(boundaryIndex, candidateIndex, score);
            }
        }
    }

    private static bool TryMergeShortSegment(
        IReadOnlyList<TargetSample> samples,
        List<int> selectedIndexes,
        List<bool> straightFlags,
        int shortSegmentIndex,
        Tpt2CurveFitSettings settings)
    {
        MergeCandidate? bestCandidate = null;
        if (shortSegmentIndex > 0)
        {
            ConsiderMerge(
                samples,
                selectedIndexes,
                straightFlags,
                firstSegmentIndex: shortSegmentIndex - 1,
                settings,
                ref bestCandidate);
        }

        if (shortSegmentIndex + 1 < straightFlags.Count)
        {
            ConsiderMerge(
                samples,
                selectedIndexes,
                straightFlags,
                firstSegmentIndex: shortSegmentIndex,
                settings,
                ref bestCandidate);
        }

        if (bestCandidate is not { } merge)
        {
            return false;
        }

        straightFlags[merge.FirstSegmentIndex] = merge.IsStraightRun;
        straightFlags.RemoveAt(merge.FirstSegmentIndex + 1);
        selectedIndexes.RemoveAt(merge.FirstSegmentIndex + 1);
        return true;
    }

    private static void ConsiderMerge(
        IReadOnlyList<TargetSample> samples,
        IReadOnlyList<int> selectedIndexes,
        IReadOnlyList<bool> straightFlags,
        int firstSegmentIndex,
        Tpt2CurveFitSettings settings,
        ref MergeCandidate? bestCandidate)
    {
        var isStraightRun =
            straightFlags[firstSegmentIndex] &&
            straightFlags[firstSegmentIndex + 1];
        var evaluation = EvaluateSegment(
            samples,
            selectedIndexes[firstSegmentIndex],
            selectedIndexes[firstSegmentIndex + 2],
            settings);
        var maximumLength = MaximumLength(isStraightRun, settings);
        if (evaluation.ChordLength < settings.MinimumSectionLength - 1e-9 ||
            evaluation.ChordLength > maximumLength + 1e-9)
        {
            return;
        }

        var score =
            evaluation.RootMeanSquareDeviation / settings.MaximumDeviation +
            evaluation.MaximumRollDeviation / settings.MaximumRollDeviation +
            PreferredMinimumPenalty(evaluation.ChordLength, settings);
        if (!IsValid(evaluation, maximumLength, settings))
        {
            score += 100.0;
        }

        if (bestCandidate is null || score < bestCandidate.Value.Score)
        {
            bestCandidate = new MergeCandidate(firstSegmentIndex, isStraightRun, score);
        }
    }

    private static double SectionLength(
        IReadOnlyList<TargetSample> samples,
        IReadOnlyList<int> selectedIndexes,
        int segmentIndex) =>
        (samples[selectedIndexes[segmentIndex + 1]].Position -
         samples[selectedIndexes[segmentIndex]].Position).Length;

    private static double MaximumLength(
        bool isStraightRun,
        Tpt2CurveFitSettings settings) =>
        isStraightRun
            ? settings.MaximumSectionLength
            : settings.TargetSectionLength *
              settings.PreferredMaximumAdjacentLengthRatio;

    private static double PreferredMinimumPenalty(
        double length,
        Tpt2CurveFitSettings settings) =>
        length >= settings.PreferredMinimumSectionLength
            ? 0.0
            : (settings.PreferredMinimumSectionLength - length) /
              settings.PreferredMinimumSectionLength *
              10.0;

    private static int FindNearestIndexAtDistance(
        IReadOnlyList<TargetSample> samples,
        int minimumIndex,
        int maximumIndex,
        double targetDistance)
    {
        if (minimumIndex > maximumIndex)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumIndex));
        }

        var lowerIndex = minimumIndex;
        var upperIndex = maximumIndex;
        while (lowerIndex < upperIndex)
        {
            var middleIndex = lowerIndex + ((upperIndex - lowerIndex) / 2);
            if (samples[middleIndex].CumulativeDistance < targetDistance)
            {
                lowerIndex = middleIndex + 1;
            }
            else
            {
                upperIndex = middleIndex;
            }
        }

        var candidateIndex = lowerIndex;
        if (candidateIndex > minimumIndex)
        {
            var previousIndex = candidateIndex - 1;
            var candidateDifference =
                Math.Abs(samples[candidateIndex].CumulativeDistance - targetDistance);
            var previousDifference =
                Math.Abs(samples[previousIndex].CumulativeDistance - targetDistance);
            if (previousDifference <= candidateDifference)
            {
                candidateIndex = previousIndex;
            }
        }

        return Math.Clamp(candidateIndex, minimumIndex, maximumIndex);
    }

    private static void RefineBoundaries(
        IReadOnlyList<TargetSample> samples,
        List<int> selectedIndexes,
        IReadOnlyList<bool> straightFlags,
        Tpt2CurveFitSettings settings)
    {
        if (selectedIndexes.Count <= 2)
        {
            return;
        }

        for (var pass = 0; pass < BoundaryRefinementPasses; pass++)
        {
            var changed = false;
            for (var selectedIndex = 1; selectedIndex < selectedIndexes.Count - 1; selectedIndex++)
            {
                if (straightFlags[selectedIndex - 1] != straightFlags[selectedIndex])
                {
                    continue;
                }

                var previousIndex = selectedIndexes[selectedIndex - 1];
                var currentIndex = selectedIndexes[selectedIndex];
                var nextIndex = selectedIndexes[selectedIndex + 1];
                var bestIndex = currentIndex;
                var bestScore = ScoreBoundary(
                    samples,
                    selectedIndexes,
                    straightFlags,
                    selectedIndex,
                    currentIndex,
                    settings);

                for (var candidateIndex = previousIndex + 1;
                     candidateIndex < nextIndex;
                     candidateIndex++)
                {
                    if (candidateIndex == currentIndex)
                    {
                        continue;
                    }

                    var score = ScoreBoundary(
                        samples,
                        selectedIndexes,
                        straightFlags,
                        selectedIndex,
                        candidateIndex,
                        settings);
                    if (score + 1e-9 < bestScore)
                    {
                        bestScore = score;
                        bestIndex = candidateIndex;
                    }
                }

                if (bestIndex != currentIndex)
                {
                    selectedIndexes[selectedIndex] = bestIndex;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }
    }

    private static double ScoreBoundary(
        IReadOnlyList<TargetSample> samples,
        IReadOnlyList<int> selectedIndexes,
        IReadOnlyList<bool> straightFlags,
        int selectedIndex,
        int candidateIndex,
        Tpt2CurveFitSettings settings)
    {
        var isStraightRun = straightFlags[selectedIndex - 1];
        var maximumLength = isStraightRun
            ? settings.MaximumSectionLength
            : settings.TargetSectionLength *
              settings.PreferredMaximumAdjacentLengthRatio;
        var previousIndex = selectedIndexes[selectedIndex - 1];
        var nextIndex = selectedIndexes[selectedIndex + 1];
        var left = EvaluateSegment(samples, previousIndex, candidateIndex, settings);
        var right = EvaluateSegment(samples, candidateIndex, nextIndex, settings);
        if (!IsValid(left, maximumLength, settings) ||
            !IsValid(right, maximumLength, settings))
        {
            return double.PositiveInfinity;
        }

        var score =
            ((left.RootMeanSquareDeviation + right.RootMeanSquareDeviation) /
             settings.MaximumDeviation) +
            ((left.MaximumRollDeviation + right.MaximumRollDeviation) /
             settings.MaximumRollDeviation) +
            (CurvatureMismatch(left.Curve, right.Curve) *
             settings.TargetSectionLength) +
            (Math.Abs(left.RollRate - right.RollRate) * 0.5);

        score += LengthRatioPenalty(left.ChordLength, right.ChordLength, settings);
        if (!isStraightRun)
        {
            score += TargetLengthPenalty(left.ChordLength, settings);
            score += TargetLengthPenalty(right.ChordLength, settings);
        }

        if (selectedIndex >= 2 &&
            straightFlags[selectedIndex - 2] == isStraightRun)
        {
            var outerLeft = EvaluateSegment(
                samples,
                selectedIndexes[selectedIndex - 2],
                previousIndex,
                settings);
            score += LengthRatioPenalty(outerLeft.ChordLength, left.ChordLength, settings);
            score += CurvatureMismatch(outerLeft.Curve, left.Curve) *
                     settings.TargetSectionLength;
            score += Math.Abs(outerLeft.RollRate - left.RollRate) * 0.5;
        }

        if (selectedIndex + 1 < straightFlags.Count &&
            straightFlags[selectedIndex + 1] == isStraightRun)
        {
            var outerRight = EvaluateSegment(
                samples,
                nextIndex,
                selectedIndexes[selectedIndex + 2],
                settings);
            score += LengthRatioPenalty(right.ChordLength, outerRight.ChordLength, settings);
            score += CurvatureMismatch(right.Curve, outerRight.Curve) *
                     settings.TargetSectionLength;
            score += Math.Abs(right.RollRate - outerRight.RollRate) * 0.5;
        }

        return score;
    }

    private static bool IsValid(
        SegmentEvaluation evaluation,
        double maximumLength,
        Tpt2CurveFitSettings settings) =>
        evaluation.ChordLength >= settings.MinimumSectionLength - 1e-9 &&
        evaluation.ChordLength <= maximumLength + 1e-9 &&
        (evaluation.IsAdjacentSourcePair ||
         (evaluation.MaximumDeviation <= settings.MaximumDeviation + 1e-9 &&
          evaluation.MaximumRollDeviation <= settings.MaximumRollDeviation + 1e-9 &&
          evaluation.TotalRollChange <= settings.MaximumRollChange + 1e-9));

    private static double TargetLengthPenalty(
        double length,
        Tpt2CurveFitSettings settings) =>
        Math.Abs(length - settings.TargetSectionLength) /
        settings.TargetSectionLength *
        2.0;

    private static double LengthRatioPenalty(
        double firstLength,
        double secondLength,
        Tpt2CurveFitSettings settings)
    {
        var shorter = Math.Min(firstLength, secondLength);
        if (shorter <= 1e-9)
        {
            return 1000.0;
        }

        var ratio = Math.Max(firstLength, secondLength) / shorter;
        if (ratio <= settings.PreferredMaximumAdjacentLengthRatio)
        {
            return 0.0;
        }

        return
            (ratio - settings.PreferredMaximumAdjacentLengthRatio) *
            2.0;
    }

    private static double CurvatureMismatch(CubicBezier3 left, CubicBezier3 right) =>
        (left.EndCurvatureVector() - right.StartCurvatureVector()).Length;

    private static SegmentEvaluation EvaluateSegment(
        IReadOnlyList<TargetSample> samples,
        int startIndex,
        int endIndex,
        Tpt2CurveFitSettings settings)
    {
        if (endIndex <= startIndex)
        {
            throw new ArgumentOutOfRangeException(nameof(endIndex));
        }

        var start = samples[startIndex];
        var end = samples[endIndex];
        var chordLength = (end.Position - start.Position).Length;
        if (chordLength <= 1e-12)
        {
            return InvalidEvaluation(start.Position, end.Position, chordLength);
        }

        var handleLength = chordLength * settings.HandleLengthFactor;
        var curve = new CubicBezier3(
            start.Position,
            start.Position + (start.Forward * handleLength),
            end.Position - (end.Forward * handleLength),
            end.Position);
        var lookup = curve.CreateArcLengthLookup();
        var sourceLength = end.CumulativeDistance - start.CumulativeDistance;
        if (sourceLength <= 1e-12)
        {
            return InvalidEvaluation(start.Position, end.Position, chordLength);
        }

        var maximumDeviation = 0.0;
        var maximumRollDeviation = 0.0;
        var sumSquaredDeviation = 0.0;
        var comparisonCount = 0;
        var worstSeverity = 0.0;
        var worstSourceIndex = -1;
        for (var index = startIndex; index <= endIndex; index++)
        {
            var sourceFraction =
                (samples[index].CumulativeDistance - start.CumulativeDistance) /
                sourceLength;
            var fittedRoll =
                start.AccumulatedTwist +
                ((end.AccumulatedTwist - start.AccumulatedTwist) * sourceFraction);
            AddComparison(
                samples[index].Position,
                lookup.EvaluateAtFraction(sourceFraction),
                samples[index].AccumulatedTwist,
                fittedRoll,
                index,
                settings,
                ref maximumDeviation,
                ref maximumRollDeviation,
                ref sumSquaredDeviation,
                ref comparisonCount,
                ref worstSeverity,
                ref worstSourceIndex);
        }

        if (endIndex - startIndex > 1)
        {
            var uniformComparisonCount = Math.Clamp(
                endIndex - startIndex,
                MinimumUniformComparisonCount,
                MaximumUniformComparisonCount);
            for (var comparisonIndex = 1;
                 comparisonIndex < uniformComparisonCount;
                 comparisonIndex++)
            {
                var sourceFraction = comparisonIndex / (double)uniformComparisonCount;
                var source = EvaluateSourceAtFraction(
                    samples,
                    startIndex,
                    endIndex,
                    sourceFraction);
                var fittedRoll =
                    start.AccumulatedTwist +
                    ((end.AccumulatedTwist - start.AccumulatedTwist) * sourceFraction);
                AddComparison(
                    source.Position,
                    lookup.EvaluateAtFraction(sourceFraction),
                    source.AccumulatedTwist,
                    fittedRoll,
                    sourceIndex: -1,
                    settings,
                    ref maximumDeviation,
                    ref maximumRollDeviation,
                    ref sumSquaredDeviation,
                    ref comparisonCount,
                    ref worstSeverity,
                    ref worstSourceIndex);
            }
        }

        var totalRollChange =
            Math.Abs(end.AccumulatedTwist - start.AccumulatedTwist);
        return new SegmentEvaluation(
            curve,
            chordLength,
            maximumDeviation,
            Math.Sqrt(sumSquaredDeviation / comparisonCount),
            maximumRollDeviation,
            totalRollChange,
            (end.AccumulatedTwist - start.AccumulatedTwist) / chordLength,
            comparisonCount,
            sumSquaredDeviation,
            worstSourceIndex,
            endIndex - startIndex == 1);
    }

    private static SegmentEvaluation InvalidEvaluation(
        Vec3 start,
        Vec3 end,
        double chordLength) =>
        new(
            new CubicBezier3(start, start, end, end),
            chordLength,
            double.PositiveInfinity,
            double.PositiveInfinity,
            double.PositiveInfinity,
            double.PositiveInfinity,
            double.PositiveInfinity,
            1,
            double.PositiveInfinity,
            -1,
            false);

    private static SourcePoint EvaluateSourceAtFraction(
        IReadOnlyList<TargetSample> samples,
        int startIndex,
        int endIndex,
        double sourceFraction)
    {
        var startDistance = samples[startIndex].CumulativeDistance;
        var targetDistance =
            startDistance +
            ((samples[endIndex].CumulativeDistance - startDistance) * sourceFraction);

        var lowerIndex = startIndex;
        var upperIndex = endIndex;
        while (lowerIndex + 1 < upperIndex)
        {
            var middleIndex = lowerIndex + ((upperIndex - lowerIndex) / 2);
            if (samples[middleIndex].CumulativeDistance < targetDistance)
            {
                lowerIndex = middleIndex;
            }
            else
            {
                upperIndex = middleIndex;
            }
        }

        var distanceSpan =
            samples[upperIndex].CumulativeDistance -
            samples[lowerIndex].CumulativeDistance;
        if (distanceSpan <= 1e-12)
        {
            return new SourcePoint(
                samples[upperIndex].Position,
                samples[upperIndex].AccumulatedTwist);
        }

        var amount =
            (targetDistance - samples[lowerIndex].CumulativeDistance) /
            distanceSpan;
        return new SourcePoint(
            Vec3.Lerp(samples[lowerIndex].Position, samples[upperIndex].Position, amount),
            samples[lowerIndex].AccumulatedTwist +
            ((samples[upperIndex].AccumulatedTwist -
              samples[lowerIndex].AccumulatedTwist) * amount));
    }

    private static void AddComparison(
        Vec3 sourcePosition,
        Vec3 fittedPosition,
        double sourceRoll,
        double fittedRoll,
        int sourceIndex,
        Tpt2CurveFitSettings settings,
        ref double maximumDeviation,
        ref double maximumRollDeviation,
        ref double sumSquaredDeviation,
        ref int comparisonCount,
        ref double worstSeverity,
        ref int worstSourceIndex)
    {
        var squaredDeviation = (sourcePosition - fittedPosition).LengthSquared;
        var deviation = Math.Sqrt(squaredDeviation);
        var rollDeviation = Math.Abs(sourceRoll - fittedRoll);
        maximumDeviation = Math.Max(maximumDeviation, deviation);
        maximumRollDeviation = Math.Max(maximumRollDeviation, rollDeviation);
        sumSquaredDeviation += squaredDeviation;
        comparisonCount++;

        if (sourceIndex < 0)
        {
            return;
        }

        var severity = Math.Max(
            deviation / settings.MaximumDeviation,
            rollDeviation / settings.MaximumRollDeviation);
        if (severity > worstSeverity)
        {
            worstSeverity = severity;
            worstSourceIndex = sourceIndex;
        }
    }

    private static double CalculateFrameTwistDegrees(
        OrientationMatrix previous,
        OrientationMatrix current)
    {
        var previousForward = previous.Forward.Normalized();
        var currentForward = current.Forward.Normalized();
        var transportedUp = ParallelTransport(
            previous.Up,
            previousForward,
            currentForward,
            previous.Right);
        transportedUp -= currentForward * Vec3.Dot(transportedUp, currentForward);
        var currentUp =
            current.Up - (currentForward * Vec3.Dot(current.Up, currentForward));
        if (transportedUp.LengthSquared <= 1e-12 || currentUp.LengthSquared <= 1e-12)
        {
            return 0.0;
        }

        transportedUp = transportedUp.Normalized();
        currentUp = currentUp.Normalized();
        var sine = Vec3.Dot(
            currentForward,
            Vec3.Cross(transportedUp, currentUp));
        var cosine = Math.Clamp(Vec3.Dot(transportedUp, currentUp), -1.0, 1.0);
        return Math.Atan2(sine, cosine) * (180.0 / Math.PI);
    }

    private static Vec3 ParallelTransport(
        Vec3 vector,
        Vec3 fromDirection,
        Vec3 toDirection,
        Vec3 fallbackAxis)
    {
        var cross = Vec3.Cross(fromDirection, toDirection);
        var sine = cross.Length;
        var cosine = Math.Clamp(
            Vec3.Dot(fromDirection, toDirection),
            -1.0,
            1.0);
        if (sine > 1e-10)
        {
            var axis = cross / sine;
            return
                (vector * cosine) +
                (Vec3.Cross(axis, vector) * sine) +
                (axis * Vec3.Dot(axis, vector) * (1.0 - cosine));
        }

        if (cosine >= 0.0)
        {
            return vector;
        }

        var axisCandidate =
            fallbackAxis -
            (fromDirection * Vec3.Dot(fallbackAxis, fromDirection));
        if (axisCandidate.LengthSquared <= 1e-12)
        {
            axisCandidate = Math.Abs(fromDirection.Y) < 0.9
                ? Vec3.Cross(fromDirection, new Vec3(0.0, 1.0, 0.0))
                : Vec3.Cross(fromDirection, new Vec3(1.0, 0.0, 0.0));
        }

        var axis180 = axisCandidate.Normalized();
        return (axis180 * (2.0 * Vec3.Dot(axis180, vector))) - vector;
    }

    private static double AngleBetweenDegrees(Vec3 first, Vec3 second)
    {
        var denominator = first.Length * second.Length;
        if (denominator <= 1e-12)
        {
            return 180.0;
        }

        var cosine = Math.Clamp(Vec3.Dot(first, second) / denominator, -1.0, 1.0);
        return Math.Acos(cosine) * (180.0 / Math.PI);
    }

    private readonly record struct TargetSample(
        int SourceIndex,
        Vec3 Position,
        Vec3 Forward,
        double AccumulatedTwist,
        double CumulativeDistance);

    private readonly record struct SourcePoint(Vec3 Position, double AccumulatedTwist);

    private readonly record struct StraightRun(int StartIndex, int EndIndex);

    private readonly record struct SelectedSegment(
        int StartIndex,
        int EndIndex,
        bool IsStraightRun);

    private readonly record struct BoundaryAdjustment(
        int BoundaryIndex,
        int SampleIndex,
        double Score);

    private readonly record struct MergeCandidate(
        int FirstSegmentIndex,
        bool IsStraightRun,
        double Score);

    private readonly record struct SegmentEvaluation(
        CubicBezier3 Curve,
        double ChordLength,
        double MaximumDeviation,
        double RootMeanSquareDeviation,
        double MaximumRollDeviation,
        double TotalRollChange,
        double RollRate,
        int ComparisonCount,
        double SumSquaredDeviation,
        int WorstSourceIndex,
        bool IsAdjacentSourcePair);
}
