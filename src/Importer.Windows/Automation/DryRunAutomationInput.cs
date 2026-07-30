using Importer.Windows.Calibration;

namespace Importer.Windows.Automation;

public sealed class DryRunAutomationInput : IAutomationInput
{
    public Task LocateWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task FocusWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task ClickAsync(
        NormalizedPoint point,
        TimeSpan inputDelay,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task EnterValueAsync(
        NormalizedPoint point,
        string value,
        TimeSpan inputDelay,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
