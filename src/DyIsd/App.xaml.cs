using System;
using System.Diagnostics;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DyIsd.Island;
using DyIsd.Native;
using DyIsd.Services;
using DyIsd.Settings;

namespace DyIsd;

/// <summary>Starts every service and connects each one to the island.</summary>
public partial class App : Application
{
    Mutex? _single;
    IslandWindow? _window;
    MessageWindow? _messages;
    TrayIcon? _tray;
    ControlCenter? _cc;
    string _lastCalendarUrl = "";

    public IslandController? Island { get; private set; }
    public MediaService Media { get; private set; } = null!;
    public VolumeService Volume { get; private set; } = null!;
    public BrightnessService Brightness { get; private set; } = null!;
    public BatteryService Battery { get; private set; } = null!;
    public ClipboardService Clip { get; private set; } = null!;
    public CalendarService Calendar { get; private set; } = null!;
    public DownloadService Downloads { get; private set; } = null!;
    public FocusTimer Timer { get; private set; } = null!;
    public FullscreenWatcher Fullscreen { get; private set; } = null!;
    public ForegroundWatcher Foreground { get; private set; } = null!;
    public PrivacyService Privacy { get; private set; } = null!;
    public EarbudsService Earbuds { get; private set; } = null!;
    public MicService Mic { get; private set; } = null!;
    public CallService Calls { get; private set; } = null!;
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

        _window = new IslandWindow();
        _window.Show();

        Media = new MediaService();
        Volume = new VolumeService();
        Brightness = new BrightnessService();
        Battery = new BatteryService();
        Clip = new ClipboardService();
        Calendar = new CalendarService();
        Downloads = new DownloadService();
        Timer = new FocusTimer();
        Fullscreen = new FullscreenWatcher();
        Foreground = new ForegroundWatcher();
        Privacy = new PrivacyService();
        Earbuds = new EarbudsService();
        Mic = new MicService();
        Calls = new CallService(Privacy, Mic);
        KeyHook = new VolumeKeyHook();
        _messages = new MessageWindow();

        Island = new IslandController(_window, Media.State, Timer, Downloads.State, Calls.State, Foreground);
        Wire();

        Foreground.Start();
        Volume.Start();
        Media.State.Volume = Volume.Muted ? 0 : Volume.Level;
        Brightness.Start();
        Battery.Start();
        Clip.Attach(_messages);
        Downloads.Start();
        Fullscreen.Start();
        Mic.Start();
        Privacy.Start();
        Earbuds.Start();
        _lastCalendarUrl = S.CalendarUrl;
        Calendar.Start();
        ApplyKeyHook();
        RegisterHotkeys();
        CreateTray();
        SettingsStore.Changed += OnSettingsChanged;

        await Media.StartAsync();

