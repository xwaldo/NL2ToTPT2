using System.ComponentModel;
using System.Runtime.InteropServices;
using Importer.Windows.Native;

namespace Importer.Windows.Hotkeys;

public sealed class EmergencyHotkey : IDisposable
{
    private const int IdentifierBase = 0x4E00;
    private const uint ModifierControl = 0x0002;
    private const uint ModifierShift = 0x0004;
    private const uint ModifierNoRepeat = 0x4000;

    private readonly nint _windowHandle;
    private readonly int _identifier;
    private bool _disposed;

    private EmergencyHotkey(nint windowHandle, EmergencyHotkeyKey key)
    {
        _windowHandle = windowHandle;
        Key = key;
        _identifier = IdentifierBase + (int)key;
        if (!NativeMethods.RegisterHotKey(
                windowHandle,
                _identifier,
                ModifierControl | ModifierShift | ModifierNoRepeat,
                (uint)key))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public EmergencyHotkeyKey Key { get; }

    public string DisplayName => $"Ctrl+Shift+{Key}";

    public static EmergencyHotkey Register(nint windowHandle, EmergencyHotkeyKey key)
    {
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        if (!Enum.IsDefined(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key));
        }

        return new EmergencyHotkey(windowHandle, key);
    }

    public bool IsEmergencyMessage(int message, nint wordParameter) =>
        !_disposed && message == NativeMethods.WmHotkey && wordParameter == _identifier;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _ = NativeMethods.UnregisterHotKey(_windowHandle, _identifier);
        _disposed = true;
    }
}
