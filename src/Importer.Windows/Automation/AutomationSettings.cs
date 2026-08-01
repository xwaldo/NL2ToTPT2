using Importer.Windows.Input;

namespace Importer.Windows.Automation;

public sealed record AutomationSettings
{
    public TimeSpan InputDelay { get; init; } = TimeSpan.FromMilliseconds(50);

    public TimeSpan MouseMovementDelay { get; init; } = TimeSpan.FromMilliseconds(50);

    public TimeSpan MouseButtonDelay { get; init; } = TimeSpan.FromMilliseconds(40);

    public TimeSpan BuildDelay { get; init; } = TimeSpan.FromMilliseconds(20);

    public TimeSpan ModeSwitchDelay { get; init; } = TimeSpan.FromMilliseconds(20);

    public TimeSpan TextInputDelay { get; init; } = TimeSpan.FromMilliseconds(20);

    public MouseMovementMode MouseMovementMode { get; init; } =
        MouseMovementMode.SnapMicroGlide;

    public bool DryRun { get; init; }

    public void Validate()
    {
        if (InputDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Mouse target hold cannot be negative.");
        }

        if (MouseMovementDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Mouse movement time cannot be negative.");
        }

        if (MouseButtonDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Mouse button delay cannot be negative.");
        }

        if (BuildDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Build delay cannot be negative.");
        }

        if (ModeSwitchDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Mode switch delay cannot be negative.");
        }

        if (TextInputDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Text input delay cannot be negative.");
        }

        if (!Enum.IsDefined(MouseMovementMode))
        {
            throw new InvalidOperationException("Mouse movement mode is not valid.");
        }
    }
}
