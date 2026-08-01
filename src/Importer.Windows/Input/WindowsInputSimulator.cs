using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Importer.Windows.Native;
using Importer.Windows.Windowing;

namespace Importer.Windows.Input;

public sealed class WindowsInputSimulator
{
    private static readonly TimeSpan MinimumActionDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan MouseSampleInterval =
        TimeSpan.FromTicks(
            (long)Math.Ceiling(TimeSpan.TicksPerSecond / 60.0));
    private static readonly TimeSpan LowLevelEventDelay = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan MaximumPreferredGlideDuration =
        TimeSpan.FromMilliseconds(200);
    private const double MicroGlideOffsetPixels = 5.0;
    private const int MaximumMouseMoveAttempts = 3;
    private const int MousePositionTolerance = 2;
    private const double PreferredMouseSpeedPixelsPerSecond = 1_600.0;
    private const double MaximumMouseSpeedPixelsPerSecond = 4_000.0;
    private const ushort VirtualKeyControl = 0x11;
    private const ushort VirtualKeyA = 0x41;
    private const ushort VirtualKeyBackspace = 0x08;
    private const ushort VirtualKeyEnter = 0x0D;

    public async Task ClickAsync(
        PixelPoint point,
        TimeSpan inputDelay,
        CancellationToken cancellationToken)
    {
        await ClickAsync(
            point,
            Max(inputDelay, MinimumActionDelay),
            MouseMovementMode.FullGlide,
            Max(inputDelay, MinimumActionDelay),
            LowLevelEventDelay,
            cancellationToken);
    }

    public async Task ClickAsync(
        PixelPoint point,
        TimeSpan inputDelay,
        MouseMovementMode mouseMovementMode,
        CancellationToken cancellationToken)
    {
        var effectiveDelay = Max(inputDelay, MinimumActionDelay);
        await ClickAsync(
            point,
            effectiveDelay,
            mouseMovementMode,
            effectiveDelay,
            LowLevelEventDelay,
            cancellationToken);
    }

    public async Task ClickAsync(
        PixelPoint point,
        TimeSpan targetHoldDelay,
        MouseMovementMode mouseMovementMode,
        TimeSpan mouseMovementDelay,
        TimeSpan mouseButtonDelay,
        CancellationToken cancellationToken)
    {
        if (targetHoldDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(targetHoldDelay));
        }
        ValidateNonNegative(mouseMovementDelay, nameof(mouseMovementDelay));
        ValidateNonNegative(mouseButtonDelay, nameof(mouseButtonDelay));

