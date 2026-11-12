using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DyIsd.Voice;

public sealed record InstalledApp(string Name, string AppId);

/// <summary>
/// Every app you can say "open …" to: the Start menu (desktop and Store apps, from Windows' own
/// list), plus desktop shortcuts and Epic Games / Steam games that aren't in the Start menu.
/// Read in the background, cached to disk, refreshed every 30 min.
/// </summary>
public static class AppCatalog
{
    static List<InstalledApp> _apps = new();
    static DateTime _loaded;
    static readonly string CacheFile = Path.Combine(Log.Dir, "apps.json");

    public static IReadOnlyList<InstalledApp> Apps
    {
        get
        {
            if (DateTime.Now - _loaded > TimeSpan.FromMinutes(30)) _ = RefreshAsync();
            return _apps;
        }
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(CacheFile))
                _apps = JsonSerializer.Deserialize<List<InstalledApp>>(File.ReadAllText(CacheFile)) ?? new();
        }
        catch { }
        _ = RefreshAsync();
    }

    static bool _refreshing;

    public static async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        _loaded = DateTime.Now;
        try
        {
            var json = await Task.Run(() =>
            {
                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -Command \"Get-StartApps | Select-Object Name,AppID | ConvertTo-Json -Compress\"")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi)!;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
                return output;
            });
            var list = new List<InstalledApp>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
                foreach (var e in items)
                {
                    var name = e.GetProperty("Name").GetString() ?? "";
                    var id = e.GetProperty("AppID").GetString() ?? "";
                    if (name.Length > 0 && id.Length > 0) list.Add(new InstalledApp(name, id));
                }
            }
            catch (Exception ex) { Log.Error("start apps", ex); }
            int start = list.Count;

            // Games and shortcuts the Start menu doesn't list. The Start menu wins on duplicates.
            var extra = await Task.Run(() => Desktop().Concat(EpicGames()).Concat(SteamGames()).ToList());
            var known = new HashSet<string>(list.Select(a => Normalize(a.Name)));
            foreach (var a in extra)
                if (known.Add(Normalize(a.Name))) list.Add(a);

            if (list.Count > 0)
            {
                _apps = list;
                Directory.CreateDirectory(Log.Dir);
                File.WriteAllText(CacheFile, JsonSerializer.Serialize(list));
                Log.Write($"apps: {start} in the Start menu, {list.Count - start} more from the desktop, Epic and Steam");
            }
        }
        catch (Exception ex)
        {
            Log.Error("app list", ex);
        }
        finally
        {
            _refreshing = false;
        }
    }

    // What people say → what the Start menu calls it.
    static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Google Chrome", ["google chrome"] = "Google Chrome", ["browser"] = "Google Chrome",
        ["edge"] = "Microsoft Edge", ["vs code"] = "Visual Studio Code", ["vscode"] = "Visual Studio Code",
        ["code"] = "Visual Studio Code", ["visual studio code"] = "Visual Studio Code",
        ["word"] = "Word", ["excel"] = "Excel", ["powerpoint"] = "PowerPoint", ["power point"] = "PowerPoint",
        ["file explorer"] = "File Explorer", ["explorer"] = "File Explorer", ["files"] = "File Explorer",
        ["my files"] = "File Explorer", ["this pc"] = "File Explorer",
        ["settings"] = "Settings", ["calculator"] = "Calculator", ["calc"] = "Calculator",
        ["notepad"] = "Notepad", ["terminal"] = "Terminal", ["command prompt"] = "Command Prompt", ["cmd"] = "Command Prompt",
        ["powershell"] = "Windows PowerShell", ["task manager"] = "Task Manager", ["camera"] = "Camera",
        ["clock"] = "Clock", ["alarms"] = "Clock", ["timer"] = "Clock", ["stopwatch"] = "Clock",
        ["photos"] = "Photos", ["paint"] = "Paint", ["snipping tool"] = "Snipping Tool", ["snip"] = "Snipping Tool",
        ["store"] = "Microsoft Store", ["microsoft store"] = "Microsoft Store", ["teams"] = "Microsoft Teams",
        ["outlook"] = "Outlook", ["mail"] = "Outlook", ["calendar"] = "Calendar", ["music"] = "Apple Music",
        ["apple music"] = "Apple Music", ["itunes"] = "iTunes", ["whatsapp"] = "WhatsApp", ["whats app"] = "WhatsApp",
        ["discord"] = "Discord", ["spotify"] = "Spotify", ["claude"] = "Claude", ["chat gpt"] = "ChatGPT",
        ["chatgpt"] = "ChatGPT", ["telegram"] = "Telegram", ["zoom"] = "Zoom", ["vlc"] = "VLC media player",
        ["media player"] = "Media Player", ["sound recorder"] = "Sound Recorder", ["voice recorder"] = "Sound Recorder",
        ["control panel"] = "Control Panel", ["phone link"] = "Phone Link", ["copilot"] = "Copilot",
        ["notion"] = "Notion", ["obs"] = "OBS Studio", ["steam"] = "Steam", ["brave"] = "Brave", ["firefox"] = "Firefox",
    };

    /// <summary>The app you most likely mean, or null when nothing is close enough.</summary>
    public static InstalledApp? Find(string spoken)
    {
        var said = Normalize(spoken);
        if (said.Length == 0) return null;
        var apps = Apps;
        if (Aliases.TryGetValue(said, out var alias))
        {
            var hit = apps.FirstOrDefault(a => Normalize(a.Name) == Normalize(alias))
                      ?? apps.FirstOrDefault(a => Normalize(a.Name).StartsWith(Normalize(alias)));
            if (hit != null) return hit;
        }

        InstalledApp? best = null;
        double bestScore = 0;
        foreach (var app in apps)
        {
            double s = Fuzzy.Score(said, Normalize(app.Name));
            // Uninstallers, help files and "readme" entries are never what you mean.
            if (app.Name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
                app.Name.Contains("readme", StringComparison.OrdinalIgnoreCase)) s -= 0.5;
            if (s > bestScore) { bestScore = s; best = app; }
        }
        return bestScore >= 0.72 ? best : null;
    }

    /// <summary>True for nicknames DyIsd knows ("vs code", "music"), which always mean the app.</summary>
    public static bool IsAlias(string spoken) => Aliases.ContainsKey(Normalize(spoken));

    public static string Normalize(string s) =>
        string.Join(' ', new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>AppIds starting with this are a shortcut file, game link or exe, not a Start-menu id.</summary>
    const string Direct = "launch:";

    public static void Launch(InstalledApp app)
    {
        if (app.AppId.StartsWith(Direct))
            Process.Start(new ProcessStartInfo(app.AppId[Direct.Length..]) { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{app.AppId}") { UseShellExecute = true });
    }

    /// <summary>Names worth teaching the speech engine: games and less common apps first.</summary>
    public static IEnumerable<string> SpokenNames(int max) =>
        Apps.OrderBy(a => a.AppId.StartsWith(Direct) ? 0 : 1)
            .Select(a => a.Name)
            .Where(n => n.Length is > 2 and < 24 && !Regex.IsMatch(n, @"uninstall|readme|help|documentation|website|manual|license|release notes|support", RegexOptions.IgnoreCase))
            .Distinct()
            .Take(max);

    // ---------------- things the Start menu misses ----------------

    static readonly Regex Junk = new(@"uninstall|readme|help|documentation|manual|license|release notes|website|redistributable|steamworks|directx|vcredist", RegexOptions.IgnoreCase);

    /// <summary>Shortcuts on your desktop and the shared desktop (.lnk, .url).</summary>
    static IEnumerable<InstalledApp> Desktop()
    {
        var found = new List<InstalledApp>();
        foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) })
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext is not (".lnk" or ".url" or ".appref-ms")) continue;
                    var name = Path.GetFileNameWithoutExtension(f);
                    if (!Junk.IsMatch(name)) found.Add(new InstalledApp(name, Direct + f));
                }
            }
            catch (Exception ex) { Log.Write("desktop shortcuts: " + ex.Message); }
        }
        return found;
    }

    /// <summary>Installed Epic Games titles (Fortnite, Rocket League…), from the launcher's own records.</summary>
    internal static IEnumerable<InstalledApp> EpicGames(string? dir = null)
    {
        var found = new List<InstalledApp>();
        dir ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        try
        {
            if (!Directory.Exists(dir)) return found;
            foreach (var f in Directory.EnumerateFiles(dir, "*.item"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(f));
                    var r = doc.RootElement;
                    string Get(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                    var name = Get("DisplayName");
                    var appName = Get("AppName");
                    if (name.Length == 0 || appName.Length == 0 || Junk.IsMatch(name)) continue;
                    // DLC and add-ons have their own records pointing at the main game: skip them.
                    var main = Get("MainGameAppName");
                    if (main.Length > 0 && !main.Equals(appName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (r.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == JsonValueKind.True) continue;
                    var ns = Get("CatalogNamespace");
                    var item = Get("CatalogItemId");
                    var id = ns.Length > 0 && item.Length > 0 ? $"{ns}%3A{item}%3A{appName}" : appName;
                    found.Add(new InstalledApp(name, Direct + $"com.epicgames.launcher://apps/{id}?action=launch&silent=true"));
                }
                catch { }
            }
        }
        catch (Exception ex) { Log.Write("epic games: " + ex.Message); }
        return found;
    }

    /// <summary>Installed Steam games, from Steam's library folders.</summary>
    static IEnumerable<InstalledApp> SteamGames()
    {
        var found = new List<InstalledApp>();
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steam = (key?.GetValue("SteamPath") as string)?.Replace('/', '\\');
            if (string.IsNullOrEmpty(steam)) return found;
            var libraries = new List<string> { Path.Combine(steam, "steamapps") };
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libraries.Add(Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps"));
            foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(lib)) continue;
                foreach (var f in Directory.EnumerateFiles(lib, "appmanifest_*.acf"))
                {
                    var text = File.ReadAllText(f);
                    var id = Regex.Match(text, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                    var name = Regex.Match(text, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                    if (id.Length > 0 && name.Length > 0 && !Junk.IsMatch(name))
                        found.Add(new InstalledApp(name, Direct + $"steam://rungameid/{id}"));
                }
            }
        }
        catch (Exception ex) { Log.Write("steam games: " + ex.Message); }
        return found;
    }
}

/// <summary>How alike two phrases are, 0..1, tolerant of speech-recognition slips.</summary>
public static class Fuzzy
{
    public static double Score(string said, string name)
    {
        if (said == name) return 1;
        if (name.StartsWith(said + " ") || name.EndsWith(" " + said) || name.Contains(" " + said + " ")) return 0.9;
        if (said.Length >= 4 && name.StartsWith(said)) return 0.85;
        var a = said.Replace(" ", "");
        var b = name.Replace(" ", "");
        if (a == b) return 0.95;
        int d = Levenshtein(a, b);
        return 1.0 - (double)d / Math.Max(a.Length, b.Length);
    }

    public static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
