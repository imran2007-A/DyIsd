using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace DyIsd.Voice;

/// <summary>
/// Presses keys, types text and scrolls, exactly as if you did it on the keyboard and mouse
/// (Windows SendInput). Goes to whatever window is in front.
/// </summary>
public static class InputSim
{
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion U; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    const uint KEYUP = 0x2, UNICODE = 0x4, EXTENDED = 0x1;
    const uint MOUSEEVENTF_WHEEL = 0x0800;

    // Keys that live on the extended part of the keyboard need a flag or they act like the numpad.
    static readonly HashSet<int> Extended = new()
    {
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5C, 0x6F, 0x90, 0xA3, 0xA5,
    };

    static INPUT Key(int vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = (up ? KEYUP : 0) | (Extended.Contains(vk) ? EXTENDED : 0) } },
    };

    static void Send(List<INPUT> inputs)
    {
        if (inputs.Count == 0) return;
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Waits until you've let go of Ctrl / Shift / Alt / Win / Space, so typed text isn't turned
    /// into shortcuts (Ctrl+Space starts Jarvis, and Ctrl may still be down).
    /// </summary>
    public static void WaitForKeysReleased(int maxMs = 1500)
    {
        int[] keys = { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x20 };
        int waited = 0;
        while (waited < maxMs)
        {
            bool any = false;
            foreach (var k in keys) if ((GetAsyncKeyState(k) & 0x8000) != 0) { any = true; break; }
            if (!any) return;
            Thread.Sleep(20);
            waited += 20;
        }
    }

    /// <summary>Presses a combination like Ctrl+Shift+T: modifiers down, key, modifiers up.</summary>
    public static void Combo(params int[] keys)
    {
        var list = new List<INPUT>();
        foreach (var k in keys) list.Add(Key(k, false));
        for (int i = keys.Length - 1; i >= 0; i--) list.Add(Key(keys[i], true));
        Send(list);
    }

    public static void Repeat(int times, params int[] keys)
    {
        for (int i = 0; i < Math.Clamp(times, 1, 50); i++)
        {
            Combo(keys);
            Thread.Sleep(25);
        }
    }

    /// <summary>Types text letter by letter (any language, emoji too). New lines press Enter.</summary>
    public static void Type(string text)
    {
        var list = new List<INPUT>();
        foreach (char c in text)
        {
            if (c == '\n') { list.Add(Key(0x0D, false)); list.Add(Key(0x0D, true)); continue; }
            if (c == '\r') continue;
            list.Add(new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = UNICODE } } });
            list.Add(new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = UNICODE | KEYUP } } });
        }
        Send(list);
    }

    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    const uint LEFTDOWN = 0x2, LEFTUP = 0x4;

    /// <summary>Clicks (or double-clicks) at a screen point, then puts the mouse back where it was.</summary>
    public static void Click(int x, int y, bool twice = false)
    {
        GetCursorPos(out var old);
        SetCursorPos(x, y);
        Thread.Sleep(30);
        var list = new List<INPUT>();
        for (int i = 0; i < (twice ? 2 : 1); i++)
        {
            list.Add(new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = LEFTDOWN } } });
            list.Add(new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = LEFTUP } } });
        }
        Send(list);
        Thread.Sleep(60);
        SetCursorPos(old.X, old.Y);
    }

    /// <summary>Scroll the mouse wheel. Positive = up. One notch = 1.</summary>
    public static void Scroll(int notches)
    {
        var list = new List<INPUT>();
        int step = Math.Sign(notches);
        for (int i = 0; i < Math.Abs(notches); i++)
            list.Add(new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { mouseData = (uint)(step * 120), dwFlags = MOUSEEVENTF_WHEEL } } });
        Send(list);
    }
}
