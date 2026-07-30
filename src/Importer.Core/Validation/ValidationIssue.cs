namespace Importer.Core.Validation;

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Code,
    string Message,
    int? SectionIndex = null);

