using Importer.Core.Geometry;

namespace Importer.Core.Conversion;

public readonly record struct SignedAxis(SourceAxis Axis, int Sign)
{
    public double Read(Vec3 source)
    {
        if (Sign is not (1 or -1))
        {
            throw new InvalidOperationException("An axis sign must be either +1 or -1.");
        }

        var component = Axis switch
        {
            SourceAxis.X => source.X,
            SourceAxis.Y => source.Y,
            SourceAxis.Z => source.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(Axis)),
        };

        return Sign * component;
    }
}

