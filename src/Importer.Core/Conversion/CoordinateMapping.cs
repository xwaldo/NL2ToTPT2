using Importer.Core.Geometry;

namespace Importer.Core.Conversion;

public sealed record CoordinateMapping(SignedAxis TargetX, SignedAxis TargetY, SignedAxis TargetZ)
{
    public static CoordinateMapping Identity { get; } = new(
        new SignedAxis(SourceAxis.X, 1),
        new SignedAxis(SourceAxis.Y, 1),
        new SignedAxis(SourceAxis.Z, 1));

    public Vec3 Map(Vec3 source)
    {
        Validate();
        return new Vec3(
            TargetX.Read(source),
            TargetY.Read(source),
            TargetZ.Read(source));
    }

    public void Validate()
    {
        var axes = new[] { TargetX, TargetY, TargetZ };
        if (axes.Any(axis => axis.Sign is not (1 or -1)))
        {
            throw new InvalidOperationException("Every coordinate sign must be either +1 or -1.");
        }

        if (axes.Select(axis => axis.Axis).Distinct().Count() != 3)
        {
            throw new InvalidOperationException("Each source axis must be mapped exactly once.");
        }
    }
}