        if (!S.FirstRunDone)
        {
            S.FirstRunDone = true;
            SettingsStore.Save();
            Island.ShowMessage("", "Accent", "DyIsd is running · hold Alt over the island to use it", 400, 6000);
            OpenControlCenter();
        }
    }

    void Wire()
    {
        var c = Island!;
        Media.ActiveChanged += c.Render;
        Downloads.ActiveChanged += c.Render;
        Foreground.Changed += c.Render;
        Timer.ActiveChanged += () =>
        {
            c.Render();
            _tray?.SetTimer(Timer.IsActive, S.FocusMinutes);
        };
        Timer.Finished += () =>
        {
            SystemSounds.Asterisk.Play();
            c.ShowMessage("", "Good", "Focus done · take 5", 230, 6000, fullscreenOk: true);
        };

        // Camera dot (green, like iPhone). The mic has no dot; it's used to spot calls.
        Privacy.Changed += () =>
        {
            var before = c.Privacy.App;
            c.Privacy = Privacy.CamApp != null ? (ThemeService.Brush("Good"), Privacy.CamApp) : (null, null);
            if (S.Features.Privacy && c.Privacy.App != null && c.Privacy.App != before) c.ShowPrivacy(true, c.Privacy.App);
            else c.Render();
        };

        // Calls: green pill with a timer while you're in another window.
        Calls.ActiveChanged += () =>
        {
            c.CallProcesses = Calls.App?.Processes ?? Array.Empty<string>();
            c.Render();
        };

        // Your Discord mute shortcut: when you press it, the island shows muted too.
        KeyHook.KeyDown = vk =>
        {
            if (S.DiscordMuteKey == 0 || vk != S.DiscordMuteKey) return;
            int mods = Win32.CurrentModifiers();
            if (mods == S.DiscordMuteModifiers) Dispatcher.BeginInvoke(Calls.DiscordShortcutPressed);
        };

        Earbuds.Changed += (name, connected, battery) => { if (S.Features.Earbuds) c.ShowEarbuds(name, connected, battery); };
        // Headphone buttons and other apps change the volume through Windows, which shows its
        // own pop-up that can't be turned off. Only show the island for changes DyIsd made,
        // so there's never a double pop-up. (When DyIsd isn't replacing Windows' pop-up, show all.)
        Volume.Changed += (level, muted, byUs) =>
        {
            Media.State.Volume = muted ? 0 : level;
            if (!S.Features.Volume) return;
            if (byUs || !S.HideWindowsVolumePopup) c.ShowVolume(level, muted);
        };
        Brightness.Changed += level => { if (S.Features.Brightness) c.ShowBrightness(level); };
        Battery.Event += (ev, pct, left) => { if (S.Features.Battery) c.ShowBattery(ev, pct, left); };
        Clip.Copied += info => { if (S.Features.Clipboard) c.ShowClipboard(info); };
        Calendar.Alert += (_, headline, detail) => { if (S.Features.Deadlines) c.ShowDeadline(headline, detail); };
        Downloads.Completed += path => { if (S.Features.Downloads) c.ShowDownloadDone(path); };
        Fullscreen.Changed += () => c.SetFullscreen(Fullscreen.IsFullscreen);

        c.ActionRequested += OnIslandAction;
        c.JumpRequested += names => AppJumper.Focus(names);
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
            case "open-file": if (Island!.LastDownloadPath != null) Open(Island.LastDownloadPath); break;
            case "show-file":
                if (Island!.LastDownloadPath != null)
                    TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{Island.LastDownloadPath}\""));
                break;
            case "batt-settings": Open("ms-settings:batterysaver"); break;
            case "shuffle": _ = Media.ToggleShuffleAsync(); break;
            case "repeat": _ = Media.CycleRepeatAsync(); break;
            case "mute": Calls.ToggleMute(S.DiscordMuteKey, S.DiscordMuteModifiers); break;
            default:
                if (tag.StartsWith("seek:") && double.TryParse(tag[5..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seek))
                    _ = Media.SeekAsync(seek);
                else if (tag.StartsWith("volume:") && double.TryParse(tag[7..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vol))
                    Volume.SetLevel(vol);
                break;
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

    public bool IslandPaused
    {
        get => Island?.Paused ?? false;
        set
        {
            if (Island != null) Island.Paused = value;
            _tray?.SetPaused(value);
        }
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
        // Needed for the volume keys and for noticing your Discord mute shortcut.
        bool needed = (S.HideWindowsVolumePopup && S.Features.Volume) || S.DiscordMuteKey != 0;
        if (needed) KeyHook.Install();
        else KeyHook.Uninstall();
    }

    void CreateTray()
    {
        _tray = new TrayIcon();
        _tray.SetTimer(false, S.FocusMinutes);
        _tray.OpenControlCenter += ToggleControlCenter;
        _tray.ToggleTimer += ToggleFocus;
        _tray.WhatsNext += ShowWhatsNext;
        _tray.PauseChanged += paused => IslandPaused = paused;
        _tray.Quit += Quit;
    }

    public void ShowWhatsNext()
    {
        var next = Calendar.Next();
        if (next == null) Island!.ShowMessage("", "Accent", "Nothing coming up this week", 280);
        else Island!.ShowDeadline(next.Value.Title, next.Value.Detail);
    }

    void ToggleControlCenter()
    {
        if (_cc != null) { _cc.Close(); return; }
        // Clicking the tray icon while the panel is open closes it first; don't instantly reopen.
        if (_lastCcClose > DateTime.Now.AddMilliseconds(-400)) return;
        OpenControlCenter();
    }

    DateTime _lastCcClose;

    public void OpenControlCenter()
    {
        if (_cc != null) { _cc.Activate(); return; }
        _cc = new ControlCenter(this);
        _cc.Closed += (_, _) => { _lastCcClose = DateTime.Now; _cc = null; };
        _cc.Open(_window!.IslandArea, S.Position);
    }

    void OnSettingsChanged()
    {
        _window!.ApplyPosition(S.Position);
        ApplyKeyHook();
        if (!S.Features.FocusTimer && Timer.IsActive) Timer.End();
        _tray?.SetTimer(Timer.IsActive, S.FocusMinutes);
        if (S.CalendarUrl != _lastCalendarUrl)
        {
            _lastCalendarUrl = S.CalendarUrl;
            _ = Calendar.RefreshAsync();
        }
        Island!.Render();
    }

    public void Quit()
    {
        _cc?.Close();
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
            Foreground?.Dispose();
            Mic?.Dispose();
            if (_messages != null) Win32.UnregisterHotKey(_messages.Handle, HotkeyFocus);
        }
        catch { }
        base.OnExit(e);
    }
}
