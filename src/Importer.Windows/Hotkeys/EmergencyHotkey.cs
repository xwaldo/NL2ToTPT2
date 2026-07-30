using System.ComponentModel;
using Importer.Windows.Native;

namespace Importer.Windows.Hotkeys;

public sealed class EmergencyHotkey : IDisposable
{
    private const int Identifier = 0x4E4C;
    private const uint ModifierControl = 0x0002;
    private const uint ModifierShift = 0x0004;
    private const uint ModifierNoRepeat = 0x4000;
    private const uint VirtualKeyX = 0x58;

    private readonly nint _windowHandle;
    private bool _disposed;

    private EmergencyHotkey(nint windowHandle)
    {
        _windowHandle = windowHandle;
        if (!NativeMethods.RegisterHotKey(
                windowHandle,
                Identifier,
                ModifierControl | ModifierShift | ModifierNoRepeat,
                VirtualKeyX))
        {
            throw new Win32Exception(
                "Could not register Ctrl+Shift+X as the emergency stop. Another application may already use it.");
        }
    }

    public const string DisplayName = "Ctrl+Shift+X";

    public static EmergencyHotkey Register(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        return new EmergencyHotkey(windowHandle);
    }

    public static bool IsEmergencyMessage(int message, nint wordParameter) =>
        message == NativeMethods.WmHotkey &&
        wordParameter == Identifier;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _ = NativeMethods.UnregisterHotKey(_windowHandle, Identifier);
        _disposed = true;
    }
}
