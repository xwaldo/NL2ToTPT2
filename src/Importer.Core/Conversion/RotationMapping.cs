using Importer.Core.Models;

namespace Importer.Core.Conversion;

public sealed record RotationMapping(
    int YawSign,
    int PitchSign,
    int RollSign,
    double YawOffset = 0.0,
    double PitchOffset = 0.0,
    double RollOffset = 0.0)
{
    public static RotationMapping Identity { get; } = new(1, 1, 1);

    public EulerAngles Map(EulerAngles source)
    {
        Validate();
        return new EulerAngles(
            Transform(source.Yaw, YawSign, YawOffset),
            Transform(source.Pitch, PitchSign, PitchOffset),
            Transform(source.Roll, RollSign, RollOffset));
    }

    public void Validate()
    {
        if (YawSign is not (1 or -1) ||
            PitchSign is not (1 or -1) ||
            RollSign is not (1 or -1))
        {
            throw new InvalidOperationException("Rotation signs must be either +1 or -1.");
        }

        if (!double.IsFinite(YawOffset) ||
            !double.IsFinite(PitchOffset) ||
            !double.IsFinite(RollOffset))
        {
            throw new InvalidOperationException("Rotation offsets must be finite.");
        }
    }

    private static double Transform(double value, int sign, double offset)
    {
        var transformed = (value * sign) + offset;
        return transformed >= -180.0 && transformed < 180.0
            ? transformed
            : AngleMath.Normalize(transformed);
    }
}
