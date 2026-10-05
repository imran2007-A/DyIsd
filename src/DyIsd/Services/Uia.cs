using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Automation;

namespace DyIsd.Services;

/// <summary>
/// Reads other apps' windows through UI Automation, Windows' accessibility system (what
/// screen readers use). How DyIsd sees Clock's timers, a ringing call's Answer button and a
/// file transfer's progress bar, none of which those apps share any other way.
/// Call from a background thread: these calls can take tens of milliseconds.
/// </summary>
public static class Uia
{
    public sealed record Node(string Name, string Type, AutomationElement Element);

    /// <summary>Top-level windows that belong to any of these processes.</summary>
    public static List<AutomationElement> WindowsOf(params string[] processNames)
    {
        var pids = new HashSet<int>();
        foreach (var name in processNames)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(name)) { pids.Add(p.Id); p.Dispose(); }
            }
            catch { }
        }
        if (pids.Count == 0) return new List<AutomationElement>();
        return TopWindows(w => pids.Contains(w.Current.ProcessId));
    }

    public static bool Running(params string[] processNames)
    {
        foreach (var name in processNames)
        {
            try
            {
                var found = Process.GetProcessesByName(name);
                bool any = found.Length > 0;
                foreach (var p in found) p.Dispose();
                if (any) return true;
            }
            catch { }
        }
        return false;
    }

    public static List<AutomationElement> TopWindows(Func<AutomationElement, bool> keep)
    {
        var list = new List<AutomationElement>();
        try
        {
            foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
            {
                try { if (keep(w)) list.Add(w); } catch { }
            }
        }
        catch { }
        return list;
    }

    /// <summary>Every element of the given kinds inside a window, with names fetched in one go.</summary>
    public static List<Node> Find(AutomationElement window, params ControlType[] types)
    {
        var result = new List<Node>();
        try
        {
            var cache = new CacheRequest();
            cache.Add(AutomationElement.NameProperty);
            cache.Add(AutomationElement.ControlTypeProperty);
            Condition cond = types.Length == 1
                ? new PropertyCondition(AutomationElement.ControlTypeProperty, types[0])
                : new OrCondition(types.Select(t => (Condition)new PropertyCondition(AutomationElement.ControlTypeProperty, t)).ToArray());
            using (cache.Activate())
            {
                foreach (AutomationElement e in window.FindAll(TreeScope.Descendants, cond))
                {
                    try
                    {
                        result.Add(new Node((e.Cached.Name ?? "").Trim(), e.Cached.ControlType.ProgrammaticName, e));
                    }
                    catch { }
                }
            }
        }
        catch { }
        return result;
    }

    /// <summary>Presses a button the way a screen reader would. False if it can't be pressed.</summary>
    public static bool Press(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var p))
            {
                ((InvokePattern)p).Invoke();
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Error("uia press", ex);
        }
        return false;
    }

    /// <summary>Value 0..1 of a progress bar, or null.</summary>
    public static double? Progress(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(RangeValuePattern.Pattern, out var p))
            {
                var r = (RangeValuePattern)p;
                double span = r.Current.Maximum - r.Current.Minimum;
                if (span > 0) return Math.Clamp((r.Current.Value - r.Current.Minimum) / span, 0, 1);
            }
        }
        catch { }
        return null;
    }
}
