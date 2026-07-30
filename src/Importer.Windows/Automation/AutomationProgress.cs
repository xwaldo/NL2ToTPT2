namespace Importer.Windows.Automation;

public sealed record AutomationProgress(
    AutomationState State,
    int CurrentSection,
    int TotalSections,
    string Message,
    DateTimeOffset OccurredAt);
