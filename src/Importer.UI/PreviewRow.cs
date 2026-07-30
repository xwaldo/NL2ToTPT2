using Importer.Core.Conversion;
using Importer.Core.Models;
using System.Globalization;

namespace Importer.UI;

public sealed class PreviewRow
{
    public PreviewRow(ImportAnchor anchor)
    {
        Step = "ANCHOR";
        Points = "Point 1 (starting node)";
        IsAnchor = true;
        X = "—";
        Y = "—";
        Z = "—";
        Yaw = AngleMath.FormatRotation(anchor.Rotation.Yaw, anchor.InputPrecision);
        Pitch = AngleMath.FormatRotation(anchor.Rotation.Pitch, anchor.InputPrecision);
        Roll = AngleMath.FormatRotation(anchor.Rotation.Roll, anchor.InputPrecision);
        BuiltX = PositionMath.FormatPosition(anchor.Position.X, anchor.InputPrecision);
        BuiltY = PositionMath.FormatPosition(anchor.Position.Y, anchor.InputPrecision);
        BuiltZ = PositionMath.FormatPosition(anchor.Position.Z, anchor.InputPrecision);
    }

    public PreviewRow(ImportedSection section)
    {
        Step = section.SectionIndex.ToString(CultureInfo.InvariantCulture);
        Points = $"Point {section.SectionIndex} → Point {section.SectionIndex + 1}";
        X = PositionMath.FormatPosition(section.EnteredDelta.X, section.InputPrecision);
        Y = PositionMath.FormatPosition(section.EnteredDelta.Y, section.InputPrecision);
        Z = PositionMath.FormatPosition(section.EnteredDelta.Z, section.InputPrecision);
        Yaw = AngleMath.FormatRotation(section.Rotation.Yaw, section.InputPrecision);
        Pitch = AngleMath.FormatRotation(section.Rotation.Pitch, section.InputPrecision);
        Roll = AngleMath.FormatRotation(section.Rotation.Roll, section.InputPrecision);
        BuiltX = PositionMath.FormatPosition(
            section.ExpectedBuiltPosition.X,
            section.InputPrecision);
        BuiltY = PositionMath.FormatPosition(
            section.ExpectedBuiltPosition.Y,
            section.InputPrecision);
        BuiltZ = PositionMath.FormatPosition(
            section.ExpectedBuiltPosition.Z,
            section.InputPrecision);
    }

    public string Step { get; }

    public string Points { get; }

    public bool IsAnchor { get; }

    public string X { get; }

    public string Y { get; }

    public string Z { get; }

    public string Yaw { get; }

    public string Pitch { get; }

    public string Roll { get; }

    public string BuiltX { get; }

    public string BuiltY { get; }

    public string BuiltZ { get; }
}
