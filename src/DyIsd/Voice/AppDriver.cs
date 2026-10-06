using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using DyIsd.Services;

namespace DyIsd.Voice;

/// <summary>
/// Drives another app's window the way you would by hand: find its search box, type, find a
/// button, press it. Built on UI Automation (Services/Uia.cs). Every call here can take a while,
/// so use it from a background thread. Used for WhatsApp, Discord, Claude and Apple Music.
/// </summary>
public static class AppDriver
{
    /// <summary>The app's main window, waiting up to maxMs for it to appear.</summary>
    public static AutomationElement? WaitWindow(string[] processes, int maxMs = 8000, Func<AutomationElement, bool>? pick = null)
    {
        for (int waited = 0; ; waited += 250)
        {
            var wins = Uia.WindowsOf(processes);
            var w = pick == null ? wins.OrderByDescending(Area).FirstOrDefault() : wins.FirstOrDefault(pick);
            if (w != null || waited >= maxMs) return w;
            Thread.Sleep(250);
        }
    }

    static double Area(AutomationElement w)
    {
        try { var r = w.Current.BoundingRectangle; return r.IsEmpty ? 0 : r.Width * r.Height; }
        catch { return 0; }
    }

    public static void Focus(AutomationElement window)
    {
        try { CommandRunner.FocusWindow(new IntPtr(window.Current.NativeWindowHandle)); } catch { }
        Thread.Sleep(300);
    }

    /// <summary>First element of these kinds whose name matches, waiting up to maxMs for it.</summary>
    public static Uia.Node? WaitFor(AutomationElement root, Regex name, int maxMs, params ControlType[] types)
    {
        for (int waited = 0; ; waited += 300)
        {
            var hit = Uia.Find(root, types).FirstOrDefault(n => name.IsMatch(n.Name));
            if (hit != null || waited >= maxMs) return hit;
            Thread.Sleep(300);
        }
    }

    public static List<Uia.Node> All(AutomationElement root, Regex name, params ControlType[] types) =>
        Uia.Find(root, types).Where(n => name.IsMatch(n.Name)).ToList();

    /// <summary>Names of everything of these kinds, for the log ("whatsapp sees: …").</summary>
    public static string Describe(AutomationElement root, int max, params ControlType[] types) =>
        string.Join(" | ", Uia.Find(root, types).Select(n => n.Name).Where(n => n.Length > 0).Distinct().Take(max));

    /// <summary>Presses / selects / clicks an element, whichever it supports.</summary>
    public static bool Activate(AutomationElement e, bool doubleClick = false)
    {
        try
        {
            if (!doubleClick && e.TryGetCurrentPattern(InvokePattern.Pattern, out var inv)) { ((InvokePattern)inv).Invoke(); return true; }
            if (!doubleClick && e.TryGetCurrentPattern(TogglePattern.Pattern, out var tog)) { ((TogglePattern)tog).Toggle(); return true; }
            if (!doubleClick && e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sel)) { ((SelectionItemPattern)sel).Select(); return true; }
            return Click(e, doubleClick);
        }
        catch (Exception ex)
        {
            Log.Error("app driver", ex);
            return Click(e, doubleClick);
        }
    }

    public static bool Click(AutomationElement e, bool twice = false)
    {
        try
        {
            if (e.TryGetClickablePoint(out var pt)) { InputSim.Click((int)pt.X, (int)pt.Y, twice); return true; }
            var r = e.Current.BoundingRectangle;
            if (r.IsEmpty) return false;
            InputSim.Click((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2), twice);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Puts the cursor in a text box and types, replacing what was there.</summary>
    public static bool TypeInto(AutomationElement box, string text)
    {
        try
        {
            try { box.SetFocus(); } catch { Click(box); }
            Thread.Sleep(150);
            InputSim.Combo(CommandParser.Ctrl, 'A');
            InputSim.Type(text);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("app driver type", ex);
            return false;
        }
    }

    /// <summary>On / off state of a toggle button (Discord's Mute, Deafen), or null if it has none.</summary>
    public static bool? IsOn(AutomationElement e)
    {
        try
        {
            if (e.TryGetCurrentPattern(TogglePattern.Pattern, out var t))
                return ((TogglePattern)t).Current.ToggleState == ToggleState.On;
        }
        catch { }
        return null;
    }

    /// <summary>How well a name matches what you said, 0..1 (word by word, forgiving spelling).</summary>
    public static double NameScore(string spoken, string name)
    {
        var said = AppCatalog.Normalize(spoken);
        var n = AppCatalog.Normalize(name);
        if (said.Length == 0 || n.Length == 0) return 0;
        if (n == said) return 1;
        if (n.StartsWith(said + " ") || n.Split(' ').Contains(said)) return 0.9;
        return Fuzzy.Score(said, n);
    }
}
