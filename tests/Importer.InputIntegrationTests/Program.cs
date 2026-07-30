using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Importer.Windows.Input;
using Importer.Windows.Windowing;

var options = IntegrationOptions.Parse(args);
var repositoryRoot = FindRepositoryRoot();
var configuration = GetBuildConfiguration();
var targetExecutable = Path.Combine(
    repositoryRoot,
    "tests",
    "Importer.InputTestTarget",
    "bin",
    configuration,
    "net10.0-windows",
    "Importer.InputTestTarget.exe");
if (!File.Exists(targetExecutable))
{
    throw new FileNotFoundException(
        "Build the solution before running the input integration test.",
        targetExecutable);
}

var sessionDirectory = Path.Combine(
    repositoryRoot,
    "TestResults",
    "InputIntegration",
    $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
Directory.CreateDirectory(sessionDirectory);
Console.WriteLine($"Session: {sessionDirectory}");

using var targetProcess = StartTarget(targetExecutable, sessionDirectory);
try
{
    var readyPath = Path.Combine(sessionDirectory, "ready.json");
    var ready = await WaitForReadyAsync(
        readyPath,
        targetProcess,
        TimeSpan.FromSeconds(15));
    FocusTarget(new nint(ready.WindowHandle));

    var simulator = new WindowsInputSimulator();
    var expectedBuilds = new List<IReadOnlyDictionary<string, string>>();
    for (var iteration = 1; iteration <= options.Iterations; iteration++)
    {
        var expected = CreateExpectedValues(iteration);
        expectedBuilds.Add(expected);

        await ClickAsync(simulator, ready, "FocusRelease", options);
        await ClickAsync(simulator, ready, "MoveSplineTab", options);
        await Task.Delay(options.TabDelay);
        await EnterAsync(simulator, ready, "PositionX", expected["PositionX"], options);
        await EnterAsync(simulator, ready, "PositionY", expected["PositionY"], options);
        await EnterAsync(simulator, ready, "PositionZ", expected["PositionZ"], options);

        await ClickAsync(simulator, ready, "FocusRelease", options);
        await ClickAsync(simulator, ready, "RotateSplineTab", options);
        await Task.Delay(options.TabDelay);
        await EnterAsync(simulator, ready, "Yaw", expected["Yaw"], options);
        await EnterAsync(simulator, ready, "Pitch", expected["Pitch"], options);
        await EnterAsync(simulator, ready, "Roll", expected["Roll"], options);

        await ClickAsync(simulator, ready, "FocusRelease", options);
        await ClickAsync(simulator, ready, "BuildButton", options);
        await Task.Delay(options.BuildDelay);
        Console.WriteLine($"Completed input iteration {iteration}/{options.Iterations}.");
    }

    await Task.Delay(TimeSpan.FromMilliseconds(500));
    var eventPath = Path.Combine(sessionDirectory, "events.jsonl");
    var actualBuilds = ReadBuildSnapshots(eventPath);
    ValidateBuilds(expectedBuilds, actualBuilds, eventPath);
    var mouseMoveCount = CountEvents(eventPath, "MouseMove");
    var minimumMouseMoveCount = options.Iterations * 20;
    if (mouseMoveCount < minimumMouseMoveCount)
    {
        throw new InvalidOperationException(
            $"Expected at least {minimumMouseMoveCount} observed mouse-move samples, " +
            $"but observed {mouseMoveCount}. See {eventPath}.");
    }

    Console.WriteLine(
        $"PASS: {actualBuilds.Count} build snapshots matched all six intended fields; " +
        $"{mouseMoveCount} mouse-move samples were observed.");
}
finally
{
    if (!targetProcess.HasExited)
    {
        _ = targetProcess.CloseMainWindow();
        if (!targetProcess.WaitForExit(5_000))
        {
            targetProcess.Kill(entireProcessTree: true);
            targetProcess.WaitForExit();
        }
    }
}

static Process StartTarget(string executable, string sessionDirectory)
{
    var startInfo = new ProcessStartInfo(executable)
    {
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add("--session-dir");
    startInfo.ArgumentList.Add(sessionDirectory);
    return Process.Start(startInfo) ??
           throw new InvalidOperationException("The instrumented input target did not start.");
}

static async Task<TargetReady> WaitForReadyAsync(
    string readyPath,
    Process process,
    TimeSpan timeout)
{
    var startedAt = Stopwatch.GetTimestamp();
    while (Stopwatch.GetElapsedTime(startedAt) < timeout)
    {
        if (process.HasExited)
        {
            throw new InvalidOperationException(
                $"The instrumented target exited with code {process.ExitCode} before becoming ready.");
        }

        if (File.Exists(readyPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(readyPath);
                var ready = JsonSerializer.Deserialize<TargetReady>(json);
                if (ready is not null)
                {
                    return ready;
                }
            }
            catch (IOException)
            {
                // The target may still be replacing the ready file.
            }
            catch (JsonException)
            {
                // The target may still be flushing the ready file.
            }
        }

        await Task.Delay(50);
    }

    throw new TimeoutException("Timed out waiting for the instrumented target window.");
}

static void FocusTarget(nint windowHandle)
{
    if (NativeMethods.IsIconic(windowHandle))
    {
        _ = NativeMethods.ShowWindowAsync(windowHandle, NativeMethods.SwRestore);
    }

    for (var attempt = 0; attempt < 10; attempt++)
    {
        if (NativeMethods.GetForegroundWindow() == windowHandle)
        {
            return;
        }

        _ = NativeMethods.SetForegroundWindow(windowHandle);
        Thread.Sleep(100);
    }

    throw new Win32Exception(
        "Windows did not allow the instrumented input target to become the foreground window.");
}

static Task ClickAsync(
    WindowsInputSimulator simulator,
    TargetReady ready,
    string control,
    IntegrationOptions options)
{
    var point = ready.Controls[control];
    return simulator.ClickAsync(
        new PixelPoint(point.X, point.Y),
        options.InputDelay,
        CancellationToken.None);
}

static async Task EnterAsync(
    WindowsInputSimulator simulator,
    TargetReady ready,
    string control,
    string value,
    IntegrationOptions options)
{
    var point = ready.Controls[control];
    await simulator.EnterValueAsync(
        new PixelPoint(point.X, point.Y),
        value,
        options.InputDelay,
        CancellationToken.None);
    await Task.Delay(options.InputDelay);
}

static IReadOnlyDictionary<string, string> CreateExpectedValues(int iteration) =>
    new Dictionary<string, string>
    {
        ["PositionX"] = $"{iteration}.123456789012345",
        ["PositionY"] = $"-{iteration}.23456789012345",
        ["PositionZ"] = $"{iteration}.34567890123456",
        ["Yaw"] = $"{iteration}.4567890123456",
        ["Pitch"] = iteration == 1
            ? "-150.307132468909"
            : $"-{149 + iteration}.307132468909",
        ["Roll"] = $"{iteration}.56789012345678",
    };

static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadBuildSnapshots(string eventPath)
{
    var builds = new List<IReadOnlyDictionary<string, string>>();
    using var stream = new FileStream(
        eventPath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite);
    using var reader = new StreamReader(stream);
    while (reader.ReadLine() is { } line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (!string.Equals(
                root.GetProperty("Kind").GetString(),
                "Build",
                StringComparison.Ordinal))
        {
            continue;
        }

        var fields = root.GetProperty("Details")
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.GetString() ?? string.Empty);
        builds.Add(fields);
    }

    return builds;
}

static int CountEvents(string eventPath, string kind)
{
    var count = 0;
    using var stream = new FileStream(
        eventPath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite);
    using var reader = new StreamReader(stream);
    while (reader.ReadLine() is { } line)
    {
        using var document = JsonDocument.Parse(line);
        if (string.Equals(
                document.RootElement.GetProperty("Kind").GetString(),
                kind,
                StringComparison.Ordinal))
        {
            count++;
        }
    }

    return count;
}

static void ValidateBuilds(
    IReadOnlyList<IReadOnlyDictionary<string, string>> expected,
    IReadOnlyList<IReadOnlyDictionary<string, string>> actual,
    string eventPath)
{
    if (actual.Count != expected.Count)
    {
        throw new InvalidOperationException(
            $"Expected {expected.Count} Build events but observed {actual.Count}. " +
            $"See {eventPath}.");
    }

    for (var iteration = 0; iteration < expected.Count; iteration++)
    {
        foreach (var (field, expectedValue) in expected[iteration])
        {
            actual[iteration].TryGetValue(field, out var actualValue);
            if (!string.Equals(expectedValue, actualValue, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Iteration {iteration + 1}, {field}: expected '{expectedValue}', " +
                    $"observed '{actualValue}'. See {eventPath}.");
            }
        }
    }
}

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "NL2ToTPT2.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate the repository root.");
}

