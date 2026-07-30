namespace Importer.Windows.Automation;

public sealed record AutomationSettings
{
    public TimeSpan InputDelay { get; init; } = TimeSpan.FromMilliseconds(50);

    public TimeSpan BuildDelay { get; init; } = TimeSpan.FromMilliseconds(20);

    public TimeSpan ModeSwitchDelay { get; init; } = TimeSpan.FromMilliseconds(20);

    public bool DryRun { get; init; }

    public void Validate()
    {
        if (InputDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Input delay cannot be negative.");
        }

        if (BuildDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Build delay cannot be negative.");
        }

        if (ModeSwitchDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Mode switch delay cannot be negative.");
        }
    }
}
