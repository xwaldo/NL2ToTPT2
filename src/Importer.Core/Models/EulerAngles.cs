namespace Importer.Core.Models;

public readonly record struct EulerAngles(double Yaw, double Pitch, double Roll)
{
    public bool IsFinite =>
        double.IsFinite(Yaw) &&
        double.IsFinite(Pitch) &&
        double.IsFinite(Roll);
}

