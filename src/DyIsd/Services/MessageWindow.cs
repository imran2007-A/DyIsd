using System;
using System.Windows.Interop;

namespace DyIsd.Services;

/// <summary>
/// An invisible window that only receives Windows messages (clipboard changes, hotkeys).
/// </summary>
public sealed class MessageWindow : IDisposable
{
    readonly HwndSource _source;
    public IntPtr Handle => _source.Handle;
    public event Action<int, IntPtr, IntPtr>? Message;

    public MessageWindow()
    {
        var p = new HwndSourceParameters("DyIsdMessages")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE: message-only window
        };
        _source = new HwndSource(p);
        _source.AddHook(Hook);
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        Message?.Invoke(msg, wParam, lParam);
        return IntPtr.Zero;
    }

    public void Dispose() => _source.Dispose();
}
