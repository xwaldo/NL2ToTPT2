using Importer.Core.Geometry;

namespace Importer.Core.Models;

public sealed record ImportAnchor(
    int SourceIndex,
    Vec3 Position,
    EulerAngles Rotation,
    InputPrecisionMode InputPrecision);
