namespace Importer.Windows.Calibration;

public readonly record struct NormalizedPoint(double X, double Y)
{
    public bool IsValid =>
        double.IsFinite(X) &&
        double.IsFinite(Y) &&
        X is >= 0.0 and <= 1.0 &&
        Y is >= 0.0 and <= 1.0;
}

