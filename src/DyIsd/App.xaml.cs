using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DyIsd.Island;
using DyIsd.Native;
using DyIsd.Services;
using DyIsd.Settings;

namespace DyIsd;

/// <summary>Starts every service and connects each one to the island.</summary>
public partial class App : Application
{
    Mutex? _single;
    IslandWindow? _island;
    IslandController? _ctl;
    MessageWindow? _messages;
    TrayIcon? _tray;
    SettingsWindow? _settingsWindow;
    string _lastCalendarUrl = "";

    public MediaService Media { get; private set; } = null!;
    public VolumeService Volume { get; private set; } = null!;
    public BrightnessService Brightness { get; private set; } = null!;
    public BatteryService Battery { get; private set; } = null!;
    public NotificationService Notifications { get; private set; } = null!;
    public ClipboardService Clip { get; private set; } = null!;
    public CalendarService Calendar { get; private set; } = null!;
    public DownloadService Downloads { get; private set; } = null!;
    public FocusTimer Timer { get; private set; } = null!;
    public FullscreenWatcher Fullscreen { get; private set; } = null!;
    public VolumeKeyHook KeyHook { get; private set; } = null!;

    const int HotkeyFocus = 1;
    static AppSettings S => SettingsStore.Current;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only one copy of DyIsd at a time.
        _single = new Mutex(true, "DyIsd.SingleInstance.v1", out bool first);
        if (!first)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, a) => { Log.Error("ui", a.Exception); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Write("fatal: " + a.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("task", a.Exception); a.SetObserved(); };
        Log.Write($"DyIsd starting (packaged: {Win32.IsPackaged})");

        SettingsStore.Load();
        ThemeService.Init();

        _island = new IslandWindow();
        _island.Show();

        Media = new MediaService();
        Volume = new VolumeService();
        Brightness = new BrightnessService();
        Battery = new BatteryService();
        Notifications = new NotificationService();
        Clip = new ClipboardService();
        Calendar = new CalendarService();
        Downloads = new DownloadService();
        Timer = new FocusTimer();
        Fullscreen = new FullscreenWatcher();
        KeyHook = new VolumeKeyHook();
        _messages = new MessageWindow();

        _ctl = new IslandController(_island, Media.State, Timer, Downloads.State);
        Wire();

        Volume.Start();
        Brightness.Start();
        Battery.Start();
        Clip.Attach(_messages);
        Downloads.Start();
        Fullscreen.Start();
        _lastCalendarUrl = S.CalendarUrl;
        Calendar.Start();
        ApplyKeyHook();
        RegisterHotkeys();
        CreateTray();
        SettingsStore.Changed += OnSettingsChanged;

        await Media.StartAsync();
        if (S.Features.Notifications) await Notifications.StartAsync(askPermission: !S.FirstRunDone);

        if (!S.FirstRunDone)
        {
            S.FirstRunDone = true;
            SettingsStore.Save();
            _ctl.ShowMessage("", "Accent", "DyIsd is running · Ctrl+Alt+F starts a focus session", 440, 5000);
            OpenSettings();
        }
    }

    void Wire()
    {
        var c = _ctl!;
        Media.ActiveChanged += c.Render;
        Downloads.ActiveChanged += c.Render;
        Timer.ActiveChanged += () =>
        {
            c.Render();
            _tray?.SetTimer(Timer.IsActive, S.FocusMinutes);
        };
        Timer.Finished += () =>
        {
            SystemSounds.Asterisk.Play();
            c.ShowMessage("", "Good", "Focus session done · take 5", 290, 6000, fullscreenOk: true);
        };

        Volume.Changed += (level, muted) => { if (S.Features.Volume) c.ShowVolume(level, muted); };
        Brightness.Changed += level => { if (S.Features.Brightness) c.ShowBrightness(level); };
        Battery.Event += (ev, pct, left) => { if (S.Features.Battery) c.ShowBattery(ev, pct, left); };
        Notifications.Arrived += n => { if (S.Features.Notifications) c.ShowNotification(n); };
        Clip.Copied += info => { if (S.Features.Clipboard) c.ShowClipboard(info); };
        Calendar.Alert += (_, headline, detail) => { if (S.Features.Deadlines) c.ShowDeadline(headline, detail); };
        Downloads.Completed += path => { if (S.Features.Downloads) c.ShowDownloadDone(path); };
        Fullscreen.Changed += () => c.SetFullscreen(Fullscreen.IsFullscreen);

        c.ActionRequested += OnIslandAction;
        c.WheelRequested += (dir, shift) =>
        {
            if (shift) { if (S.Features.Brightness) Brightness.Step(dir * 5); }
            else if (S.Features.Volume) Volume.Step(dir * 2);
        };
        c.PositionDropped += zone =>
        {
            S.Position = zone;
            SettingsStore.Save();
        };

        // Volume keys: swallow them so the Windows pop-up never shows, then change volume ourselves.
        KeyHook.Handler = (vk, down) =>
        {
            if (!S.HideWindowsVolumePopup || !S.Features.Volume || !Volume.IsReady) return false;
            if (down)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (vk == Win32.VK_VOLUME_UP) Volume.Step(2);
                    else if (vk == Win32.VK_VOLUME_DOWN) Volume.Step(-2);
                    else Volume.ToggleMute();
                });
            }
            return true;
        };
    }

    void OnIslandAction(string tag)
    {
        switch (tag)
        {
            case "prev": _ = Media.PreviousAsync(); break;
            case "toggle": _ = Media.TogglePlayPauseAsync(); break;
            case "next": _ = Media.NextAsync(); break;
            case "tpause": Timer.TogglePause(); break;
            case "t5": Timer.AddFive(); break;
            case "tend": Timer.End(); break;
            case "dfolder": Open(Downloads.Folder); break;
            case "open-file": if (_ctl!.LastDownloadPath != null) Open(_ctl.LastDownloadPath); break;
            case "show-file":
                if (_ctl!.LastDownloadPath != null)
                    TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{_ctl.LastDownloadPath}\""));
                break;
            case "batt-settings": Open("ms-settings:batterysaver"); break;
        }
    }

    static void Open(string target) => TryStart(new ProcessStartInfo(target) { UseShellExecute = true });

    static void TryStart(ProcessStartInfo psi)
    {
        try { Process.Start(psi); }
        catch (Exception ex) { Log.Error("open", ex); }
    }

    public void ToggleFocus()
    {
        if (!S.Features.FocusTimer) return;
        if (Timer.IsActive) Timer.End();
        else Timer.Start(S.FocusMinutes);
    }

    void RegisterHotkeys()
    {
        // Ctrl + Alt + F starts or ends a focus session from anywhere.
        if (!Win32.RegisterHotKey(_messages!.Handle, HotkeyFocus, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, 0x46))
            Log.Write("Ctrl+Alt+F is already used by another app");
        _messages.Message += (msg, w, _) =>
        {
            if (msg == Win32.WM_HOTKEY && w == (IntPtr)HotkeyFocus) ToggleFocus();
        };
    }

    void ApplyKeyHook()
    {
        if (S.HideWindowsVolumePopup && S.Features.Volume) KeyHook.Install();
        else KeyHook.Uninstall();
    }

    void CreateTray()
    {
        _tray = new TrayIcon();
        _tray.SetTimer(false, S.FocusMinutes);
        _tray.OpenSettings += OpenSettings;
        _tray.ToggleTimer += ToggleFocus;
        _tray.WhatsNext += ShowWhatsNext;
        _tray.PauseChanged += paused => _ctl!.Paused = paused;
        _tray.Quit += Quit;
    }

    public void ShowWhatsNext()
    {
        var next = Calendar.Next();
        if (next == null) _ctl!.ShowMessage("", "Accent", "Nothing coming up in the next 7 days", 340);
        else _ctl!.ShowDeadline(next.Value.Title, next.Value.Detail);
    }

    public void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    void OnSettingsChanged()
    {
        ThemeService.Apply();
        _island!.ApplyPosition(S.Position);
        ApplyKeyHook();
        if (!S.Features.FocusTimer && Timer.IsActive) Timer.End();
        if (!S.Features.Notifications) Notifications.Stop();
        _tray?.SetTimer(Timer.IsActive, S.FocusMinutes);
        if (S.CalendarUrl != _lastCalendarUrl)
        {
            _lastCalendarUrl = S.CalendarUrl;
            _ = Calendar.RefreshAsync();
        }
        _ctl!.Render();
    }

    void Quit()
    {
        _tray?.Dispose();
        KeyHook.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _tray?.Dispose();
            KeyHook?.Dispose();
            Volume?.Dispose();
            Brightness?.Dispose();
            Downloads?.Dispose();
            if (_messages != null) Win32.UnregisterHotKey(_messages.Handle, HotkeyFocus);
        }
        catch { }
        base.OnExit(e);
    }

    public static string LogFolder => Log.Dir;
    public static bool LogFolderExists => Directory.Exists(Log.Dir);
}
