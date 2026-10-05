using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DyIsd.Native;
using DyIsd.Services;
using DyIsd.Settings;
using Microsoft.Win32;
using Windows.Devices.Radios;
using WF = System.Windows.Forms;

namespace DyIsd.Voice;

/// <summary>What the island says after a command.</summary>
/// <param name="Confirm">Set when the command needs a "yes" first (shut down, empty the bin…).</param>
public sealed record Reply(string Glyph, string Brush, string Text, bool Failed = false, Func<Task<Reply>>? Confirm = null, bool Silent = false);

/// <summary>Does what CommandParser understood. Runs on the UI thread; key presses wait for you to let go of the keys first.</summary>
public sealed class CommandRunner
{
    readonly App _app;
    static AppSettings S => SettingsStore.Current;

    public CommandRunner(App app) => _app = app;

    static Reply Ok(string glyph, string text) => new(glyph, "Accent", text);
    static Reply Good(string glyph, string text) => new(glyph, "Good", text);
    static Reply Bad(string text) => new("", "Orange", text, Failed: true);

    public async Task<Reply> RunAsync(List<Cmd> cmds)
    {
        var labels = new List<string>();
        Reply last = Ok("", "Done");
        for (int i = 0; i < cmds.Count; i++)
        {
            var cmd = cmds[i];
            Reply r;
            try
            {
                r = await RunOneAsync(cmd);
            }
            catch (Exception ex)
            {
                Log.Error("jarvis run " + cmd.Kind, ex);
                r = Bad("That didn't work: " + ex.Message);
            }
            Log.Write($"jarvis did: {cmd.Kind} [{cmd.Text}] → {r.Text}");
            if (r.Failed || r.Confirm != null) return r; // stop at the first problem or question
            last = r;
            if (!r.Silent) labels.Add(r.Text);
            // After opening something, give it a moment to appear before typing into it.
            if (i + 1 < cmds.Count && cmd.Kind is "open" or "switch" or "bare" or "url")
                await WaitForNewWindowAsync(cmd.Kind == "url" ? 2500 : 4000);
        }
        if (labels.Count > 1) last = last with { Text = string.Join(" · ", labels.TakeLast(3)) };
        return last;
    }

