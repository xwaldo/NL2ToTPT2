using Importer.Windows.Calibration;
using Importer.Windows.Input;
using Importer.Windows.Windowing;

namespace Importer.Windows.Automation;

public sealed class RobloxAutomationInput(
    RobloxWindowLocator windowLocator,
    WindowsInputSimulator inputSimulator) : IAutomationInput
{
    private nint _windowHandle;

    public Task LocateWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _windowHandle = windowLocator.Locate();
        return Task.CompletedTask;
    }

    public Task FocusWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureLocated();
        windowLocator.Focus(_windowHandle);
        return Task.CompletedTask;
    }

    public Task ClickAsync(
        NormalizedPoint point,
        TimeSpan inputDelay,
        CancellationToken cancellationToken)
    {
        EnsureLocated();
        var screenPoint = windowLocator.ToScreenPoint(_windowHandle, point);
        return inputSimulator.ClickAsync(
            screenPoint,
            inputDelay,
            cancellationToken);
    }

    public Task EnterValueAsync(
        NormalizedPoint point,
        string value,
        TimeSpan inputDelay,
        CancellationToken cancellationToken)
    {
        EnsureLocated();
        var screenPoint = windowLocator.ToScreenPoint(_windowHandle, point);
        return inputSimulator.EnterValueAsync(
            screenPoint,
            value,
            inputDelay,
            cancellationToken);
    }

    private void EnsureLocated()
    {
        if (_windowHandle == nint.Zero)
        {
            throw new InvalidOperationException("The Roblox window has not been located.");
        }
    }
}
