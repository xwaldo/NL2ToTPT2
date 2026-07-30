using Importer.Core.Conversion;
using Importer.Core.Geometry;
using Importer.Core.Models;

namespace Importer.Core.Processing;

public sealed class SectionGenerator
{
    public IReadOnlyList<ImportedSection> Generate(
        IReadOnlyList<NoLimitsSample> samples,
        ImportConversionProfile conversionProfile,
        double positionScale = 1.0,
        Vec3? startingPosition = null,
        InputPrecisionMode inputPrecision = InputPrecisionMode.Fixed,
        PositionReferenceMode positionReference = PositionReferenceMode.PreviousNodeAxes)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(conversionProfile);
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

        if (!Enum.IsDefined(positionReference))
        {
            throw new ArgumentOutOfRangeException(nameof(positionReference));
        }

        var anchor = CreateAnchor(
            samples,
            conversionProfile,
            startingPosition,
            inputPrecision);
        var orientationConverter = new OrientationConverter(
            conversionProfile.OrientationCoordinates,
            conversionProfile.Rotation);
        var sourceOrigin = conversionProfile.PositionCoordinates.Map(samples[0].Position);
        var builtPosition = anchor.Position;
        var outputOrigin = builtPosition;
        var sections = new List<ImportedSection>(samples.Count - 1);

        for (var sampleIndex = 1; sampleIndex < samples.Count; sampleIndex++)
        {
            var sample = samples[sampleIndex];
            var desiredPosition =
                outputOrigin +
                ((conversionProfile.PositionCoordinates.Map(sample.Position) - sourceOrigin) * positionScale);
            var exactWorldDelta = desiredPosition - builtPosition;
            var previousAxes = positionReference == PositionReferenceMode.PreviousNodeAxes
                ? CreatePositionAxes(
                    samples[sampleIndex - 1],
                    orientationConverter,
                    inputPrecision)
                : default;
            var exactDelta = positionReference == PositionReferenceMode.PreviousNodeAxes
                ? WorldVectorToPreviousNodeInput(previousAxes, exactWorldDelta)
                : exactWorldDelta;
            var enteredDelta = inputPrecision == InputPrecisionMode.Full
                ? exactDelta
                : PositionMath.RoundPosition(exactDelta);
            var enteredWorldDelta = positionReference == PositionReferenceMode.PreviousNodeAxes
                ? PreviousNodeInputToWorldVector(previousAxes, enteredDelta)
                : enteredDelta;
            builtPosition += enteredWorldDelta;

            sections.Add(new ImportedSection(
                sections.Count + 1,
                sample.SourceIndex,
                desiredPosition,
                enteredDelta,
                builtPosition,
                orientationConverter.Convert(sample),
                inputPrecision,
                positionReference));
        }

        return sections;
    }

    public ImportAnchor CreateAnchor(
        IReadOnlyList<NoLimitsSample> samples,
        ImportConversionProfile conversionProfile,
        Vec3? startingPosition = null,
        InputPrecisionMode inputPrecision = InputPrecisionMode.Fixed)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(conversionProfile);
        if (samples.Count == 0)
        {
            throw new ArgumentException("At least one sample is required.", nameof(samples));
        }

        if (!Enum.IsDefined(inputPrecision))
        {
            throw new ArgumentOutOfRangeException(nameof(inputPrecision));
        }

        conversionProfile.Validate();
        var orientationConverter = new OrientationConverter(
            conversionProfile.OrientationCoordinates,
            conversionProfile.Rotation);
        return new ImportAnchor(
            samples[0].SourceIndex,
            startingPosition ?? Vec3.Zero,
            orientationConverter.Convert(samples[0]),
            inputPrecision);
    }

    private static Vec3 WorldVectorToPreviousNodeInput(
        OrientationMatrix previousAxes,
        Vec3 worldVector)
    {
        var localVector = previousAxes.WorldVectorToLocal(worldVector);
        return new Vec3(localVector.X, localVector.Y, -localVector.Z);
    }

    private static Vec3 PreviousNodeInputToWorldVector(
        OrientationMatrix previousAxes,
        Vec3 inputVector) =>
        previousAxes.LocalVectorToWorld(
            new Vec3(inputVector.X, inputVector.Y, -inputVector.Z));

    private static OrientationMatrix CreatePositionAxes(
        NoLimitsSample sample,
        OrientationConverter orientationConverter,
        InputPrecisionMode inputPrecision)
    {
        var rotation = orientationConverter.Convert(sample);
        if (inputPrecision == InputPrecisionMode.Fixed)
        {
            rotation = new EulerAngles(
                AngleMath.RoundRotation(rotation.Yaw),
                AngleMath.RoundRotation(rotation.Pitch),
                AngleMath.RoundRotation(rotation.Roll));
        }

        return OrientationMatrix.FromRobloxYawPitchRoll(rotation);
    }
}
