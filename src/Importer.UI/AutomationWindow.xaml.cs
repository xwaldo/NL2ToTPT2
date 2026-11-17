using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Importer.Core.Conversion;
using Importer.Core.Models;
using Importer.Windows.Automation;
using Importer.Windows.Calibration;
using Importer.Windows.Hotkeys;
using Importer.Windows.Input;
using Importer.Windows.Windowing;
using Microsoft.Win32;

namespace Importer.UI;

public partial class AutomationWindow : Window
{
    private const string PreferencesKey = @"HKEY_CURRENT_USER\Software\NL2ToTPT2";
    private const string EmergencyHotkeyPreference = "EmergencyHotkey";

    private readonly IReadOnlyList<ImportedSection> _sections;
    private readonly ImportAnchor _anchor;
    private readonly Dictionary<CalibrationTarget, NormalizedPoint> _calibration = [];
    private readonly List<string> _logLines = [];
    private readonly RobloxWindowLocator _windowLocator = new();
    private readonly WindowsInputSimulator _inputSimulator = new();
    private readonly CalibrationProfileStore _calibrationStore = new();
    private EmergencyHotkey? _emergencyHotkey;
    private HwndSource? _windowSource;
    private CancellationTokenSource? _automationCancellation;
    private AutomationRunner? _automationRunner;
    private bool _captureInProgress;

    public AutomationWindow(ImportAnchor anchor, IReadOnlyList<ImportedSection> sections)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(sections);
        _anchor = anchor;
        _sections = sections;
        InitializeComponent();
        SectionCountTextBlock.Text = $"{sections.Count:N0} sections available";
        AnchorSummaryTextBlock.Text =
            $"ANCHOR — Reset the starting section to a straight shape, then rotate the selected starting node to " +
            $"{FormatRotation(anchor.Rotation, anchor.InputPrecision)}. " +
            "Section 1 builds Point 1 → Point 2.";
        AutomationProgressBar.Maximum = sections.Count;
        LoadSavedCalibration();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var windowHandle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(windowHandle);
        _windowSource.AddHook(WindowMessageHook);

