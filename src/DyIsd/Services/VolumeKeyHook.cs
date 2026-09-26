using System;
using System.Runtime.InteropServices;
using DyIsd.Native;

namespace DyIsd.Services;

/// <summary>
/// Watches the keyboard for two things:
///  - Volume keys: swallowed before Windows sees them, so its own volume pop-up never appears.
///  - Your Discord mute shortcut: noticed (not swallowed) so the island can show you're muted.
/// </summary>
public sealed class VolumeKeyHook : IDisposable
{
    /// <summary>Volume key code and whether it's a press. Return true to swallow the key.</summary>
    public Func<int, bool, bool>? Handler { get; set; }

    /// <summary>Any other key pressed down (virtual key code). Must return fast.</summary>
    public Action<int>? KeyDown { get; set; }

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
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            int vk = (int)info.vkCode;
            bool down = wParam == (IntPtr)Win32.WM_KEYDOWN || wParam == (IntPtr)Win32.WM_SYSKEYDOWN;
            bool injected = (info.flags & 0x10) != 0; // pressed by a program (including us), not by you
            if (vk is Win32.VK_VOLUME_MUTE or Win32.VK_VOLUME_DOWN or Win32.VK_VOLUME_UP)
            {
                if (Handler != null && Handler(vk, down)) return (IntPtr)1;
            }
            else if (down && !injected)
            {
                KeyDown?.Invoke(vk);
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
