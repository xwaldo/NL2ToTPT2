using Importer.Core.Geometry;

namespace Importer.Core.Models;

public sealed record ImportedSection(
    int SectionIndex,
    int SourceIndex,
    Vec3 DesiredPosition,
    Vec3 EnteredDelta,
    Vec3 ExpectedBuiltPosition,
    EulerAngles Rotation,
    InputPrecisionMode InputPrecision = InputPrecisionMode.Fixed,
    PositionReferenceMode PositionReference = PositionReferenceMode.PreviousNodeAxes);
