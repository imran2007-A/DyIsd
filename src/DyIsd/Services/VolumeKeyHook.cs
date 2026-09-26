using System;
using System.Runtime.InteropServices;
using DyIsd.Native;

namespace DyIsd.Services;

/// <summary>
/// Catches the volume keys before Windows sees them. Because Windows never receives the key,
/// its own volume pop-up never appears, and the island shows instead.
/// </summary>
public sealed class VolumeKeyHook : IDisposable
{
    /// <summary>Gets the virtual key code and whether it's a key press. Return true to swallow the key.</summary>
    public Func<int, bool, bool>? Handler { get; set; }

    IntPtr _hook;
    Win32.LowLevelKeyboardProc? _proc; // kept in a field so the garbage collector doesn't free it

    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = Callback;
        _hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _proc, Win32.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) Log.Write("keyboard hook failed: " + Marshal.GetLastWin32Error());
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        Win32.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Handler != null)
        {
            var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            int vk = (int)info.vkCode;
            if (vk is Win32.VK_VOLUME_MUTE or Win32.VK_VOLUME_DOWN or Win32.VK_VOLUME_UP)
            {
                bool down = wParam == (IntPtr)Win32.WM_KEYDOWN || wParam == (IntPtr)Win32.WM_SYSKEYDOWN;
                if (Handler(vk, down)) return (IntPtr)1;
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
