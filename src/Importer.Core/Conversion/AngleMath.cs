using System.Globalization;
using Importer.Core.Models;

namespace Importer.Core.Conversion;

public static class AngleMath
{
    public static double Normalize(double angle)
    {
        angle = (angle + 180.0) % 360.0;
        if (angle < 0.0)
        {
            angle += 360.0;
        }

        return angle - 180.0;
    }

    public static double ShortestDifference(double from, double to) =>
        Normalize(to - from);

    public static double RoundRotation(double value)
    {
        if (Math.Abs(value) < 0.05)
        {
            return 0.0;
        }

        var rounded = Math.Round(Normalize(value), 1, MidpointRounding.AwayFromZero);
        return rounded == 0.0 ? 0.0 : Normalize(rounded);
    }

    public static string FormatRotation(
        double value,
        InputPrecisionMode precision = InputPrecisionMode.Fixed)
    {
        if (precision == InputPrecisionMode.Fixed)
        {
            return RoundRotation(value).ToString("0.0", CultureInfo.InvariantCulture);
        }

        if (precision != InputPrecisionMode.Full)
        {
            throw new ArgumentOutOfRangeException(nameof(precision));
        }

        var formatted = value.ToString("0.###############", CultureInfo.InvariantCulture);
        return formatted == "-0" ? "0" : formatted;
    }
}