static string GetBuildConfiguration()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (string.Equals(directory.Name, "Debug", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(directory.Name, "Release", StringComparison.OrdinalIgnoreCase))
        {
            return directory.Name;
        }

        directory = directory.Parent;
    }

    return "Debug";
}

sealed record IntegrationOptions(
    int Iterations,
    TimeSpan InputDelay,
    TimeSpan TabDelay,
    TimeSpan BuildDelay)
{
    public static IntegrationOptions Parse(IReadOnlyList<string> arguments)
    {
        var iterations = ReadInt(arguments, "--iterations", 10);
        var inputDelay = ReadMilliseconds(arguments, "--input-delay", 100);
        var tabDelay = ReadMilliseconds(arguments, "--tab-delay", 250);
        var buildDelay = ReadMilliseconds(arguments, "--build-delay", 250);
        if (iterations <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(arguments),
                "Iterations must be positive.");
        }

        return new IntegrationOptions(iterations, inputDelay, tabDelay, buildDelay);
    }

    private static int ReadInt(
        IReadOnlyList<string> arguments,
        string name,
        int defaultValue)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(
                    arguments[index + 1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                return value;
            }
        }

        return defaultValue;
    }

    private static TimeSpan ReadMilliseconds(
        IReadOnlyList<string> arguments,
        string name,
        double defaultValue)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(
                    arguments[index + 1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value) &&
                value >= 0)
            {
                return TimeSpan.FromMilliseconds(value);
            }
        }

        return TimeSpan.FromMilliseconds(defaultValue);
    }
}

sealed record TargetReady(
    int ProcessId,
    long WindowHandle,
    IReadOnlyDictionary<string, ScreenPoint> Controls);

sealed record ScreenPoint(int X, int Y);

static class NativeMethods
{
    internal const int SwRestore = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindowAsync(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();
}
