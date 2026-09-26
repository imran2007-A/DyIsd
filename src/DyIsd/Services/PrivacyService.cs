using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DyIsd.Services;

/// <summary>
/// Shows which app is using your microphone or camera, like the iPhone's orange and green dots.
/// Windows records this in the registry (it's what powers the mic icon in the taskbar).
/// </summary>
public sealed class PrivacyService
{
    /// <summary>App using the mic (null when none), app using the camera (null when none).</summary>
    public event Action? Changed;
    public string? MicApp { get; private set; }
    public string? CamApp { get; private set; }

    const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };

    public void Start()
    {
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    void Poll()
    {
        var mic = FindUser("microphone");
        var cam = FindUser("webcam");
        if (mic == MicApp && cam == CamApp) return;
        MicApp = mic;
        CamApp = cam;
        Changed?.Invoke();
    }

    static string? FindUser(string capability)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Root + capability);
            if (key == null) return null;

            // Store apps
            foreach (var name in key.GetSubKeyNames())
            {
                if (name == "NonPackaged") continue;
                using var app = key.OpenSubKey(name);
                if (InUse(app)) return PackagedName(name);
            }
            // Regular desktop apps (Chrome, Discord, Zoom...)
            using var np = key.OpenSubKey("NonPackaged");
            if (np != null)
            {
                foreach (var name in np.GetSubKeyNames())
                {
                    using var app = np.OpenSubKey(name);
                    if (InUse(app)) return DesktopName(name);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("privacy", ex);
        }
        return null;
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
