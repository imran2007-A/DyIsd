using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DyIsd.Services;

/// <summary>
/// Which apps are using your microphone or camera right now. Windows records this in the
/// registry (it's what powers the mic icon in the taskbar). The camera gets a green dot on the
/// island; the microphone list is how DyIsd knows you're on a call.
/// </summary>
public sealed class PrivacyService
{
    public event Action? Changed;
    public string? CamApp { get; private set; }

    /// <summary>Apps holding the mic: lowercase registry key (package name or exe path) and a display name.</summary>
    public IReadOnlyList<(string Key, string Name)> MicApps { get; private set; } = Array.Empty<(string, string)>();

    const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public void Start()
    {
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        Poll();
    }

    void Poll()
    {
        var mic = FindUsers("microphone");
        var cam = FindUsers("webcam").Select(u => u.Name).FirstOrDefault();
        if (cam == CamApp && mic.Select(m => m.Key).SequenceEqual(MicApps.Select(m => m.Key))) return;
        CamApp = cam;
        MicApps = mic;
        Changed?.Invoke();
    }

    static List<(string Key, string Name)> FindUsers(string capability)
    {
        var list = new List<(string, string)>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Root + capability);
            if (key == null) return list;

            // Store apps (WhatsApp, Teams...)
            foreach (var name in key.GetSubKeyNames())
            {
                if (name == "NonPackaged") continue;
                using var app = key.OpenSubKey(name);
                if (InUse(app)) list.Add((name.ToLowerInvariant(), PackagedName(name)));
            }
            // Regular desktop apps (Chrome, Discord, Zoom...)
            using var np = key.OpenSubKey("NonPackaged");
            if (np != null)
            {
                foreach (var name in np.GetSubKeyNames())
                {
                    using var app = np.OpenSubKey(name);
                    if (InUse(app)) list.Add((name.ToLowerInvariant(), DesktopName(name)));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("privacy", ex);
        }
        return list;
    }

    // In use = started at some point and not stopped yet.
    static bool InUse(RegistryKey? app) =>
        app != null &&
        app.GetValue("LastUsedTimeStart") is long start && start > 0 &&
        app.GetValue("LastUsedTimeStop") is long stop && stop == 0;

    static string PackagedName(string key)
    {
        var n = key.Split('_')[0];
        int dot = n.LastIndexOf('.');
        return dot >= 0 ? n[(dot + 1)..] : n;
    }

    static string DesktopName(string key)
    {
        var path = key.Replace('#', '\\');
        try
        {
            if (File.Exists(path))
            {
                var desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) return desc.Trim();
            }
        }
        catch { }
        return Path.GetFileNameWithoutExtension(path);
    }
}