    async Task<Reply> RunOneAsync(Cmd c)
    {
        switch (c.Kind)
        {
            // ---------------- apps, sites, folders ----------------
            case "open": return await OpenAsync(c.Text, switchFirst: false);
            case "switch": return await OpenAsync(c.Text, switchFirst: true);
            case "bare":
                {
                    var r = await OpenAsync(c.Text, switchFirst: true, strict: true);
                    return r.Failed ? Bad($"Didn't catch a command in \"{c.Text}\"") : r;
                }
            case "url":
                Shell(c.Text);
                return Ok(c.Text.StartsWith("ms-settings") ? "" : "", c.Say.Length > 0 ? c.Say : "Opened");
            case "close-app": return CloseApp(c.Text);
            case "window": return WindowCommand(c.Text, c.Say);

            // ---------------- keyboard ----------------
            case "keys":
                if (c.Keys == null || c.Keys.Length == 0) return Bad("I don't know that key");
                await KeysAsync(() => InputSim.Repeat(Math.Max(1, c.N), c.Keys));
                return Ok("", c.Say);
            case "type":
                await KeysAsync(() => InputSim.Type(c.Text));
                return Ok("", c.Text.Length > 26 ? $"Typed \"{c.Text[..24]}…\"" : $"Typed \"{c.Text}\"");
            case "find":
                await KeysAsync(() =>
                {
                    InputSim.Combo(CommandParser.Ctrl, 'F');
                    System.Threading.Thread.Sleep(250);
                    InputSim.Type(c.Text);
                });
                return Ok("", $"Find \"{c.Text}\"");
            case "scroll":
                await KeysAsync(() => InputSim.Scroll(c.N));
                return Ok(c.N > 0 ? "" : "", c.Say);

            // ---------------- sound & screen ----------------
            case "volume-set":
                _app.Volume.SetLevel(c.N / 100.0);
                return new Reply(VolumeService.GlyphFor(c.N / 100f, false), "Accent", $"Volume {c.N}", Silent: true);
            case "volume-step":
                if (_app.Volume.Muted) _app.Volume.ToggleMute();
                _app.Volume.Step(c.N);
                return new Reply("", "Accent", c.N > 0 ? "Louder" : "Quieter", Silent: true);
            case "mute":
                if ((c.Text == "mute") != _app.Volume.Muted) _app.Volume.ToggleMute();
                return new Reply("", "Accent", c.Text == "mute" ? "Muted" : "Unmuted", Silent: true);
            case "brightness-set":
            case "brightness-step":
                if (!_app.Brightness.Supported) return Bad("This screen's brightness can't be changed by apps");
                _app.Brightness.Step(c.Kind == "brightness-set" ? c.N - _app.Brightness.Level : c.N);
                return new Reply("", "Accent", "Brightness", Silent: true);

            // ---------------- music ----------------
            case "media": return await MediaAsync(c.Text);
            case "stop":
                if (_app.Media.State.IsPlaying) return await MediaAsync("pause");
                return new Reply("", "Accent", "Okay", Silent: true);
            case "playing":
                {
                    var m = _app.Media.State;
                    if (!m.HasSession) return Ok("", "Nothing is playing");
                    var who = string.IsNullOrWhiteSpace(m.Artist) ? "" : " · " + m.Artist;
                    return Ok("", $"{m.Title}{who}");
                }

            // ---------------- questions ----------------
            case "time": return Ok("", "It's " + DateTime.Now.ToString("h:mm tt"));
            case "date": return Ok("", DateTime.Now.ToString("dddd, d MMMM"));
            case "battery": return Battery();
            case "calc": return Ok("", $"{c.Say} = {c.Text}");
            case "deadlines":
                _app.ShowWhatsNext();
                return new Reply("", "Accent", "", Silent: true);

            // ---------------- timers & reminders ----------------
            case "timer":
                {
                    int secs = c.N > 0 ? c.N : S.FocusMinutes * 60;
                    _app.Timer.StartSeconds(secs, c.Text);
                    return Good("", $"{c.Text} · {Pretty(secs)}");
                }
            case "timer-stop":
                if (!_app.Timer.IsActive) return Ok("", "No timer running");
                _app.Timer.End();
                return Ok("", "Timer stopped");
            case "timer-pause":
                if (!_app.Timer.IsActive) return Ok("", "No timer running");
                _app.Timer.TogglePause();
                return Ok("", _app.Timer.IsRunning ? "Timer resumed" : "Timer paused");
            case "timer-add":
                if (!_app.Timer.IsActive) return Ok("", "No timer running");
                _app.Timer.AddFive();
                return Ok("", "+5 minutes");
            case "remind": return Remind(c);
            case "remind-ask": return Bad($"When? Try \"remind me to {c.Text.ToLowerInvariant()} at 5 pm\"");

            // ---------------- calls ----------------
            case "answer":
                return _app.Ring.Answer() ? Good("", "Answered") : Bad("No call is ringing");
            case "decline":
                return _app.Ring.Decline() ? new Reply("", "Bad", "Declined") : Bad("No call is ringing");
            case "hangup":
                return Bad("Hanging up comes in the next Jarvis update. Use the call window.");
            case "mic":
                {
                    bool mute = c.Text == "mute";
                    if (_app.Calls.State.IsActive)
                    {
                        if (_app.Calls.State.Muted != mute) _app.Calls.ToggleMute(S.DiscordMuteKey, S.DiscordMuteModifiers);
                    }
                    else _app.Mic.SetMuted(mute);
                    return new Reply(mute ? "" : "", mute ? "Bad" : "Good", mute ? "Mic muted" : "Mic on");
                }

            // ---------------- the PC ----------------
            case "radio": return await RadioAsync(c.Text, c.N);
            case "theme": return Theme(c.Text);
            case "power": return Power(c.Text);
            case "recycle":
                return new Reply("", "Orange", "Empty the Recycle Bin?", Confirm: () =>
                {
                    int hr = SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
                    return Task.FromResult(hr == 0 || hr == unchecked((int)0x8000FFFF) ? Ok("", "Recycle Bin emptied") : Bad("Couldn't empty the Recycle Bin"));
                });

            // ---------------- DyIsd & Jarvis ----------------
            case "island": return Island(c.Text);
            case "jarvis":
                S.JarvisWakeWord = c.Text == "wake";
                SettingsStore.Save();
                return Ok("", S.JarvisWakeWord ? "Listening for \"Jarvis\" again" : "Okay, only Ctrl + Space now");
            case "help":
                return Ok("", "Try: open chrome · search youtube for lofi · volume 40 · type hello · remind me to study at 7");
            case "thanks": return new Reply("", "Good", "Anytime, Imran");
            case "hello": return new Reply("", "Good", "Hey Imran. What do you need?");
            case "howareyou": return new Reply("", "Good", "Running smooth. What do you need?");
            case "whoami": return Ok("", "I'm Jarvis, DyIsd's voice. Say \"help\" for ideas.");
            case "cancel": return new Reply("", "Accent", "Okay", Silent: true);
            case "yes": return Ok("", "Nothing to confirm");
            case "later": return new Reply("", "Purple", c.Text, Failed: true);
            default: return Bad($"Didn't catch a command in \"{c.Text}\"");
        }
    }

