namespace Importer.Core.Conversion;

public sealed record ImportConversionProfile(
    CoordinateMapping PositionCoordinates,
    CoordinateMapping OrientationCoordinates,
    RotationMapping Rotation)
{
    public static ImportConversionProfile Identity { get; } = new(
        CoordinateMapping.Identity,
        CoordinateMapping.Identity,
        RotationMapping.Identity);

    /// <summary>
    /// Calibrated against the target editor:
    /// yaw 0 points toward -Z, yaw +90 points toward -X, positive pitch rises,
    /// and positive roll banks left.
    /// </summary>
    public static ImportConversionProfile NoLimitsToRoblox { get; } = new(
        new CoordinateMapping(
            new SignedAxis(SourceAxis.X, -1),
            new SignedAxis(SourceAxis.Y, 1),
            new SignedAxis(SourceAxis.Z, -1)),
        CoordinateMapping.Identity,
        new RotationMapping(
            YawSign: 1,
            PitchSign: 1,
            RollSign: -1));

    public void Validate()
    {
        PositionCoordinates.Validate();
        OrientationCoordinates.Validate();
        Rotation.Validate();
    }
}

