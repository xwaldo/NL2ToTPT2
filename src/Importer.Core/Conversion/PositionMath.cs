using System.Globalization;
using Importer.Core.Geometry;
using Importer.Core.Models;

namespace Importer.Core.Conversion;

public static class PositionMath
{
    public static double RoundPosition(double value)
    {
        if (Math.Abs(value) < 0.005)
        {
            return 0.0;
        }

        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return rounded == 0.0 ? 0.0 : rounded;
    }

    public static Vec3 RoundPosition(Vec3 value) =>
        new(
            RoundPosition(value.X),
            RoundPosition(value.Y),
            RoundPosition(value.Z));

    public static string FormatPosition(
        double value,
        InputPrecisionMode precision = InputPrecisionMode.Fixed)
    {
        if (precision == InputPrecisionMode.Fixed)
        {
            return RoundPosition(value).ToString("0.00", CultureInfo.InvariantCulture);
        }

        if (precision != InputPrecisionMode.Full)
        {
            throw new ArgumentOutOfRangeException(nameof(precision));
        }

        var formatted = value.ToString("0.###############", CultureInfo.InvariantCulture);
        return formatted == "-0" ? "0" : formatted;
    }
}
