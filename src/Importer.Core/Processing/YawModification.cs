using Importer.Core.Conversion;

namespace Importer.Core.Processing;

public enum YawModificationMode
{
    SetAnchorExactly,
    Increment,
}

public readonly record struct YawModification(
    double Angle,
    YawModificationMode Mode)
{
    public static YawModification None =>
        new(0.0, YawModificationMode.Increment);

    public double ResolveDelta(double originalAnchorYaw)
    {
        Validate();
        if (!double.IsFinite(originalAnchorYaw))
        {
            throw new ArgumentOutOfRangeException(nameof(originalAnchorYaw));
        }

        return Mode switch
        {
            YawModificationMode.SetAnchorExactly =>
                AngleMath.ShortestDifference(originalAnchorYaw, Angle),
            YawModificationMode.Increment => AngleMath.Normalize(Angle),
            _ => throw new ArgumentOutOfRangeException(nameof(Mode)),
        };
    }

    public void Validate()
    {
        if (!double.IsFinite(Angle))
        {
            throw new InvalidOperationException("Yaw modification must be finite.");
        }

        if (!Enum.IsDefined(Mode))
        {
            throw new InvalidOperationException("Yaw modification mode is not valid.");
        }
    }
}
