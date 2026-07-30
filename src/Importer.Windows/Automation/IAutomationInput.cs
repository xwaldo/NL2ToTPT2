using Importer.Windows.Calibration;

namespace Importer.Windows.Automation;

public interface IAutomationInput
{
    Task LocateWindowAsync(CancellationToken cancellationToken);

    Task FocusWindowAsync(CancellationToken cancellationToken);

    Task ClickAsync(
        NormalizedPoint point,
        TimeSpan inputDelay,
        CancellationToken cancellationToken);

    Task EnterValueAsync(
        NormalizedPoint point,
        string value,
        TimeSpan inputDelay,
        CancellationToken cancellationToken);
}
