namespace Importer.Windows.Calibration;

public sealed record ControlCalibration
{
    public required NormalizedPoint PositionX { get; init; }

    public required NormalizedPoint PositionY { get; init; }

    public required NormalizedPoint PositionZ { get; init; }

    public required NormalizedPoint Yaw { get; init; }

    public required NormalizedPoint Pitch { get; init; }

    public required NormalizedPoint Roll { get; init; }

    public required NormalizedPoint FocusRelease { get; init; }

    public required NormalizedPoint MoveSplineTab { get; init; }

    public required NormalizedPoint RotateSplineTab { get; init; }

    public required NormalizedPoint BuildButton { get; init; }

    public bool IsValid =>
        PositionX.IsValid &&
        PositionY.IsValid &&
        PositionZ.IsValid &&
        Yaw.IsValid &&
        Pitch.IsValid &&
        Roll.IsValid &&
        FocusRelease.IsValid &&
        MoveSplineTab.IsValid &&
        RotateSplineTab.IsValid &&
        BuildButton.IsValid;
}
