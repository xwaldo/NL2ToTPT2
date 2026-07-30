using System.Diagnostics;
using Importer.Core.Conversion;
using Importer.Core.Models;
using Importer.Windows.Calibration;

namespace Importer.Windows.Automation;

public sealed class AutomationRunner(
    IAutomationInput automationInput,
    AutomationSettings settings)
{
    private static readonly NormalizedPoint DryRunPoint = new(0.5, 0.5);
    private readonly AsyncPauseGate _pauseGate = new();
    private AutomationProgress _currentProgress = new(
        AutomationState.Idle,
        0,
        0,
        "Idle",
        DateTimeOffset.Now);
    private int _isRunning;

    public event Action<AutomationProgress>? ProgressChanged;

    public bool IsRunning => Volatile.Read(ref _isRunning) != 0;

    public bool IsPaused => _pauseGate.IsPaused;

    public void Pause()
    {
        if (IsRunning && _pauseGate.Pause())
        {
            Report(
                AutomationState.Paused,
                _currentProgress.CurrentSection,
                _currentProgress.TotalSections,
                "Paused. Use Resume to continue or Ctrl+Shift+X to stop.");
        }
    }

    public void Resume()
    {
        if (_pauseGate.Resume())
        {
            Report(
                _currentProgress.State,
                _currentProgress.CurrentSection,
                _currentProgress.TotalSections,
                "Resuming automation.");
        }
    }

    public async Task RunAsync(
        IReadOnlyList<ImportedSection> sections,
        ControlCalibration? calibration,
        int startIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sections);
        settings.Validate();
        if (sections.Count == 0)
        {
            throw new ArgumentException("The import plan contains no sections.", nameof(sections));
        }

        if (startIndex < 0 || startIndex >= sections.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startIndex),
                $"Start index must be between 0 and {sections.Count - 1}.");
        }

        if (!settings.DryRun && (calibration is null || !calibration.IsValid))
        {
            throw new InvalidOperationException(
                "All textbox and button positions must be calibrated before live automation.");
        }

        if (Interlocked.Exchange(ref _isRunning, 1) != 0)
        {
            throw new InvalidOperationException("Automation is already running.");
        }

        _ = _pauseGate.Resume();
        try
        {
            Report(AutomationState.LocateWindow, startIndex + 1, sections.Count, "Locating Roblox window.");
            await automationInput.LocateWindowAsync(cancellationToken).ConfigureAwait(false);

            for (var index = startIndex; index < sections.Count; index++)
            {
                var section = sections[index];
                var points = calibration;
                await WaitUntilResumedAsync(cancellationToken).ConfigureAwait(false);
                await automationInput.FocusWindowAsync(cancellationToken).ConfigureAwait(false);
                await DelayAtLeastAsync(
                    settings.InputDelay,
                    cancellationToken).ConfigureAwait(false);
                Report(
                    AutomationState.FocusWindow,
                    section.SectionIndex,
                    sections.Count,
                    "Roblox focus confirmed for this section.");

                await ClickModeControlAsync(
                    AutomationState.SwitchToPositionMode,
                    points?.FocusRelease ?? DryRunPoint,
                    points?.MoveSplineTab ?? DryRunPoint,
                    section,
                    sections.Count,
                    "Calibrated Move spline tab",
                    cancellationToken).ConfigureAwait(false);

                await EnterValueAsync(
                    AutomationState.EnterX,
                    points?.PositionX ?? DryRunPoint,
                    PositionMath.FormatPosition(
                        section.EnteredDelta.X,
                        section.InputPrecision),
                    section,
                    sections.Count,
                    "X",
                    cancellationToken).ConfigureAwait(false);
                await EnterValueAsync(
                    AutomationState.EnterY,
                    points?.PositionY ?? DryRunPoint,
                    PositionMath.FormatPosition(
                        section.EnteredDelta.Y,
                        section.InputPrecision),
                    section,
                    sections.Count,
                    "Y",
                    cancellationToken).ConfigureAwait(false);
                await EnterValueAsync(
                    AutomationState.EnterZ,
                    points?.PositionZ ?? DryRunPoint,
                    PositionMath.FormatPosition(
                        section.EnteredDelta.Z,
                        section.InputPrecision),
                    section,
                    sections.Count,
                    "Z",
                    cancellationToken).ConfigureAwait(false);

                await ClickModeControlAsync(
                    AutomationState.SwitchToRotationMode,
                    points?.FocusRelease ?? DryRunPoint,
                    points?.RotateSplineTab ?? DryRunPoint,
                    section,
                    sections.Count,
                    "Calibrated Rotate spline tab",
                    cancellationToken).ConfigureAwait(false);

                await EnterValueAsync(
                    AutomationState.EnterYaw,
                    points?.Yaw ?? DryRunPoint,
                    AngleMath.FormatRotation(
                        section.Rotation.Yaw,
                        section.InputPrecision),
                    section,
                    sections.Count,
                    "yaw",
                    cancellationToken).ConfigureAwait(false);
                await EnterValueAsync(
                    AutomationState.EnterPitch,
                    points?.Pitch ?? DryRunPoint,
                    AngleMath.FormatRotation(
                        section.Rotation.Pitch,
                        section.InputPrecision),
                    section,
                    sections.Count,
                    "pitch",
                    cancellationToken).ConfigureAwait(false);
                await EnterValueAsync(
                    AutomationState.EnterRoll,
                    points?.Roll ?? DryRunPoint,
                    AngleMath.FormatRotation(
                        section.Rotation.Roll,
                        section.InputPrecision),
                    section,
                    sections.Count,
                    "roll",
                    cancellationToken).ConfigureAwait(false);

                await ReleaseFocusAsync(
                    points?.FocusRelease ?? DryRunPoint,
                    section,
                    sections.Count,
                    cancellationToken).ConfigureAwait(false);
                await automationInput.ClickAsync(
                    points?.BuildButton ?? DryRunPoint,
                    settings.InputDelay,
                    cancellationToken).ConfigureAwait(false);
                Report(
                    AutomationState.Build,
                    section.SectionIndex,
                    sections.Count,
                    "Calibrated Build button click completed.");
                Report(
                    AutomationState.WaitForCompletion,
                    section.SectionIndex,
                    sections.Count,
                    $"Waiting {settings.BuildDelay.TotalMilliseconds:0} ms for build completion.");
                await DelayAtLeastAsync(
                    settings.BuildDelay,
                    cancellationToken).ConfigureAwait(false);
                await WaitUntilResumedAsync(cancellationToken).ConfigureAwait(false);

                Report(
                    AutomationState.Verify,
                    section.SectionIndex,
                    sections.Count,
                    "Fixed build delay completed; section considered successful.");
                Report(
                    AutomationState.Advance,
                    section.SectionIndex,
                    sections.Count,
                    $"Section {section.SectionIndex} completed.");
            }

            Report(
                AutomationState.Completed,
                sections.Count,
                sections.Count,
                $"Completed {sections.Count - startIndex} sections.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Report(
                AutomationState.Stopped,
                _currentProgress.CurrentSection,
                sections.Count,
                "Automation stopped. No undo input was sent.");
            throw;
        }
        catch (Exception exception)
        {
            Report(
                AutomationState.Error,
                _currentProgress.CurrentSection,
                sections.Count,
                exception.Message);
            throw;
        }
        finally
        {
            _ = _pauseGate.Resume();
            Volatile.Write(ref _isRunning, 0);
        }
    }

    private async Task ClickModeControlAsync(
        AutomationState state,
        NormalizedPoint focusReleasePoint,
        NormalizedPoint controlPoint,
        ImportedSection section,
        int totalSections,
        string message,
        CancellationToken cancellationToken)
    {
        await ReleaseFocusAsync(
            focusReleasePoint,
            section,
            totalSections,
            cancellationToken).ConfigureAwait(false);
        await WaitUntilResumedAsync(cancellationToken).ConfigureAwait(false);
        await automationInput.ClickAsync(
            controlPoint,
            settings.InputDelay,
            cancellationToken).ConfigureAwait(false);
        Report(
            state,
            section.SectionIndex,
            totalSections,
            $"{message} click completed.");
        await DelayAtLeastAsync(
            settings.ModeSwitchDelay,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ReleaseFocusAsync(
        NormalizedPoint point,
        ImportedSection section,
        int totalSections,
        CancellationToken cancellationToken)
    {
        await WaitUntilResumedAsync(cancellationToken).ConfigureAwait(false);
        await automationInput.ClickAsync(
            point,
            settings.InputDelay,
            cancellationToken).ConfigureAwait(false);
        Report(
            AutomationState.ReleaseTextboxFocus,
            section.SectionIndex,
            totalSections,
            "Calibrated safe-area click completed; textbox focus released.");
    }

    private async Task EnterValueAsync(
        AutomationState state,
        NormalizedPoint point,
        string value,
        ImportedSection section,
        int totalSections,
        string fieldName,
        CancellationToken cancellationToken)
    {
        await WaitUntilResumedAsync(cancellationToken).ConfigureAwait(false);
        await automationInput.EnterValueAsync(
            point,
            value,
            settings.InputDelay,
            cancellationToken).ConfigureAwait(false);
        await DelayAtLeastAsync(
            settings.InputDelay,
            cancellationToken).ConfigureAwait(false);
        Report(
            state,
            section.SectionIndex,
            totalSections,
            $"Entered {fieldName}: {value} at normalized ({point.X:0.00000}, {point.Y:0.00000}).");
    }

    private Task WaitUntilResumedAsync(CancellationToken cancellationToken) =>
        _pauseGate.WaitAsync(cancellationToken);

    private static async Task DelayAtLeastAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = delay - Stopwatch.GetElapsedTime(startedAt);
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Report(
        AutomationState state,
        int currentSection,
        int totalSections,
        string message)
    {
        var progress = new AutomationProgress(
            state,
            currentSection,
            totalSections,
            message,
            DateTimeOffset.Now);
        _currentProgress = progress;
        ProgressChanged?.Invoke(progress);
    }
}
