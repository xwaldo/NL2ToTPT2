using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Importer.InputTestTarget;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _readyPath;
    private readonly StreamWriter _eventWriter;
    private long _eventSequence;
    private string _activeMode = "None";
    private Point? _lastMousePosition;

    public MainWindow(string sessionDirectory)
    {
        Directory.CreateDirectory(sessionDirectory);
        _readyPath = Path.Combine(sessionDirectory, "ready.json");
        _eventWriter = new StreamWriter(
            new FileStream(
                Path.Combine(sessionDirectory, "events.jsonl"),
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };

        InitializeComponent();
        AttachTextboxDiagnostics(PositionX);
        AttachTextboxDiagnostics(PositionY);
        AttachTextboxDiagnostics(PositionZ);
        AttachTextboxDiagnostics(Yaw);
        AttachTextboxDiagnostics(Pitch);
        AttachTextboxDiagnostics(Roll);
        RootGrid.PreviewMouseMove += RootGrid_PreviewMouseMove;
        ContentRendered += MainWindow_ContentRendered;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        var controls = new Dictionary<string, ScreenPoint>
        {
            [nameof(PositionX)] = GetCenter(PositionX),
            [nameof(PositionY)] = GetCenter(PositionY),
            [nameof(PositionZ)] = GetCenter(PositionZ),
            [nameof(Yaw)] = GetCenter(Yaw),
            [nameof(Pitch)] = GetCenter(Pitch),
            [nameof(Roll)] = GetCenter(Roll),
            [nameof(FocusRelease)] = GetCenter(FocusRelease),
            [nameof(MoveSplineTab)] = GetCenter(MoveSplineTab),
            [nameof(RotateSplineTab)] = GetCenter(RotateSplineTab),
            [nameof(BuildButton)] = GetCenter(BuildButton),
        };
        var ready = new TargetReady(
            Environment.ProcessId,
            new WindowInteropHelper(this).Handle.ToInt64(),
            controls);
        File.WriteAllText(
            _readyPath,
            JsonSerializer.Serialize(ready, JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Log("Ready", null, null, controls);
        _ = Activate();
    }

    private void AttachTextboxDiagnostics(TextBox textbox)
    {
        textbox.PreviewMouseLeftButtonDown += (_, e) =>
            Log("MouseDown", textbox.Name, $"ClickCount={e.ClickCount}");
        textbox.GotKeyboardFocus += (_, _) =>
            Log("Focus", textbox.Name, textbox.Text);
        textbox.LostKeyboardFocus += (_, _) =>
            Log("Blur", textbox.Name, textbox.Text);
        textbox.TextChanged += (_, _) =>
            Log("TextChanged", textbox.Name, textbox.Text);
        textbox.PreviewKeyDown += (_, e) =>
        {
            Log("KeyDown", textbox.Name, e.Key.ToString());
            if (e.Key != Key.Enter)
            {
                return;
            }

            Log("Commit", textbox.Name, textbox.Text);
            _ = Keyboard.Focus(RootGrid);
            e.Handled = true;
        };
    }

    private void RootGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(RootGrid);
        if (_lastMousePosition == position)
        {
            return;
        }

        _lastMousePosition = position;
        Log(
            "MouseMove",
            null,
            FormattableString.Invariant($"{position.X:0.0},{position.Y:0.0}"));
    }

    private void MoveSplineTab_Click(object sender, RoutedEventArgs e)
    {
        _activeMode = "Move";
        ModeText.Text = "Mode: move";
        Log("TabChanged", nameof(MoveSplineTab), _activeMode);
    }

    private void RotateSplineTab_Click(object sender, RoutedEventArgs e)
    {
        _activeMode = "Rotate";
        ModeText.Text = "Mode: rotate";
        Log("TabChanged", nameof(RotateSplineTab), _activeMode);
    }

    private void FocusRelease_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _ = Keyboard.Focus(RootGrid);
        Log("FocusReleased", nameof(FocusRelease), _activeMode);
    }

    private void BuildButton_Click(object sender, RoutedEventArgs e)
    {
        var fields = new Dictionary<string, string>
        {
            [nameof(PositionX)] = PositionX.Text,
            [nameof(PositionY)] = PositionY.Text,
            [nameof(PositionZ)] = PositionZ.Text,
            [nameof(Yaw)] = Yaw.Text,
            [nameof(Pitch)] = Pitch.Text,
            [nameof(Roll)] = Roll.Text,
        };
        Log("Build", nameof(BuildButton), _activeMode, fields);
    }

    private ScreenPoint GetCenter(FrameworkElement control)
    {
        var point = control.PointToScreen(
            new Point(control.ActualWidth / 2, control.ActualHeight / 2));
        return new ScreenPoint(
            (int)Math.Round(point.X),
            (int)Math.Round(point.Y));
    }

    private void Log(
        string kind,
        string? control,
        string? value,
        object? details = null)
    {
        var entry = new TargetEvent(
            Interlocked.Increment(ref _eventSequence),
            Stopwatch.GetTimestamp(),
            DateTimeOffset.Now,
            kind,
            control,
            value,
            _activeMode,
            details);
        _eventWriter.WriteLine(JsonSerializer.Serialize(entry));
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _eventWriter.Dispose();
    }

    private sealed record TargetReady(
        int ProcessId,
        long WindowHandle,
        IReadOnlyDictionary<string, ScreenPoint> Controls);

    private sealed record ScreenPoint(int X, int Y);

    private sealed record TargetEvent(
        long Sequence,
        long StopwatchTimestamp,
        DateTimeOffset OccurredAt,
        string Kind,
        string? Control,
        string? Value,
        string ActiveMode,
        object? Details);
}