    static string Pretty(int secs) =>
        secs >= 3600 ? $"{secs / 3600} h{(secs % 3600 / 60 > 0 ? $" {secs % 3600 / 60} min" : "")}"
        : secs >= 60 ? $"{secs / 60} min{(secs % 60 > 0 ? $" {secs % 60} s" : "")}" : $"{secs} s";

    // ---------------- opening things ----------------

    static readonly Dictionary<string, string> SitesMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube"] = "https://www.youtube.com", ["yt"] = "https://www.youtube.com", ["google"] = "https://www.google.com",
        ["gmail"] = "https://mail.google.com", ["email"] = "https://mail.google.com", ["my email"] = "https://mail.google.com",
        ["github"] = "https://github.com", ["linkedin"] = "https://www.linkedin.com", ["instagram"] = "https://www.instagram.com",
        ["insta"] = "https://www.instagram.com", ["facebook"] = "https://www.facebook.com", ["twitter"] = "https://x.com", ["x"] = "https://x.com",
        ["reddit"] = "https://www.reddit.com", ["netflix"] = "https://www.netflix.com", ["prime video"] = "https://www.primevideo.com",
        ["amazon prime"] = "https://www.primevideo.com", ["hotstar"] = "https://www.hotstar.com", ["jio hotstar"] = "https://www.hotstar.com",
        ["amazon"] = "https://www.amazon.in", ["flipkart"] = "https://www.flipkart.com", ["myntra"] = "https://www.myntra.com",
        ["swiggy"] = "https://www.swiggy.com", ["zomato"] = "https://www.zomato.com", ["chatgpt"] = "https://chatgpt.com",
        ["chat gpt"] = "https://chatgpt.com", ["claude ai"] = "https://claude.ai", ["gemini"] = "https://gemini.google.com",
        ["perplexity"] = "https://www.perplexity.ai", ["leetcode"] = "https://leetcode.com", ["leet code"] = "https://leetcode.com",
        ["codeforces"] = "https://codeforces.com", ["codechef"] = "https://www.codechef.com", ["hackerrank"] = "https://www.hackerrank.com",
        ["hacker rank"] = "https://www.hackerrank.com", ["geeksforgeeks"] = "https://www.geeksforgeeks.org", ["geeks for geeks"] = "https://www.geeksforgeeks.org",
        ["gfg"] = "https://www.geeksforgeeks.org", ["stack overflow"] = "https://stackoverflow.com", ["stackoverflow"] = "https://stackoverflow.com",
        ["w3schools"] = "https://www.w3schools.com", ["google drive"] = "https://drive.google.com", ["drive"] = "https://drive.google.com",
        ["google docs"] = "https://docs.google.com", ["docs"] = "https://docs.google.com", ["google sheets"] = "https://sheets.google.com",
        ["sheets"] = "https://sheets.google.com", ["google slides"] = "https://slides.google.com", ["google meet"] = "https://meet.google.com",
        ["meet"] = "https://meet.google.com", ["google maps"] = "https://maps.google.com", ["maps"] = "https://maps.google.com",
        ["google calendar"] = "https://calendar.google.com", ["google classroom"] = "https://classroom.google.com", ["classroom"] = "https://classroom.google.com",
        ["google translate"] = "https://translate.google.com", ["translate"] = "https://translate.google.com", ["wikipedia"] = "https://www.wikipedia.org",
        ["whatsapp web"] = "https://web.whatsapp.com", ["discord web"] = "https://discord.com/app", ["spotify web"] = "https://open.spotify.com",
        ["canva"] = "https://www.canva.com", ["figma"] = "https://www.figma.com", ["notion web"] = "https://www.notion.so",
        ["outlook web"] = "https://outlook.live.com", ["unstop"] = "https://unstop.com", ["internshala"] = "https://internshala.com",
        ["naukri"] = "https://www.naukri.com", ["kaggle"] = "https://www.kaggle.com", ["hugging face"] = "https://huggingface.co",
        ["vercel"] = "https://vercel.com", ["srm academia"] = "https://academia.srmist.edu.in", ["academia"] = "https://academia.srmist.edu.in",
        ["srm portal"] = "https://academia.srmist.edu.in", ["srm"] = "https://www.srmist.edu.in", ["coursera"] = "https://www.coursera.org",
        ["udemy"] = "https://www.udemy.com", ["nptel"] = "https://onlinecourses.nptel.ac.in", ["chess"] = "https://www.chess.com",
        ["chess com"] = "https://www.chess.com", ["pinterest"] = "https://www.pinterest.com", ["quora"] = "https://www.quora.com",
        ["medium"] = "https://medium.com", ["claude console"] = "https://platform.claude.com", ["anthropic console"] = "https://platform.claude.com",
        ["new tab page"] = "https://www.google.com", ["google news"] = "https://news.google.com", ["news"] = "https://news.google.com",
        ["weather"] = "https://www.google.com/search?q=weather", ["cricbuzz"] = "https://www.cricbuzz.com", ["cricket score"] = "https://www.cricbuzz.com",
        ["imdb"] = "https://www.imdb.com", ["duolingo"] = "https://www.duolingo.com", ["devpost"] = "https://devpost.com",
        ["product hunt"] = "https://www.producthunt.com", ["hacker news"] = "https://news.ycombinator.com",
    };

    static readonly Dictionary<string, string> SettingsPages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wifi"] = "network-wifi", ["network"] = "network-status", ["internet"] = "network-status", ["bluetooth"] = "bluetooth",
        ["devices"] = "bluetooth", ["display"] = "display", ["screen"] = "display", ["sound"] = "sound", ["audio"] = "sound",
        ["volume"] = "apps-volume", ["battery"] = "batterysaver", ["power"] = "powersleep", ["power and battery"] = "powersleep",
        ["update"] = "windowsupdate", ["updates"] = "windowsupdate", ["windows update"] = "windowsupdate", ["apps"] = "appsfeatures",
        ["installed apps"] = "appsfeatures", ["storage"] = "storagesense", ["personalization"] = "personalization",
        ["wallpaper"] = "personalization-background", ["background"] = "personalization-background", ["themes"] = "themes",
        ["theme"] = "themes", ["colors"] = "colors", ["dark mode"] = "colors", ["lock screen"] = "lockscreen", ["taskbar"] = "taskbar",
        ["start"] = "personalization-start", ["mouse"] = "mousetouchpad", ["touchpad"] = "devices-touchpad", ["keyboard"] = "typing",
        ["typing"] = "typing", ["privacy"] = "privacy", ["microphone"] = "privacy-microphone", ["mic"] = "privacy-microphone",
        ["camera"] = "privacy-webcam", ["webcam"] = "privacy-webcam", ["location"] = "privacy-location", ["notifications"] = "notifications",
        ["notification"] = "notifications", ["focus"] = "quiethours", ["do not disturb"] = "quiethours", ["night light"] = "nightlight",
        ["vpn"] = "network-vpn", ["hotspot"] = "network-mobilehotspot", ["mobile hotspot"] = "network-mobilehotspot",
        ["airplane mode"] = "network-airplanemode", ["date and time"] = "dateandtime", ["time"] = "dateandtime", ["date"] = "dateandtime",
        ["language"] = "regionlanguage", ["region"] = "regionlanguage", ["accounts"] = "yourinfo", ["account"] = "yourinfo",
        ["default apps"] = "defaultapps", ["startup apps"] = "startupapps", ["startup"] = "startupapps", ["printers"] = "printers",
        ["printer"] = "printers", ["about"] = "about", ["system"] = "about", ["about this pc"] = "about", ["sign in"] = "signinoptions",
        ["sign in options"] = "signinoptions", ["password"] = "signinoptions", ["multitasking"] = "multitasking", ["clipboard"] = "clipboard",
        ["graphics"] = "display-advancedgraphics", ["gpu"] = "display-advancedgraphics", ["recovery"] = "recovery",
        ["accessibility"] = "easeofaccess", ["fonts"] = "fonts", ["gaming"] = "gaming-gamebar", ["game mode"] = "gaming-gamemode",
        ["nearby sharing"] = "crossdevice", ["nearby share"] = "crossdevice", ["phone"] = "mobile-devices", ["proxy"] = "network-proxy",
        ["ethernet"] = "network-ethernet", ["sounds"] = "sound", ["scaling"] = "display", ["resolution"] = "display",
    };

    static readonly Dictionary<string, string> Folders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["downloads"] = "shell:Downloads", ["download"] = "shell:Downloads", ["download folder"] = "shell:Downloads", ["downloads folder"] = "shell:Downloads",
        ["documents"] = "shell:Personal", ["documents folder"] = "shell:Personal", ["my documents"] = "shell:Personal",
        ["desktop folder"] = "shell:Desktop", ["pictures"] = "shell:My Pictures", ["photos folder"] = "shell:My Pictures",
        ["pictures folder"] = "shell:My Pictures", ["screenshots"] = "shell:Screenshots", ["screenshots folder"] = "shell:Screenshots",
        ["music folder"] = "shell:My Music", ["videos"] = "shell:My Video", ["videos folder"] = "shell:My Video",
        ["recycle bin"] = "shell:RecycleBinFolder", ["trash"] = "shell:RecycleBinFolder", ["bin"] = "shell:RecycleBinFolder",
        ["home folder"] = "shell:Profile", ["user folder"] = "shell:Profile", ["appdata"] = "shell:Local AppData",
        ["app data"] = "shell:Local AppData", ["temp"] = "%TEMP%", ["temp folder"] = "%TEMP%", ["startup folder"] = "shell:Startup",
        ["onedrive"] = "shell:OneDrive", ["one drive"] = "shell:OneDrive", ["control panel"] = "shell:ControlPanelFolder",
        ["dyisd folder"] = Log.Dir, ["dyisd logs"] = Log.Dir, ["your logs"] = Log.Dir, ["log"] = Log.Dir,
    };

    async Task<Reply> OpenAsync(string target, bool switchFirst, bool strict = false)
    {
        var t = target.Trim().TrimEnd('.');
        t = System.Text.RegularExpressions.Regex.Replace(t, @"^(?:the|my|a|an)\s+", "");
        if (t.Length == 0) return Bad("Open what?");

        // Settings pages: "wifi settings", "settings for bluetooth"
        var sm = System.Text.RegularExpressions.Regex.Match(t, @"^(?:(.+?) settings?|settings? (?:for|of) (.+))$");
        if (sm.Success)
        {
            var key = sm.Groups[1].Success ? sm.Groups[1].Value : sm.Groups[2].Value;
            if (SettingsPages.TryGetValue(key, out var page)) { Shell("ms-settings:" + page); return Ok("", Title(key) + " settings"); }
        }
        if (Folders.TryGetValue(t, out var folder))
        {
            Shell(Environment.ExpandEnvironmentVariables(folder));
            return Ok("", Title(t));
        }

        // An address: "github.com", "example dot com"
        var addr = t.Replace(" dot ", ".").Replace(" ", "");
        if (System.Text.RegularExpressions.Regex.IsMatch(addr, @"^[\w-]+(\.[\w-]+)*\.(com|in|org|net|io|ai|dev|edu|co|app|me|gov|info|xyz|tech|ly|gg|tv)(/\S*)?$"))
        {
            Shell("https://" + addr);
            return Ok("", addr);
        }

        // Already open? Bring it to the front.
        if (switchFirst && FindWindow(t) is { } win)
        {
            FocusWindow(win.Handle);
            return Ok("", Title(win.Name));
        }

        var app = AppCatalog.Find(t);
        bool siteKnown = SitesMap.TryGetValue(t, out var site);
        // An installed app with this exact name wins; otherwise the website.
        if (app != null && (!siteKnown || AppCatalog.Normalize(app.Name) == AppCatalog.Normalize(t) || AppCatalog.IsAlias(t)))
        {
            AppCatalog.Launch(app);
            return Ok("", app.Name);
        }
        if (siteKnown)
        {
            Shell(site!);
            return Ok("", Title(t));
        }
        if (!strict && FindWindow(t) is { } w2)
        {
            FocusWindow(w2.Handle);
            return Ok("", Title(w2.Name));
        }
        if (strict) return Bad($"No app called \"{t}\"");
        await Task.CompletedTask;
        return Bad($"No app called \"{t}\". Say \"search {t}\" to look it up.");
    }

    static string Title(string s) => s.Length == 0 ? s : string.Join(' ', s.Split(' ').Select(w => w.Length <= 2 ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..]));

    static void Shell(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    sealed record WindowHit(IntPtr Handle, string Name, Process Process);

    /// <summary>An open window whose app name or title sounds like what you said.</summary>
    static WindowHit? FindWindow(string spoken)
    {
        var said = AppCatalog.Normalize(spoken);
        if (said.Length == 0) return null;
        WindowHit? best = null;
        double bestScore = 0;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero || p.Id == Environment.ProcessId) continue;
                string name = p.ProcessName, title = p.MainWindowTitle;
                string described = "";
                try { described = p.MainModule?.FileVersionInfo.FileDescription ?? ""; } catch { }
                double s = new[]
                {
                    Fuzzy.Score(said, AppCatalog.Normalize(name)),
                    Fuzzy.Score(said, AppCatalog.Normalize(described)),
                    TitleScore(said, title),
                }.Max();
                if (s > bestScore) { bestScore = s; best = new WindowHit(p.MainWindowHandle, described.Length > 0 ? described : name, p); }
            }
            catch { }
        }
        return bestScore >= 0.8 ? best : null;
    }

    static double TitleScore(string said, string title)
    {
        var t = AppCatalog.Normalize(title);
        if (t.Length == 0) return 0;
        // "Inbox - Gmail - Google Chrome": the app is usually after the last dash.
        var tail = AppCatalog.Normalize(title.Split(new[] { " - ", " — ", " | " }, StringSplitOptions.None).Last());
        if (tail == said) return 1;
        return t.Contains(said) && said.Length >= 4 ? 0.85 : Fuzzy.Score(said, tail) * 0.95;
    }

    static void FocusWindow(IntPtr h)
    {
        // Windows only lets the app that got the last key press change the front window,
        // so press a key nobody uses (F24) first.
        InputSim.Combo(0x87);
        if (Win32.IsIconic(h)) Win32.ShowWindow(h, Win32.SW_RESTORE);
        Win32.SetForegroundWindow(h);
    }

    static Reply CloseApp(string spoken)
    {
        var hit = FindWindow(spoken);
        if (hit == null) return Bad($"\"{spoken}\" isn't open");
        int closed = 0;
        foreach (var p in Process.GetProcessesByName(hit.Process.ProcessName))
        {
            try { if (p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow()) closed++; } catch { }
        }
        return closed > 0 ? Ok("", "Closed " + Title(hit.Name)) : Bad($"Couldn't close {hit.Name}");
    }

    static Reply WindowCommand(string what, string say)
    {
        var h = Win32.GetForegroundWindow();
        if (h == IntPtr.Zero) return Bad("No window in front");
        Win32.ShowWindow(h, what switch { "minimize" => 6, "maximize" => 3, _ => Win32.SW_RESTORE });
        return Ok("", say);
    }

    static async Task WaitForNewWindowAsync(int maxMs)
    {
        var before = Win32.GetForegroundWindow();
        int waited = 0;
        while (waited < maxMs && Win32.GetForegroundWindow() == before)
        {
            await Task.Delay(100);
            waited += 100;
        }
        await Task.Delay(waited >= maxMs ? 0 : 600); // let it finish drawing and focus its text box
    }

    static Task KeysAsync(Action press) => Task.Run(() =>
    {
        InputSim.WaitForKeysReleased();
        press();
    });

    // ---------------- music ----------------

    async Task<Reply> MediaAsync(string what)
    {
        var m = _app.Media;
        if (!m.State.HasSession) return Bad("Nothing is playing. Say \"play\" and a song to search YouTube.");
        switch (what)
        {
            case "play":
                if (!m.State.IsPlaying) await m.TogglePlayPauseAsync();
                return new Reply("", "Accent", "Playing", Silent: true);
            case "pause":
                if (m.State.IsPlaying) await m.TogglePlayPauseAsync();
                return new Reply("", "Accent", "Paused", Silent: true);
            case "next": await m.NextAsync(); return new Reply("", "Accent", "Next", Silent: true);
            case "prev": await m.PreviousAsync(); return new Reply("", "Accent", "Previous", Silent: true);
            case "restart":
                if (m.State.CanSeek) await m.SeekAsync(0); else await m.PreviousAsync();
                return new Reply("", "Accent", "From the top", Silent: true);
            case "shuffle":
                await m.ToggleShuffleAsync();
                return Ok("", m.State.Shuffle ? "Shuffle off" : "Shuffle on");
            case "repeat":
                await m.CycleRepeatAsync();
                return Ok("", "Repeat changed");
        }
        return Bad("Hmm?");
    }

    // ---------------- battery ----------------

    static Reply Battery()
    {
        var ps = WF.SystemInformation.PowerStatus;
        if (ps.BatteryChargeStatus.HasFlag(WF.BatteryChargeStatus.NoSystemBattery)) return Ok("", "No battery, you're on power");
        int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
        bool charging = ps.PowerLineStatus == WF.PowerLineStatus.Online;
        string extra = charging ? " · charging" : ps.BatteryLifeRemaining > 0 ? $" · about {Pretty(ps.BatteryLifeRemaining / 60 * 60)} left" : "";
        return new Reply(BatteryService.GlyphFor(pct), pct < 20 && !charging ? "Bad" : "Good", $"Battery {pct}%{extra}");
    }

    // ---------------- reminders ----------------

    Reply Remind(Cmd c)
    {
        var when = c.When!.Value;
        bool deadline = c.N == 1;
        var d = new ManualDeadline
        {
            Title = c.Text,
            Due = when,
            RemindBeforeMinutes = deadline ? new[] { 1440, 120, 30 }.Where(m => when.AddMinutes(-m) > DateTime.Now).ToList() : new List<int> { 0 },
        };
        if (deadline && !d.RemindBeforeMinutes.Contains(0)) d.RemindBeforeMinutes.Add(0);
        S.ManualDeadlines.Add(d);
        SettingsStore.Save();
        string day = when.Date == DateTime.Today ? "today" : when.Date == DateTime.Today.AddDays(1) ? "tomorrow" : when.ToString("ddd d MMM");
        return Good("", $"{(deadline ? "Deadline" : "Reminder")}: {c.Text} · {day} {when:h:mm tt}");
    }

    // ---------------- Wi-Fi, Bluetooth, theme, power ----------------

    static async Task<Reply> RadioAsync(string which, int state)
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed) return Bad("Windows won't let apps change radios. Opened the setting.");
            var radios = await Radio.GetRadiosAsync();
            var list = radios.Where(r => which == "all" || (which == "wifi" ? r.Kind == RadioKind.WiFi : r.Kind == RadioKind.Bluetooth)).ToList();
            if (list.Count == 0) return Bad(which == "wifi" ? "No Wi-Fi adapter found" : "No Bluetooth adapter found");
            bool on = state switch { 1 => true, 0 => false, _ => list[0].State != RadioState.On };
            foreach (var r in list) await r.SetStateAsync(on ? RadioState.On : RadioState.Off);
            string name = which == "all" ? (on ? "Airplane mode off" : "Airplane mode on") : (which == "wifi" ? "Wi-Fi " : "Bluetooth ") + (on ? "on" : "off");
            return Ok(which == "bluetooth" ? "" : which == "wifi" ? "" : "", name);
        }
        catch (Exception ex)
        {
            Log.Error("radio", ex);
            Shell(which == "bluetooth" ? "ms-settings:bluetooth" : which == "wifi" ? "ms-settings:network-wifi" : "ms-settings:network-airplanemode");
            return Bad("Couldn't switch it, opened the setting instead");
        }
    }

    static Reply Theme(string mode)
    {
        const string key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        using var k = Registry.CurrentUser.OpenSubKey(key, writable: true);
        if (k == null) return Bad("Couldn't change the theme");
        bool light = mode switch
        {
            "light" => true,
            "dark" => false,
            _ => (k.GetValue("AppsUseLightTheme") as int? ?? 1) == 0,
        };
        k.SetValue("AppsUseLightTheme", light ? 1 : 0, RegistryValueKind.DWord);
        k.SetValue("SystemUsesLightTheme", light ? 1 : 0, RegistryValueKind.DWord);
        // Tell open apps the theme changed.
        SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, "ImmersiveColorSet", 0x2, 200, out _);
        return Ok(light ? "" : "", light ? "Light mode" : "Dark mode");
    }

    static Reply Power(string what)
    {
        switch (what)
        {
            case "lock":
                LockWorkStation();
                return new Reply("", "Accent", "Locked", Silent: true);
            case "sleep":
                return new Reply("", "Orange", "Put the laptop to sleep?", Confirm: async () =>
                {
                    await Task.Delay(800);
                    SetSuspendState(false, false, false);
                    return Ok("", "Sleeping");
                });
            case "shutdown":
                return new Reply("", "Bad", "Shut down the laptop?", Confirm: () =>
                {
                    Process.Start(new ProcessStartInfo("shutdown", "/s /t 2") { CreateNoWindow = true, UseShellExecute = false });
                    return Task.FromResult(Ok("", "Shutting down"));
                });
            case "restart":
                return new Reply("", "Orange", "Restart the laptop?", Confirm: () =>
                {
                    Process.Start(new ProcessStartInfo("shutdown", "/r /t 2") { CreateNoWindow = true, UseShellExecute = false });
                    return Task.FromResult(Ok("", "Restarting"));
                });
            default:
                return new Reply("", "Orange", "Sign out of Windows?", Confirm: () =>
                {
                    Process.Start(new ProcessStartInfo("shutdown", "/l") { CreateNoWindow = true, UseShellExecute = false });
                    return Task.FromResult(Ok("", "Signing out"));
                });
        }
    }

    Reply Island(string what)
    {
        switch (what)
        {
            case "hide":
                _ = Task.Delay(1800).ContinueWith(_ => _app.Dispatcher.BeginInvoke(() => _app.IslandPaused = true));
                return Ok("", "Hiding the island. Tray icon brings it back.");
            case "show":
                _app.IslandPaused = false;
                return Ok("", "Island's back");
            case "settings":
                _app.OpenControlCenter();
                return new Reply("", "Accent", "Control Center", Silent: true);
            default:
                S.Position = what;
                SettingsStore.Save();
                return Ok("", "Moved " + what);
        }
    }

    // ---------------- Windows ----------------

    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate, bool force, bool wakeupEventsDisabled);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
