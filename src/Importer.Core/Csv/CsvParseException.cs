namespace Importer.Core.Csv;

public sealed class CsvParseException : Exception
{
    public CsvParseException(string message, int? lineNumber = null, Exception? innerException = null)
        : base(lineNumber is null ? message : $"Line {lineNumber}: {message}", innerException)
    {
        LineNumber = lineNumber;
    }

    public int? LineNumber { get; }
}

