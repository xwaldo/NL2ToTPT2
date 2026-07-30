using Importer.Core.Geometry;

namespace Importer.Core.Models;

public sealed record NoLimitsSample(
    int SourceIndex,
    Vec3 Position,
    Vec3 Front,
    Vec3 Left,
    Vec3 Up);

