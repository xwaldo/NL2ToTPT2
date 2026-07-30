using Importer.Core.Models;

namespace Importer.Core.Conversion;

public sealed class OrientationConverter(
    CoordinateMapping coordinateMapping,
    RotationMapping rotationMapping)
{
    public EulerAngles Convert(NoLimitsSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        var matrix = OrientationMatrix.FromNoLimitsBasis(
            coordinateMapping.Map(sample.Front),
            coordinateMapping.Map(sample.Left),
            coordinateMapping.Map(sample.Up));
        var angles = matrix.ToYawPitchRoll();

        return rotationMapping.Map(angles);
    }
}
