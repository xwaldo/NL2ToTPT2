using System.ComponentModel;
using System.Diagnostics;
using Importer.Windows.Calibration;
using Importer.Windows.Native;

namespace Importer.Windows.Windowing;

public sealed class RobloxWindowLocator
{
    private const string RobloxProcessName = "RobloxPlayerBeta";

    public nint Locate()
    {
        foreach (var process in Process.GetProcessesByName(RobloxProcessName))
        {
            using (process)
            {
                try
                {
                    var windowHandle = process.MainWindowHandle;
                    if (windowHandle != nint.Zero &&
                        NativeMethods.IsWindow(windowHandle) &&
                        NativeMethods.IsWindowVisible(windowHandle))
                    {
                        return windowHandle;
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process may exit while it is being inspected.
                }
            }
        }

        throw new InvalidOperationException(
            "No visible Roblox window was found. Open Roblox in windowed or borderless-windowed mode.");
    }

    public void Focus(nint windowHandle)
    {
        EnsureWindow(windowHandle);
        if (NativeMethods.IsIconic(windowHandle))
        {
            _ = NativeMethods.ShowWindowAsync(windowHandle, NativeMethods.SwRestore);
        }

        if (!NativeMethods.SetForegroundWindow(windowHandle) &&
            NativeMethods.GetForegroundWindow() != windowHandle)
        {
            throw new Win32Exception(
                "Windows did not allow the Roblox window to receive focus. Click Roblox and try again.");
        }
    }

    public PixelPoint ToScreenPoint(nint windowHandle, NormalizedPoint normalizedPoint)
    {
        if (!normalizedPoint.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(normalizedPoint),
                "The calibrated point must be within the normalized client area.");
        }

        var bounds = GetClientBounds(windowHandle);
        return new PixelPoint(
            bounds.Left + (int)Math.Round(normalizedPoint.X * bounds.Width),
            bounds.Top + (int)Math.Round(normalizedPoint.Y * bounds.Height));
    }

    public NormalizedPoint CaptureCursorPosition(nint windowHandle)
    {
        var bounds = GetClientBounds(windowHandle);
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            throw new Win32Exception();
        }

        if (cursor.X < bounds.Left ||
            cursor.X >= bounds.Left + bounds.Width ||
            cursor.Y < bounds.Top ||
            cursor.Y >= bounds.Top + bounds.Height)
        {
            throw new InvalidOperationException(
                "The mouse cursor was outside the Roblox client area when calibration was captured.");
        }

        return new NormalizedPoint(
            (cursor.X - bounds.Left) / (double)bounds.Width,
            (cursor.Y - bounds.Top) / (double)bounds.Height);
    }

    private static ClientBounds GetClientBounds(nint windowHandle)
    {
        EnsureWindow(windowHandle);
        if (!NativeMethods.GetClientRect(windowHandle, out var rectangle))
        {
            throw new Win32Exception();
        }

        var origin = new NativeMethods.NativePoint();
        if (!NativeMethods.ClientToScreen(windowHandle, ref origin))
        {
            throw new Win32Exception();
        }

        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Bottom - rectangle.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("The Roblox client area has no usable size.");
        }

        return new ClientBounds(origin.X, origin.Y, width, height);
    }

    private static void EnsureWindow(nint windowHandle)
    {
        if (windowHandle == nint.Zero || !NativeMethods.IsWindow(windowHandle))
        {
            throw new InvalidOperationException("The tracked Roblox window is no longer available.");
        }
    }

    private readonly record struct ClientBounds(int Left, int Top, int Width, int Height);
}
