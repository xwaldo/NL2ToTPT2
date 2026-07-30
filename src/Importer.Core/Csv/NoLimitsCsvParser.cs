using System.Globalization;
using Importer.Core.Geometry;
using Importer.Core.Models;

namespace Importer.Core.Csv;

public sealed class NoLimitsCsvParser
{
    private static readonly string[] RequiredColumns =
    [
        "No.",
        "PosX", "PosY", "PosZ",
        "FrontX", "FrontY", "FrontZ",
        "LeftX", "LeftY", "LeftZ",
        "UpX", "UpY", "UpZ",
    ];

    public async Task<IReadOnlyList<NoLimitsSample>> ParseFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var reader = new StreamReader(stream);
        return await ParseAsync(reader, cancellationToken);
    }

    public async Task<IReadOnlyList<NoLimitsSample>> ParseAsync(
        TextReader reader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (headerLine is null)
        {
            throw new CsvParseException("The file is empty.");
        }

        var delimiter = DetectDelimiter(headerLine);
        var headers = SplitRow(headerLine, delimiter);
        var indexes = BuildColumnIndexes(headers);
        var samples = new List<NoLimitsSample>();
        var lineNumber = 1;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = SplitRow(line, delimiter);
            try
            {
                var sample = new NoLimitsSample(
                    ParseInt(values, indexes["No."]),
                    ParseVector(values, indexes, "Pos"),
                    ParseVector(values, indexes, "Front"),
                    ParseVector(values, indexes, "Left"),
                    ParseVector(values, indexes, "Up"));

                ValidateSample(sample);
                samples.Add(sample);
            }
            catch (Exception exception) when (
                exception is FormatException or
                IndexOutOfRangeException or
                InvalidDataException or
                OverflowException)
            {
                throw new CsvParseException(exception.Message, lineNumber, exception);
            }
        }

        if (samples.Count < 2)
        {
            throw new CsvParseException("At least two data rows are required.");
        }

        return samples;
    }

    private static char DetectDelimiter(string headerLine)
    {
        if (headerLine.Contains('\t'))
        {
            return '\t';
        }

        if (headerLine.Contains(','))
        {
            return ',';
        }

        throw new CsvParseException("The header is not tab- or comma-delimited.");
    }

    private static string[] SplitRow(string line, char delimiter) =>
        line.Split(delimiter)
            .Select(value => value.Trim().Trim('"').TrimStart('\uFEFF'))
            .ToArray();

    private static Dictionary<string, int> BuildColumnIndexes(IReadOnlyList<string> headers)
    {
        var indexes = headers
            .Select((name, index) => (name, index))
            .ToDictionary(pair => pair.name, pair => pair.index, StringComparer.OrdinalIgnoreCase);

        var missing = RequiredColumns.Where(column => !indexes.ContainsKey(column)).ToArray();
        if (missing.Length > 0)
        {
            throw new CsvParseException($"Missing required columns: {string.Join(", ", missing)}.");
        }

        return indexes;
    }

    private static int ParseInt(IReadOnlyList<string> values, int index) =>
        int.Parse(values[index], NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static Vec3 ParseVector(
        IReadOnlyList<string> values,
        IReadOnlyDictionary<string, int> indexes,
        string prefix) =>
        new(
            ParseDouble(values, indexes[$"{prefix}X"]),
            ParseDouble(values, indexes[$"{prefix}Y"]),
            ParseDouble(values, indexes[$"{prefix}Z"]));

    private static double ParseDouble(IReadOnlyList<string> values, int index) =>
        double.Parse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture);

    private static void ValidateSample(NoLimitsSample sample)
    {
        if (!sample.Position.IsFinite ||
            !sample.Front.IsFinite ||
            !sample.Left.IsFinite ||
            !sample.Up.IsFinite)
        {
            throw new InvalidDataException("The row contains NaN or infinity.");
        }

        if (sample.Front.LengthSquared <= 1e-12 ||
            sample.Left.LengthSquared <= 1e-12 ||
            sample.Up.LengthSquared <= 1e-12)
        {
            throw new InvalidDataException("An orientation vector has zero length.");
        }
    }
}
