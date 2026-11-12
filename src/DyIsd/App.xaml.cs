using System;
using System.Linq;
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
using DyIsd.Voice;

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
    public ReminderStore Reminders { get; private set; } = null!;
    public DownloadService Downloads { get; private set; } = null!;
    public FocusTimer Timer { get; private set; } = null!;
    public FullscreenWatcher Fullscreen { get; private set; } = null!;
    public ForegroundWatcher Foreground { get; private set; } = null!;
    public PrivacyService Privacy { get; private set; } = null!;
    public EarbudsService Earbuds { get; private set; } = null!;
    public MicService Mic { get; private set; } = null!;
    public CallService Calls { get; private set; } = null!;
    public VolumeKeyHook KeyHook { get; private set; } = null!;
    public ClockService Clock { get; private set; } = null!;
    public RingService Ring { get; private set; } = null!;
    public TransferService Transfers { get; private set; } = null!;
    public JarvisService Jarvis { get; private set; } = null!;
    public ClaudeCodeService Claude { get; private set; } = null!;
    bool _pttHeld;

    const int HotkeyFocus = 1, HotkeyHome = 2;
    static AppSettings S => SettingsStore.Current;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Self-check: "DyIsd.exe --hear clip.wav" runs a recording through Jarvis's ears and
        // the command rules, logs what each heard and what it would do, then quits.
        if (e.Args.Length == 2 && e.Args[0] == "--hear")
        {
            SettingsStore.Load();
            AppCatalog.Load();
            await JarvisService.HearFileAsync(e.Args[1]);
            Shutdown();
            return;
        }

        // "DyIsd.exe --demo VIEW out.png": a picture of one view with sample data (see Island/Demo.cs).
        if (e.Args.Length == 3 && e.Args[0] == "--demo")
        {
            SettingsStore.Load();
            ThemeService.Init();
            try { await DyIsd.Island.Demo.RunAsync(e.Args[1], e.Args[2]); }
            catch (Exception ex) { Log.Error("demo", ex); }
            Shutdown();
            return;
        }

        // Dev check of the WhatsApp flow Jarvis uses, with the "Send to …?" already answered yes:
        //   DyIsd.exe --whatsapp-test message NAME "text"   or   --whatsapp-test call NAME
        // A call is hung up again as soon as its window opens. Logs every step.
        if (e.Args.Length >= 3 && e.Args[0] == "--whatsapp-test")
        {
            SettingsStore.Load();
            AppCatalog.Load();
            try
            {
                bool call = e.Args[1] == "call";
                var ask = call ? await WhatsApp.CallAsync(e.Args[2], video: false) : await WhatsApp.MessageAsync(e.Args[2], e.Args.Length > 3 ? e.Args[3] : "DyIsd test, ignore");
                Log.Write($"whatsapp test: asked \"{ask.Text}\"");
                var done = ask.Confirm != null ? await ask.Confirm() : ask;
                Log.Write($"whatsapp test: result \"{done.Text}\" failed={done.Failed}");
                if (call && !done.Failed)
                {
                    await Task.Delay(3000);
                    Log.Write($"whatsapp test: hung up = {await Task.Run(WhatsApp.HangUp)}");
                }
            }
            catch (Exception ex) { Log.Error("whatsapp test", ex); }
            Shutdown();
            return;
        }

        // "DyIsd.exe --brain text": what the AI brain makes of a (misheard) sentence. Runs nothing.
        if (e.Args.Length == 1 && e.Args[0] == "--shot") { Log.Write("shot test: " + Voice.CommandRunner.WindowShot().Text); Shutdown(); return; }
        if (e.Args.Length == 2 && e.Args[0] == "--brain")
        {
            SettingsStore.Load();
            var cmds = CommandParser.Parse(e.Args[1], DateTime.Now);
            Log.Write($"rules: \"{e.Args[1]}\" → {string.Join(" | ", cmds.Select(c => c.Kind + ":" + c.Text + (c.Say.Length > 0 ? " say " + c.Say : "")))}");
            if (cmds.All(c => c.Kind != "unknown")) { Shutdown(); return; }
            try { await Brain.AskAsync(e.Args[1], "Installed apps: Discord, WhatsApp, Spotify, Chrome\nWhatsApp contacts: " + string.Join(", ", S.WhatsAppContacts.Select(c => c.Name))); }
            catch (Exception ex) { Log.Error("brain test", ex); }
            Shutdown();
            return;
        }

        // "DyIsd.exe --am-library play|shuffle": plays your Apple Music songs the way Jarvis does.
        if (e.Args.Length == 2 && e.Args[0] == "--am-library")
        {
            SettingsStore.Load();
            AppCatalog.Load();
            Media = new MediaService();
            await Media.StartAsync();
            var r = await AppleMusic.PlayLibraryAsync(e.Args[1] == "shuffle");
            Log.Write($"am library test: {r.Text} failed={r.Failed}");
            Shutdown();
            return;
        }

        // "DyIsd.exe --discord-open general": opens that chat the way Jarvis would, sends nothing.
        if (e.Args.Length == 2 && e.Args[0] == "--discord-open")
        {
            SettingsStore.Load();
            var found = await Task.Run(() => DiscordControl.OpenChat(e.Args[1], S.DiscordServer));
            Log.Write("discord open test: " + (found ?? "not found"));
            Shutdown();
            return;
        }

        // "DyIsd.exe --discord-send general text": Jarvis's Discord message flow with "Send to …?" answered yes.
        if (e.Args.Length == 3 && e.Args[0] == "--discord-send")
        {
            SettingsStore.Load();
            var found = await Task.Run(() => DiscordControl.OpenChat(e.Args[1], S.DiscordServer));
            bool sent = found != null && await Task.Run(() => DiscordControl.SendInOpenChat(found, e.Args[2]));
            Log.Write($"discord send test: {found ?? "not found"} sent={sent}");
            Shutdown();
            return;
        }

        // A plain launch (Windows startup, Start menu) becomes the watchdog that keeps the real
        // DyIsd ("--run") alive and restarts it if it crashes. One watchdog at a time.
        if (e.Args.Length == 0)
        {
            _single = new Mutex(true, "DyIsd.Watchdog.v1", out bool firstGuard);
            if (firstGuard) await Services.Watchdog.RunAsync();
            Shutdown();
            return;
        }

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
        Clock = new ClockService();
        Ring = new RingService(Calls.State);
        Transfers = new TransferService();
        _messages = new MessageWindow();

        Claude = new ClaudeCodeService();
        Reminders = new ReminderStore();
        Island = new IslandController(_window, Media.State, Timer, Downloads.State, Calls.State, Clock.State, Ring.State, Transfers.State, Foreground,
            Claude.State, new HomeState(Claude.State, Media.State, Timer, Reminders));
        Jarvis = new JarvisService(this, Island);
        Wire();

        Foreground.MonitorChanged += () => _window.FollowMonitor(Foreground.Monitor);
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
        Clock.Start();
        Ring.Start();
        Transfers.Start();
        Claude.Start();
        _lastCalendarUrl = S.CalendarUrl;
        Calendar.Start();
        Reminders.Start();
        ApplyKeyHook();
        RegisterHotkeys();
        CreateTray();
        SettingsStore.Changed += OnSettingsChanged;

        await Media.StartAsync();
        Jarvis.Start();

        if (!S.FirstRunDone)
        {
            S.FirstRunDone = true;
            SettingsStore.Save();
            Island.ShowMessage("\uE720", "Accent", "DyIsd is running · hold Ctrl + Space to talk to Jarvis", 400, 6000);
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
            c.ShowMessage("\uE73E", "Good", Timer.Label == "Focus" ? "Focus done · take 5" : "Timer done", 230, 6000, fullscreenOk: true);
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
            // Right Ctrl alone as the shortcut: Ctrl being "held" is the key itself.
            foreach (int flag in new[] { Win32.MOD_KEY_CTRL, Win32.MOD_KEY_ALT, Win32.MOD_KEY_SHIFT, Win32.MOD_KEY_WIN })
                if (IsModifierOf(vk, flag)) mods &= ~flag;
            if (mods == S.DiscordMuteModifiers) Dispatcher.BeginInvoke(Calls.DiscordShortcutPressed);
        };

        Earbuds.Changed += (name, connected, battery) => { if (S.Features.Earbuds) c.ShowEarbuds(name, connected, battery); };

        // Windows Clock's timer / stopwatch, ringing calls, file transfers.
        Clock.ActiveChanged += c.Render;
        Clock.TimerDone += () =>
        {
            if (!S.Features.Clock) return;
            SystemSounds.Asterisk.Play();
            c.ShowMessage("\uE916", "Orange", "Clock timer done", 220, 6000, fullscreenOk: true);
        };
        Ring.ActiveChanged += () =>
        {
            c.RingProcesses = Ring.Processes;
            c.Render();
        };
        Transfers.ActiveChanged += c.Render;

        // Claude Code: Thinking → Editing → Done. "Done" and "Needs you" take the front.
        Claude.ActiveChanged += c.Render;
        Claude.State.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ClaudeState.IsDone) or nameof(ClaudeState.NeedsYou) or nameof(ClaudeState.HostProcesses)) c.Render();
        };
        Claude.Finished += () =>
        {
            if (!S.Features.ClaudeCode) return;
            if (S.JarvisSounds) Chime.Done();
            c.ClaudeFinished();
        };

        // Home panel: clipboard history, files dropped on the island, notifications when its tab is open.
        Clip.Copied += info => { if (S.Features.Clipboard) c.Home.AddClip(info); };
        _window!.FilesDropped += c.FilesDropped;
        c.HomeChanged += open =>
        {
            if (open && c.Home.Tab == "notes-in" && S.Features.Notifications) _ = RefreshNotificationsAsync();
        };
        Transfers.Done += (title, file) => { if (S.Features.Transfers) c.ShowTransferDone(title, file); };

        // Jarvis: hold your Jarvis key (Ctrl + Space unless changed) to talk. The key is swallowed so
        // nothing gets typed.
        KeyHook.Intercept = (vk, down) =>
        {
            if (!S.JarvisEnabled) return false;
            if (vk == S.JarvisKey)
            {
                if (down)
                {
                    if (_pttHeld) return true; // key repeat while held
                    if (Win32.CurrentModifiers() != S.JarvisMods) return false;
                    _pttHeld = true;
                    Dispatcher.BeginInvoke(Jarvis.PttDown);
                    return true;
                }
                if (!_pttHeld) return false;
                _pttHeld = false;
                Dispatcher.BeginInvoke(Jarvis.PttUp);
                return true;
            }
            // Let go of a modifier first: stop listening too.
            if (!down && _pttHeld && IsModifierOf(vk, S.JarvisMods))
            {
                _pttHeld = false;
                Dispatcher.BeginInvoke(Jarvis.PttUp);
            }
            return false;
        };
        // Headphone buttons and other apps change the volume through Windows, which shows its
        // own pop-up that can't be turned off. Only show the island for changes DyIsd made,
        // so there's never a double pop-up. (When DyIsd isn't replacing Windows' pop-up, show all.)
        bool wasMuted = Volume.Muted;
        Volume.Changed += (level, muted, byUs) =>
        {
            Media.State.Volume = muted ? 0 : level;
            bool flipped = muted != wasMuted;
            wasMuted = muted;
            if (!S.Features.Volume) return;
            if (!byUs && S.HideWindowsVolumePopup) return;
            // Mute on/off looks like the iPhone's silent switch; other changes show the level.
            c.Home.VolumeGlyph = VolumeService.GlyphFor(level, muted);
            if (flipped) c.ShowSilent(muted);
            else c.ShowVolume(level, muted);
        };
        Brightness.Changed += level => { if (S.Features.Brightness) c.ShowBrightness(level); };
        Battery.Event += (ev, pct, left) => { if (S.Features.Battery) c.ShowBattery(ev, pct, left); };
        Clip.Copied += info => { if (S.Features.Clipboard) c.ShowClipboard(info); };
        Calendar.Alert += (_, headline, detail) => { if (S.Features.Deadlines) c.ShowDeadline(headline, detail); };
        Reminders.Due += r => { c.ShowDeadline(r.Text, "Reminder · " + r.WhenText); Voice.Chime.Done(); };
        Downloads.Completed += path => { if (S.Features.Downloads) c.ShowDownloadDone(path); };
        Fullscreen.Changed += () => c.SetFullscreen(Fullscreen.IsFullscreen);

        c.ActionRequested += OnIslandAction;
        c.JumpRequested += names =>
        {
            if (AppJumper.Focus(names)) return;
            // Store apps like Clock don't always have a window to bring forward: open them instead.
            if (names == IslandController.ClockProcesses) Open("ms-clock:");
        };
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

    static bool IsModifierOf(int vk, int mods) =>
        ((mods & Win32.MOD_KEY_CTRL) != 0 && vk is 0x11 or 0xA2 or 0xA3) ||
        ((mods & Win32.MOD_KEY_ALT) != 0 && vk is 0x12 or 0xA4 or 0xA5) ||
        ((mods & Win32.MOD_KEY_SHIFT) != 0 && vk is 0x10 or 0xA0 or 0xA1) ||
        ((mods & Win32.MOD_KEY_WIN) != 0 && vk is 0x5B or 0x5C);

    void OnIslandAction(string tag)
    {
        // Home → Reminders: add (typed, maybe with a time), tick, delete.
        if (tag.StartsWith("rem-add:")) { var (text, when) = Voice.CommandParser.ReminderFrom(tag[8..]); Reminders.Add(text, when); return; }
        if (tag.StartsWith("rem-tick:")) { if (Reminders.ById(tag[9..]) is { } r) Reminders.SetDone(r, !r.Done); return; }
        if (tag.StartsWith("rem-del:")) { if (Reminders.ById(tag[8..]) is { } r) Reminders.Remove(r); return; }
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
            case "answer": Ring.Answer(); break;
            case "decline": Ring.Decline(); break;
            case "xfer-folder": Open(Native.Win32.DownloadsFolder()); break;
            case "jv-yes": Jarvis.Confirm(true); break;
            case "jv-no": Jarvis.Confirm(false); break;
            case "jv-download": Jarvis.StartDownload(); break;
            case "cc": Island!.CloseHome(); OpenControlCenter(); break;
            case "home-focus": if (!Timer.IsActive) Timer.Start(S.FocusMinutes); break;
            case "claude-connect":
                if (Claude.Connect()) Island!.ShowMessage("\uE73E", "Good", "Claude Code connected · starts with its next session", 380, 4500);
                else Island!.ShowMessage("\uE783", "Bad", "Couldn't edit Claude Code's settings · see log", 340, 4500);
                break;
            case "sysmute": Volume.ToggleMute(); break;
            default:
                if (tag.StartsWith("focus:") && int.TryParse(tag[6..], out var mins)) Timer.Start(mins);
                else if (tag.StartsWith("clip:"))
                {
                    try { Clipboard.SetText(tag[5..]); } catch (Exception ex) { Log.Error("clipboard copy", ex); }
                }
                else if (tag.StartsWith("tray-open:")) Open(tag[10..]);
                else if (tag.StartsWith("tray-remove:")) Island!.Home.RemoveFromTray(tag[12..]);
                else
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

    async System.Threading.Tasks.Task RefreshNotificationsAsync()
    {
        var (items, status) = await NotificationService.ReadAsync();
        Island!.Home.SetNotifications(items, status);
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

    // Not Ctrl + Alt: dictation apps like Glaido use Ctrl + Alt on its own as their talk key
    // and swallow every Ctrl + Alt shortcut.
    const uint MOD_SHIFT = 0x4, MOD_WIN = 0x8;

    void RegisterHotkeys()
    {
        // Win + Shift + F starts or ends a focus session from anywhere.
        if (!Win32.RegisterHotKey(_messages!.Handle, HotkeyFocus, MOD_WIN | MOD_SHIFT | Win32.MOD_NOREPEAT, 0x46))
            Log.Write("Win+Shift+F is already used by another app");
        // Win + Shift + H opens the Home panel (date, Claude Code, music, focus, tray, clipboard, notes).
        if (!Win32.RegisterHotKey(_messages.Handle, HotkeyHome, MOD_WIN | MOD_SHIFT | Win32.MOD_NOREPEAT, 0x48))
            Log.Write("Win+Shift+H is already used by another app");
        _messages.Message += (msg, w, _) =>
        {
            if (msg == Win32.WM_HOTKEY && w == (IntPtr)HotkeyFocus) ToggleFocus();
            if (msg == Win32.WM_HOTKEY && w == (IntPtr)HotkeyHome) Island!.ToggleHome();
        };
    }

    void ApplyKeyHook()
    {
        // Needed for the volume keys, noticing your Discord mute shortcut, and Ctrl + Space for Jarvis.
        bool needed = (S.HideWindowsVolumePopup && S.Features.Volume) || S.DiscordMuteKey != 0 || S.JarvisEnabled;
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
        if (next == null) Island!.ShowMessage("\uE787", "Accent", "Nothing coming up this week", 280);
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
        // Forget it as soon as it starts closing: touching a closing window throws.
        _cc.Closing += (_, _) => { _lastCcClose = DateTime.Now; _cc = null; };
        _cc.Open(_window!.Handle, S.Position);
    }

    void OnSettingsChanged()
    {
        _window!.ApplyPosition(S.Position);
        ApplyKeyHook();
        Jarvis.Apply();
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
            Jarvis?.Dispose();
            Transfers?.Dispose();
            Claude?.Dispose();
            if (_messages != null) { Win32.UnregisterHotKey(_messages.Handle, HotkeyFocus); Win32.UnregisterHotKey(_messages.Handle, HotkeyHome); }
        }
        catch { }
        base.OnExit(e);
    }
}