        cancellationToken.ThrowIfCancellationRequested();
        var target = CreateMouseTarget(point);
        await RefreshMousePositionAsync(
            target,
            targetHoldDelay,
            mouseMovementDelay,
            mouseMovementMode,
            cancellationToken);
        await ClickMouseAsync(
            target,
            mouseButtonDelay,
            cancellationToken);
        await DelayAtLeastAsync(targetHoldDelay, cancellationToken);
    }

    public async Task EnterValueAsync(
        PixelPoint point,
        string value,
        TimeSpan inputDelay,
        CancellationToken cancellationToken)
    {
        await EnterValueAsync(
            point,
            value,
            inputDelay,
            MouseMovementMode.FullGlide,
            inputDelay,
            inputDelay,
            LowLevelEventDelay,
            cancellationToken);
    }

    public async Task EnterValueAsync(
        PixelPoint point,
        string value,
        TimeSpan inputDelay,
        MouseMovementMode mouseMovementMode,
        CancellationToken cancellationToken)
    {
        await EnterValueAsync(
            point,
            value,
            inputDelay,
            mouseMovementMode,
            inputDelay,
            inputDelay,
            LowLevelEventDelay,
            cancellationToken);
    }

    public async Task EnterValueAsync(
        PixelPoint point,
        string value,
        TimeSpan inputDelay,
        MouseMovementMode mouseMovementMode,
        TimeSpan textInputDelay,
        CancellationToken cancellationToken)
    {
        var effectiveDelay = Max(inputDelay, MinimumActionDelay);
        await EnterValueAsync(
            point,
            value,
            effectiveDelay,
            mouseMovementMode,
            Max(textInputDelay, MinimumActionDelay),
            effectiveDelay,
            LowLevelEventDelay,
            cancellationToken);
    }

    public async Task EnterValueAsync(
        PixelPoint point,
        string value,
        TimeSpan targetHoldDelay,
        MouseMovementMode mouseMovementMode,
        TimeSpan textInputDelay,
        TimeSpan mouseMovementDelay,
        TimeSpan mouseButtonDelay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateNonNegative(targetHoldDelay, nameof(targetHoldDelay));
        ValidateNonNegative(textInputDelay, nameof(textInputDelay));
        ValidateNonNegative(mouseMovementDelay, nameof(mouseMovementDelay));
        ValidateNonNegative(mouseButtonDelay, nameof(mouseButtonDelay));

        cancellationToken.ThrowIfCancellationRequested();
        var target = CreateMouseTarget(point);
        await RefreshMousePositionAsync(
            target,
            targetHoldDelay,
            mouseMovementDelay,
            mouseMovementMode,
            cancellationToken);
        await ClickMouseAsync(
            target,
            mouseButtonDelay,
            cancellationToken);
        await ClickMouseAsync(
            target,
            mouseButtonDelay,
            cancellationToken);
        await DelayAtLeastAsync(textInputDelay, cancellationToken);

        await SendKeyChordAsync(
            VirtualKeyControl,
            VirtualKeyA,
            textInputDelay,
            cancellationToken);
        await SendVirtualKeyAsync(
            VirtualKeyBackspace,
            textInputDelay,
            cancellationToken);
        await SendUnicodeValueAsync(
            value,
            textInputDelay,
            cancellationToken);
        await DelayAtLeastAsync(textInputDelay, cancellationToken);
        await SendVirtualKeyAsync(
            VirtualKeyEnter,
            textInputDelay,
            cancellationToken);
    }

    private static MouseTarget CreateMouseTarget(PixelPoint point)
    {
        var left = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        var top = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);
        if (width <= 1 || height <= 1)
        {
            throw new InvalidOperationException("Windows reported an invalid virtual screen size.");
        }

        return new MouseTarget(
            point,
            new AbsoluteMousePoint(
                (int)Math.Round((point.X - left) * 65535.0 / (width - 1)),
                (int)Math.Round((point.Y - top) * 65535.0 / (height - 1))));
    }

    private static async Task RefreshMousePositionAsync(
        MouseTarget target,
        TimeSpan targetHoldDelay,
        TimeSpan mouseMovementDelay,
        MouseMovementMode mouseMovementMode,
        CancellationToken cancellationToken)
    {
        NativeMethods.NativePoint actualPoint = default;
        for (var attempt = 1; attempt <= MaximumMouseMoveAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!NativeMethods.GetCursorPos(out var startPoint))
            {
                throw new Win32Exception();
            }

            var start = new PixelPoint(startPoint.X, startPoint.Y);
            switch (mouseMovementMode)
            {
                case MouseMovementMode.SnapMicroGlide:
                    await SnapAndMicroGlideAsync(
                        start,
                        target,
                        mouseMovementDelay,
                        cancellationToken);
                    break;

                case MouseMovementMode.FullGlide:
                    var glideDuration = MouseTrajectory.CalculateResponsiveDuration(
                        start,
                        target.ScreenPoint,
                        mouseMovementDelay,
                        PreferredMouseSpeedPixelsPerSecond,
                        MaximumMouseSpeedPixelsPerSecond,
                        MaximumPreferredGlideDuration);
                    await GlideMouseAsync(
                        start,
                        target.ScreenPoint,
                        glideDuration,
                        cancellationToken);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(mouseMovementMode),
                        mouseMovementMode,
                        "Unsupported mouse movement mode.");
            }

            await GlideMouseAsync(
                target.ScreenPoint,
                target.ScreenPoint,
                targetHoldDelay,
                cancellationToken);

            if (!NativeMethods.GetCursorPos(out actualPoint))
            {
                throw new Win32Exception();
            }

            if (Math.Abs(actualPoint.X - target.ScreenPoint.X) <= MousePositionTolerance &&
                Math.Abs(actualPoint.Y - target.ScreenPoint.Y) <= MousePositionTolerance)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Mouse did not reach ({target.ScreenPoint.X}, {target.ScreenPoint.Y}) " +
            $"after {MaximumMouseMoveAttempts} attempts; " +
            $"last position was ({actualPoint.X}, {actualPoint.Y}).");
    }

    private static async Task SnapAndMicroGlideAsync(
        PixelPoint start,
        MouseTarget target,
        TimeSpan mouseMovementDelay,
        CancellationToken cancellationToken)
    {
        var approachPoint = MouseTrajectory.CreateApproachPoint(
            start,
            target.ScreenPoint,
            MicroGlideOffsetPixels);
        SendMouseMove(CreateMouseTarget(approachPoint));
        await DelayAtLeastAsync(MouseSampleInterval, cancellationToken);
        await GlideMouseAsync(
            approachPoint,
            target.ScreenPoint,
            mouseMovementDelay,
            cancellationToken);
    }

    private static void ValidateNonNegative(TimeSpan value, string parameterName)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static async Task GlideMouseAsync(
        PixelPoint start,
        PixelPoint end,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        foreach (var sample in MouseTrajectory.Create(
                     start,
                     end,
                     duration,
                     MouseSampleInterval))
        {
            await DelayUntilAsync(
                startedAt,
                sample.Offset,
                cancellationToken);
            SendMouseMove(CreateMouseTarget(sample.Point));
        }
    }

    private static void SendMouseMove(MouseTarget target)
    {
        Send(
        [
            MouseInput(
                target.AbsolutePoint.X,
                target.AbsolutePoint.Y,
                NativeMethods.MouseEventMove |
                NativeMethods.MouseEventMoveNoCoalesce |
                NativeMethods.MouseEventAbsolute |
                NativeMethods.MouseEventVirtualDesk),
        ]);
    }

    private static async Task ClickMouseAsync(
        MouseTarget target,
        TimeSpan inputDelay,
        CancellationToken cancellationToken)
    {
        const uint positionFlags =
            NativeMethods.MouseEventMove |
            NativeMethods.MouseEventMoveNoCoalesce |
            NativeMethods.MouseEventAbsolute |
            NativeMethods.MouseEventVirtualDesk;

        var buttonDown = false;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Send(
            [
                MouseInput(
                    target.AbsolutePoint.X,
                    target.AbsolutePoint.Y,
                    positionFlags | NativeMethods.MouseEventLeftDown),
            ]);
            buttonDown = true;
            await DelayAtLeastAsync(inputDelay, CancellationToken.None);

            Send(
            [
                MouseInput(
                    target.AbsolutePoint.X,
                    target.AbsolutePoint.Y,
                    positionFlags | NativeMethods.MouseEventLeftUp),
            ]);
            buttonDown = false;
            await DelayAtLeastAsync(inputDelay, cancellationToken);
            VerifyMousePosition(target.ScreenPoint);
        }
        finally
        {
            if (buttonDown)
            {
                Send(
                [
                    MouseInput(
                        target.AbsolutePoint.X,
                        target.AbsolutePoint.Y,
                        positionFlags | NativeMethods.MouseEventLeftUp),
                ]);
            }
        }
    }

    private static async Task SendKeyChordAsync(
        ushort modifier,
        ushort key,
        TimeSpan inputDelay,
        CancellationToken cancellationToken)
    {
        var modifierDown = false;
        var keyDown = false;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Send([KeyboardInput(modifier, keyUp: false)]);
            modifierDown = true;
            await DelayAtLeastAsync(inputDelay, CancellationToken.None);

            Send([KeyboardInput(key, keyUp: false)]);
            keyDown = true;
            await DelayAtLeastAsync(inputDelay, CancellationToken.None);

            Send([KeyboardInput(key, keyUp: true)]);
            keyDown = false;
            await DelayAtLeastAsync(inputDelay, CancellationToken.None);

            Send([KeyboardInput(modifier, keyUp: true)]);
            modifierDown = false;
            await DelayAtLeastAsync(inputDelay, cancellationToken);
        }
        finally
        {
            if (keyDown)
            {
                Send([KeyboardInput(key, keyUp: true)]);
            }

            if (modifierDown)
            {
                Send([KeyboardInput(modifier, keyUp: true)]);
            }
        }
    }

    private static async Task SendVirtualKeyAsync(
        int virtualKey,
        TimeSpan inputDelay,
        CancellationToken cancellationToken)
    {
        var key = checked((ushort)virtualKey);
        var keyDown = false;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Send([KeyboardInput(key, keyUp: false)]);
            keyDown = true;
            await DelayAtLeastAsync(inputDelay, CancellationToken.None);

            Send([KeyboardInput(key, keyUp: true)]);
            keyDown = false;
            await DelayAtLeastAsync(inputDelay, cancellationToken);
        }
        finally
        {
            if (keyDown)
            {
                Send([KeyboardInput(key, keyUp: true)]);
            }
        }
    }

    private static void SendUnicodeInstant(
        string value,
        CancellationToken cancellationToken)
    {
        var inputs = new List<NativeMethods.Input>(value.Length * 2);
        foreach (var character in value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            inputs.Add(UnicodeInput(character, keyUp: false));
            inputs.Add(UnicodeInput(character, keyUp: true));
        }

        Send([.. inputs]);
    }

    private static async Task SendUnicodeValueAsync(
        string value,
        TimeSpan separatorDelay,
        CancellationToken cancellationToken)
    {
        var decimalIndex = value.IndexOf(".", StringComparison.Ordinal);
        if (decimalIndex < 0)
        {
            SendUnicodeInstant(value, cancellationToken);
            return;
        }

        var prefix = value[..decimalIndex];
        var suffix = value[(decimalIndex + 1)..];
        if (prefix.Length > 0)
        {
            SendUnicodeInstant(prefix, cancellationToken);
            await DelayAtLeastAsync(separatorDelay, cancellationToken);
        }

        SendUnicodeInstant(".", cancellationToken);
        await DelayAtLeastAsync(separatorDelay, cancellationToken);

        if (suffix.Length > 0)
        {
            SendUnicodeInstant(suffix, cancellationToken);
        }
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) =>
        left >= right ? left : right;

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

            await Task.Delay(remaining, cancellationToken);
        }
    }

    private static async Task DelayUntilAsync(
        long startedAt,
        TimeSpan offset,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = offset - Stopwatch.GetElapsedTime(startedAt);
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(remaining, cancellationToken);
        }
    }

    private static void VerifyMousePosition(PixelPoint expectedPoint)
    {
        if (!NativeMethods.GetCursorPos(out var actualPoint))
        {
            throw new Win32Exception();
        }

        if (Math.Abs(actualPoint.X - expectedPoint.X) > MousePositionTolerance ||
            Math.Abs(actualPoint.Y - expectedPoint.Y) > MousePositionTolerance)
        {
            throw new InvalidOperationException(
                $"Mouse moved away from ({expectedPoint.X}, {expectedPoint.Y}) during the click; " +
                $"actual position was ({actualPoint.X}, {actualPoint.Y}).");
        }
    }

    private readonly record struct AbsoluteMousePoint(int X, int Y);

    private readonly record struct MouseTarget(
        PixelPoint ScreenPoint,
        AbsoluteMousePoint AbsolutePoint);

    private static NativeMethods.Input MouseInput(int x, int y, uint flags) =>
        new()
        {
            Type = NativeMethods.InputMouse,
            Data = new NativeMethods.InputUnion
            {
                Mouse = new NativeMethods.MouseInput
                {
                    X = x,
                    Y = y,
                    Flags = flags,
                },
            },
        };

    private static NativeMethods.Input KeyboardInput(ushort virtualKey, bool keyUp) =>
        new()
        {
            Type = NativeMethods.InputKeyboard,
            Data = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = keyUp ? NativeMethods.KeyEventKeyUp : 0,
                },
            },
        };

    private static NativeMethods.Input UnicodeInput(char character, bool keyUp) =>
        new()
        {
            Type = NativeMethods.InputKeyboard,
            Data = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KeyboardInput
                {
                    ScanCode = character,
                    Flags = NativeMethods.KeyEventUnicode |
                            (keyUp ? NativeMethods.KeyEventKeyUp : 0),
                },
            },
        };

    private static void Send(NativeMethods.Input[] inputs)
    {
        var sent = NativeMethods.SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<NativeMethods.Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Windows accepted {sent} of {inputs.Length} injected input events. " +
                "Ensure Roblox and this importer run at the same permission level.");
        }
    }
}