        EmergencyHotkeyComboBox.ItemsSource = Enum.GetValues<EmergencyHotkeyKey>();
        var preferredKey = LoadEmergencyHotkey();
        EmergencyHotkeyComboBox.SelectedItem = preferredKey;
        TryRegisterEmergencyHotkey(windowHandle, preferredKey);
    }

    private EmergencyHotkeyKey LoadEmergencyHotkey()
    {
        try
        {
            if (Registry.GetValue(PreferencesKey, EmergencyHotkeyPreference, null) is string saved &&
                Enum.TryParse<EmergencyHotkeyKey>(saved, out var key) && Enum.IsDefined(key))
            {
                return key;
            }
        }
        catch (Exception exception) when (exception is IOException or SecurityException or UnauthorizedAccessException)
        {
            AppendLog($"Could not load the emergency shortcut: {exception.Message}");
        }

        return EmergencyHotkeyKey.X;
    }

    private void ApplyEmergencyHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_automationCancellation is not null ||
            EmergencyHotkeyComboBox.SelectedItem is not EmergencyHotkeyKey key ||
            !TryRegisterEmergencyHotkey(new WindowInteropHelper(this).Handle, key))
        {
            return;
        }

        try
        {
            Registry.SetValue(PreferencesKey, EmergencyHotkeyPreference, key.ToString());
        }
        catch (Exception exception) when (exception is IOException or SecurityException or UnauthorizedAccessException)
        {
            EmergencyHotkeyStatusTextBlock.Text = "Shortcut active for this session. Windows could not save it.";
            AppendLog($"Could not save the emergency shortcut: {exception.Message}");
        }
    }

    private bool TryRegisterEmergencyHotkey(nint windowHandle, EmergencyHotkeyKey key)
    {
        if (_emergencyHotkey?.Key != key)
        {
            try
            {
                var replacement = EmergencyHotkey.Register(windowHandle, key);
                _emergencyHotkey?.Dispose();
                _emergencyHotkey = replacement;
                AppendLog($"Emergency hotkey registered: {replacement.DisplayName}.");
            }
            catch (Win32Exception exception)
            {
                var activeStatus = _emergencyHotkey is null
                    ? "Dry-run is available."
                    : $"{_emergencyHotkey.DisplayName} is still active.";
                EmergencyHotkeyStatusTextBlock.Text =
                    $"Could not register Ctrl+Shift+{key}. Choose another key and select Apply shortcut. {activeStatus}";
                AppendLog($"Could not register Ctrl+Shift+{key}: {exception.Message}");
                return false;
            }
        }

        EmergencyHotkeyTextBlock.Text = $"Emergency stop: Ctrl+Shift+{key}";
        EmergencyHotkeyStatusTextBlock.Text = string.Empty;
        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        EmergencyStop("Auto mode window closed.");
        _emergencyHotkey?.Dispose();
        if (_windowSource is not null)
        {
            _windowSource.RemoveHook(WindowMessageHook);
        }

        _automationCancellation?.Dispose();
        base.OnClosed(e);
    }

    private async void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_captureInProgress || _automationRunner?.IsRunning == true)
        {
            return;
        }

        if (sender is not Button button ||
            button.Tag is not string tag ||
            !Enum.TryParse<CalibrationTarget>(tag, out var target))
        {
            return;
        }

        _captureInProgress = true;
        SetCalibrationButtonsEnabled(false);
        AutomationMessageTextBlock.Text =
            $"Move the cursor to {GetDisplayName(target)} in Roblox. Capturing in three seconds.";

        try
        {
            var windowHandle = _windowLocator.Locate();
            WindowState = WindowState.Minimized;
            _windowLocator.Focus(windowHandle);
            await ShowTextboxGroupForCalibrationAsync(target, windowHandle);
            await Task.Delay(TimeSpan.FromSeconds(3));

            var point = _windowLocator.CaptureCursorPosition(windowHandle);
            _calibration[target] = point;
            UpdateCalibrationText(target, point);
            var saved = TrySaveCalibration();
            AppendLog(
                $"Captured {GetDisplayName(target)} at normalized ({point.X:0.00000}, {point.Y:0.00000})" +
                $"{(saved ? " and saved." : " for this session only.")}");
            AutomationMessageTextBlock.Text =
                $"{GetDisplayName(target)} calibrated{(saved ? " and saved." : ". Save failed; see log.")}";
        }
        catch (Exception exception)
        {
            AutomationMessageTextBlock.Text = $"Calibration failed: {exception.Message}";
            MessageBox.Show(
                this,
                exception.Message,
                "Calibration failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            WindowState = WindowState.Normal;
            _ = Activate();
            _captureInProgress = false;
            SetCalibrationButtonsEnabled(true);
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_automationRunner?.IsRunning == true)
        {
            return;
        }

        if (!TryReadSettings(out var settings, out var startIndex))
        {
            return;
        }

        ControlCalibration? calibration = null;
        string? emergencyHotkeyName = null;
        if (!settings.DryRun)
        {
            if (_emergencyHotkey is not { } emergencyHotkey)
            {
                MessageBox.Show(
                    this,
                    "Choose an available emergency stop key and select Apply shortcut before starting live automation.",
                    "Emergency hotkey required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            emergencyHotkeyName = emergencyHotkey.DisplayName;

            calibration = BuildCalibration();
            if (calibration is null)
            {
                MessageBox.Show(
                    this,
                    "Capture all textbox centers, editor buttons, and the safe focus-release area before starting live automation.",
                    "Calibration required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var translationSpace =
                _sections[0].PositionReference == PositionReferenceMode.PreviousNodeAxes
                    ? "Previous node"
                    : "World";
            var startingPoint = startIndex + 1;
            var startingRotation = startIndex == 0
                ? _anchor.Rotation
                : _sections[startIndex - 1].Rotation;
            var startingPrecision = startIndex == 0
                ? _anchor.InputPrecision
                : _sections[startIndex - 1].InputPrecision;
            var confirmation = MessageBox.Show(
                this,
                $"Live mode will enter {_sections.Count - startIndex:N0} sections into Roblox.{Environment.NewLine}" +
                "Reset the selected starting section to a straight shape first. " +
                $"Select Point {startingPoint}'s node and rotate it to " +
                $"{FormatRotation(startingRotation, startingPrecision)}. " +
                $"Section {startIndex + 1} builds Point {startingPoint} → Point {startingPoint + 1}." +
                $"{Environment.NewLine}" +
                $"Confirm Advanced editor is open, Translation Space is {translationSpace}, " +
                "Auto smooth is off, every snap option is off, and Roblox is windowed or borderless-windowed." +
                $"{Environment.NewLine}" +
                "Do not use the mouse or keyboard during automation. Watch for editor warnings." +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"Emergency stop: {emergencyHotkeyName}",
                "Start live automation?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }
        }

        IAutomationInput automationInput = settings.DryRun
            ? new DryRunAutomationInput()
            : new RobloxAutomationInput(
                _windowLocator,
                _inputSimulator,
                settings.MouseMovementMode,
                settings.TextInputDelay,
                settings.MouseMovementDelay,
                settings.MouseButtonDelay);
        _automationRunner = new AutomationRunner(automationInput, settings);
        _automationRunner.ProgressChanged += AutomationProgressChanged;
        _automationCancellation = new CancellationTokenSource();
        SetRunningState(true);
        AppendLog(
            $"{(settings.DryRun ? "Dry-run" : "Live automation")} started at section {startIndex + 1}; " +
            $"mouse movement: {settings.MouseMovementMode}; " +
            $"movement/hold/button/text/tab/build ms: " +
            $"{settings.MouseMovementDelay.TotalMilliseconds:0.###}/" +
            $"{settings.InputDelay.TotalMilliseconds:0.###}/" +
            $"{settings.MouseButtonDelay.TotalMilliseconds:0.###}/" +
            $"{settings.TextInputDelay.TotalMilliseconds:0.###}/" +
            $"{settings.ModeSwitchDelay.TotalMilliseconds:0.###}/" +
            $"{settings.BuildDelay.TotalMilliseconds:0.###}.");

        try
        {
            if (emergencyHotkeyName is not null)
            {
                await RunLiveStartCountdownAsync(emergencyHotkeyName, _automationCancellation.Token);
            }

            await _automationRunner.RunAsync(
                _sections,
                calibration,
                startIndex,
                _automationCancellation.Token);
        }
        catch (OperationCanceledException) when (_automationCancellation.IsCancellationRequested)
        {
            if (!_automationRunner.IsRunning)
            {
                AutomationStateTextBlock.Text = "Stopped";
                AutomationMessageTextBlock.Text = "Automation stopped during the start countdown.";
                AppendLog("Automation stopped during the start countdown.");
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Automation stopped with an error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _automationRunner.ProgressChanged -= AutomationProgressChanged;
            _automationRunner = null;
            _automationCancellation.Dispose();
            _automationCancellation = null;
            SetRunningState(false);
        }
    }

    private static string FormatRotation(
        EulerAngles rotation,
        InputPrecisionMode precision) =>
        $"Yaw {AngleMath.FormatRotation(rotation.Yaw, precision)}, " +
        $"Pitch {AngleMath.FormatRotation(rotation.Pitch, precision)}, " +
        $"Roll {AngleMath.FormatRotation(rotation.Roll, precision)}";

    private async Task RunLiveStartCountdownAsync(string emergencyHotkeyName, CancellationToken cancellationToken)
    {
        const int countdownSeconds = 3;
        AutomationStateTextBlock.Text = "Starting live automation";
        AppendLog("Live input starts in three seconds. Switch to Roblox now.");

        for (var remaining = countdownSeconds; remaining > 0; remaining--)
        {
            AutomationMessageTextBlock.Text =
                $"Switch to Roblox now. Starting in {remaining}… Emergency stop: {emergencyHotkeyName}.";
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_automationRunner is null)
        {
            return;
        }

        if (_automationRunner.IsPaused)
        {
            _automationRunner.Resume();
            PauseButton.Content = "_Pause";
        }
        else
        {
            _automationRunner.Pause();
            PauseButton.Content = "_Resume";
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) =>
        EmergencyStop("Stop button pressed.");

    private void ClearSavedCalibrationButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            this,
            "Clear every saved calibration point?",
            "Clear calibration",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _calibrationStore.Clear();
            _calibration.Clear();
            ResetCalibrationText();
            AutomationMessageTextBlock.Text = "Saved calibration cleared.";
            AppendLog("Saved calibration cleared.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Could not clear calibration",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private nint WindowMessageHook(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (_emergencyHotkey is { } hotkey && hotkey.IsEmergencyMessage(message, wordParameter))
        {
            EmergencyStop($"{hotkey.DisplayName} pressed.");
            handled = true;
        }

        return nint.Zero;
    }

    private void EmergencyStop(string reason)
    {
        if (_automationCancellation is { IsCancellationRequested: false })
        {
            AppendLog($"EMERGENCY STOP: {reason}");
            AutomationMessageTextBlock.Text = reason;
            _automationCancellation.Cancel();
        }
    }

    private void AutomationProgressChanged(AutomationProgress progress)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            AutomationStateTextBlock.Text =
                $"{progress.State} — section {progress.CurrentSection:N0}/{progress.TotalSections:N0}";
            AutomationMessageTextBlock.Text = progress.Message;
            AutomationProgressBar.Value = Math.Clamp(
                progress.CurrentSection,
                0,
                progress.TotalSections);
            AppendLog(
                $"[{progress.State}] {progress.CurrentSection}/{progress.TotalSections}: {progress.Message}",
                progress.OccurredAt);
        });
    }

    private bool TryReadSettings(
        out AutomationSettings settings,
        out int startIndex)
    {
        settings = new AutomationSettings();
        startIndex = 0;

        if (!TryReadNonNegativeMilliseconds(InputDelayTextBox.Text, out var inputDelay) ||
            !TryReadNonNegativeMilliseconds(MouseMovementDelayTextBox.Text, out var mouseMovementDelay) ||
            !TryReadNonNegativeMilliseconds(MouseButtonDelayTextBox.Text, out var mouseButtonDelay) ||
            !TryReadNonNegativeMilliseconds(TextInputDelayTextBox.Text, out var textInputDelay) ||
            !TryReadNonNegativeMilliseconds(BuildDelayTextBox.Text, out var buildDelay) ||
            !TryReadNonNegativeMilliseconds(ModeSwitchDelayTextBox.Text, out var modeSwitchDelay))
        {
            MessageBox.Show(
                this,
                "Mouse movement, target hold, mouse button, text-step, tab-settle, and build-settle delays must be non-negative millisecond values.",
                "Invalid delay",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (MouseMovementModeComboBox.SelectedItem is not ComboBoxItem movementItem ||
            movementItem.Tag is not string movementTag ||
            !Enum.TryParse<MouseMovementMode>(movementTag, out var mouseMovementMode))
        {
            MessageBox.Show(
                this,
                "Select a valid mouse movement mode.",
                "Invalid mouse movement",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(
                StartSectionTextBox.Text,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out var sectionNumber) ||
            sectionNumber < 1 ||
            sectionNumber > _sections.Count)
        {
            MessageBox.Show(
                this,
                $"Start section must be between 1 and {_sections.Count:N0}.",
                "Invalid start section",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        settings = new AutomationSettings
        {
            InputDelay = inputDelay,
            MouseMovementDelay = mouseMovementDelay,
            MouseButtonDelay = mouseButtonDelay,
            BuildDelay = buildDelay,
            ModeSwitchDelay = modeSwitchDelay,
            TextInputDelay = textInputDelay,
            MouseMovementMode = mouseMovementMode,
            DryRun = DryRunCheckBox.IsChecked == true,
        };
        startIndex = sectionNumber - 1;
        return true;
    }

    private static bool TryReadNonNegativeMilliseconds(
        string text,
        out TimeSpan value)
    {
        if (double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out var milliseconds) &&
            double.IsFinite(milliseconds) &&
            milliseconds >= 0.0 &&
            milliseconds <= TimeSpan.MaxValue.TotalMilliseconds)
        {
            value = TimeSpan.FromMilliseconds(milliseconds);
            return true;
        }

        value = TimeSpan.Zero;
        return false;
    }

    private ControlCalibration? BuildCalibration()
    {
        if (Enum.GetValues<CalibrationTarget>().Any(target => !_calibration.ContainsKey(target)))
        {
            return null;
        }

        return new ControlCalibration
        {
            PositionX = _calibration[CalibrationTarget.PositionX],
            PositionY = _calibration[CalibrationTarget.PositionY],
            PositionZ = _calibration[CalibrationTarget.PositionZ],
            Yaw = _calibration[CalibrationTarget.Yaw],
            Pitch = _calibration[CalibrationTarget.Pitch],
            Roll = _calibration[CalibrationTarget.Roll],
            FocusRelease = _calibration[CalibrationTarget.FocusRelease],
            MoveSplineTab = _calibration[CalibrationTarget.MoveSplineTab],
            RotateSplineTab = _calibration[CalibrationTarget.RotateSplineTab],
            BuildButton = _calibration[CalibrationTarget.BuildButton],
        };
    }

    private void LoadSavedCalibration()
    {
        try
        {
            var savedPoints = _calibrationStore.Load();
            foreach (var (target, point) in savedPoints)
            {
                _calibration[target] = point;
                UpdateCalibrationText(target, point);
            }

            if (savedPoints.Count > 0)
            {
                AutomationMessageTextBlock.Text =
                    $"Loaded {savedPoints.Count}/{Enum.GetValues<CalibrationTarget>().Length} saved calibration points.";
                AppendLog(
                    $"Loaded {savedPoints.Count} calibration points from {_calibrationStore.FilePath}.");
            }
        }
        catch (Exception exception)
        {
            AutomationMessageTextBlock.Text = "Saved calibration could not be loaded; see log.";
            AppendLog($"Could not load saved calibration: {exception.Message}");
        }
    }

    private bool TrySaveCalibration()
    {
        try
        {
            _calibrationStore.Save(_calibration);
            return true;
        }
        catch (Exception exception)
        {
            AppendLog($"Could not save calibration: {exception.Message}");
            return false;
        }
    }

    private void UpdateCalibrationText(
        CalibrationTarget target,
        NormalizedPoint point)
    {
        var text = $"X {point.X:0.00000}, Y {point.Y:0.00000}";
        switch (target)
        {
            case CalibrationTarget.PositionX:
                PositionXValueText.Text = text;
                break;
            case CalibrationTarget.PositionY:
                PositionYValueText.Text = text;
                break;
            case CalibrationTarget.PositionZ:
                PositionZValueText.Text = text;
                break;
            case CalibrationTarget.Yaw:
                YawValueText.Text = text;
                break;
            case CalibrationTarget.Pitch:
                PitchValueText.Text = text;
                break;
            case CalibrationTarget.Roll:
                RollValueText.Text = text;
                break;
            case CalibrationTarget.FocusRelease:
                FocusReleaseValueText.Text = text;
                break;
            case CalibrationTarget.MoveSplineTab:
                MoveSplineTabValueText.Text = text;
                break;
            case CalibrationTarget.RotateSplineTab:
                RotateSplineTabValueText.Text = text;
                break;
            case CalibrationTarget.BuildButton:
                BuildButtonValueText.Text = text;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    private void SetRunningState(bool running)
    {
        StartButton.IsEnabled = !running;
        PauseButton.IsEnabled = running;
        StopButton.IsEnabled = running;
        InputDelayTextBox.IsEnabled = !running;
        MouseMovementDelayTextBox.IsEnabled = !running;
        MouseButtonDelayTextBox.IsEnabled = !running;
        BuildDelayTextBox.IsEnabled = !running;
        ModeSwitchDelayTextBox.IsEnabled = !running;
        TextInputDelayTextBox.IsEnabled = !running;
        MouseMovementModeComboBox.IsEnabled = !running;
        StartSectionTextBox.IsEnabled = !running;
        DryRunCheckBox.IsEnabled = !running;
        EmergencyHotkeyComboBox.IsEnabled = !running;
        ApplyEmergencyHotkeyButton.IsEnabled = !running;
        ClearSavedCalibrationButton.IsEnabled = !running;
        SetCalibrationButtonsEnabled(!running);
        if (!running)
        {
            PauseButton.Content = "_Pause";
        }
    }

    private void SetCalibrationButtonsEnabled(bool enabled)
    {
        foreach (var button in FindVisualChildren<Button>(this)
                     .Where(button => button.Tag is string))
        {
            button.IsEnabled = enabled;
        }
    }

    private void ResetCalibrationText()
    {
        PositionXValueText.Text = "Not calibrated";
        PositionYValueText.Text = "Not calibrated";
        PositionZValueText.Text = "Not calibrated";
        YawValueText.Text = "Not calibrated";
        PitchValueText.Text = "Not calibrated";
        RollValueText.Text = "Not calibrated";
        FocusReleaseValueText.Text = "Not calibrated";
        MoveSplineTabValueText.Text = "Not calibrated";
        RotateSplineTabValueText.Text = "Not calibrated";
        BuildButtonValueText.Text = "Not calibrated";
    }

    private void AppendLog(
        string message,
        DateTimeOffset? occurredAt = null)
    {
        var timestamp = (occurredAt ?? DateTimeOffset.Now).ToLocalTime();
        var line = $"{timestamp:HH:mm:ss.fff}  {message}";
        _logLines.Add(line);
        if (ImportLogTextBox.Text.Length > 0)
        {
            ImportLogTextBox.AppendText(Environment.NewLine);
        }

        ImportLogTextBox.AppendText(line);
        if (_logLines.Count > 2_000)
        {
            _logLines.RemoveRange(0, 500);
            ImportLogTextBox.Text = string.Join(Environment.NewLine, _logLines);
        }

        ImportLogTextBox.CaretIndex = ImportLogTextBox.Text.Length;
        ImportLogTextBox.ScrollToEnd();
    }

    private async Task ShowTextboxGroupForCalibrationAsync(
        CalibrationTarget target,
        nint windowHandle)
    {
        CalibrationTarget? tabTarget = target switch
        {
            CalibrationTarget.PositionX or
            CalibrationTarget.PositionY or
            CalibrationTarget.PositionZ => CalibrationTarget.MoveSplineTab,
            CalibrationTarget.Yaw or
            CalibrationTarget.Pitch or
            CalibrationTarget.Roll => CalibrationTarget.RotateSplineTab,
            _ => null,
        };

        if (tabTarget is not null &&
            _calibration.TryGetValue(tabTarget.Value, out var tabPoint))
        {
            var screenPoint = _windowLocator.ToScreenPoint(windowHandle, tabPoint);
            await _inputSimulator.ClickAsync(
                screenPoint,
                TimeSpan.FromMilliseconds(200),
                CancellationToken.None);
        }
    }

    private static string GetDisplayName(CalibrationTarget target) =>
        target switch
        {
            CalibrationTarget.PositionX => "position X",
            CalibrationTarget.PositionY => "position Y",
            CalibrationTarget.PositionZ => "position Z",
            CalibrationTarget.Yaw => "yaw",
            CalibrationTarget.Pitch => "pitch",
            CalibrationTarget.Roll => "roll",
            CalibrationTarget.FocusRelease => "focus release area",
            CalibrationTarget.MoveSplineTab => "Move spline tab",
            CalibrationTarget.RotateSplineTab => "Rotate spline tab",
            CalibrationTarget.BuildButton => "Build button",
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
