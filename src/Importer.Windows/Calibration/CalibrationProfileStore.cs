using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Importer.Windows.Calibration;

public sealed class CalibrationProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public CalibrationProfileStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NL2ToTPT2",
            "calibration.json");
    }

    public string FilePath { get; }

    public IReadOnlyDictionary<CalibrationTarget, NormalizedPoint> Load()
    {
        if (!File.Exists(FilePath))
        {
            return new Dictionary<CalibrationTarget, NormalizedPoint>();
        }

        try
        {
            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            var profile = JsonSerializer.Deserialize<SavedCalibrationProfile>(
                json,
                SerializerOptions);
            if (profile is null)
            {
                throw new InvalidDataException("The saved calibration profile is empty.");
            }

            if (profile.Version != SavedCalibrationProfile.CurrentVersion)
            {
                throw new InvalidDataException(
                    $"Calibration profile version {profile.Version} is not supported.");
            }

            return profile.Points
                .Where(pair =>
                    Enum.IsDefined(pair.Key) &&
                    pair.Value.IsValid)
                .ToDictionary();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The saved calibration profile is not valid JSON.",
                exception);
        }
    }

    public void Save(IReadOnlyDictionary<CalibrationTarget, NormalizedPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Any(pair => !Enum.IsDefined(pair.Key) || !pair.Value.IsValid))
        {
            throw new ArgumentException(
                "Calibration contains an unknown target or invalid normalized point.",
                nameof(points));
        }

        var directory = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Calibration file path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        var profile = new SavedCalibrationProfile
        {
            Points = points.ToDictionary(),
        };
        var json = JsonSerializer.Serialize(profile, SerializerOptions);
        var temporaryPath = $"{FilePath}.tmp";
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    public void Clear()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
