using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using DyIsd.Voice;

namespace DyIsd.Services;

/// <summary>
/// Reads and presses Discord's own Mute, Deafen and Disconnect buttons (the ones next to your
/// name at the bottom left), and joins voice channels by name. Discord has no API for this,
/// so it goes through UI Automation; what it sees is logged ("discord sees") for tuning.
/// Use from a background thread.
/// </summary>
public static class DiscordControl
{
    public static readonly string[] Procs = { "discord", "discordptb", "discordcanary" };
    const RegexOptions I = RegexOptions.IgnoreCase;
    static readonly Regex MuteRe = new(@"^(mute|unmute)$", I);
    static readonly Regex DeafenRe = new(@"^(deafen|undeafen)$", I);
    static readonly Regex DisconnectRe = new(@"^(disconnect|leave call|hang up)$", I);
    static readonly Regex VoiceRe = new(@"^(?<name>.+?)[\s,(]+voice channel", I);
    static string _logged = "";

    static AutomationElement? Window() => AppDriver.WaitWindow(Procs, 0);

    static Uia.Node? Button(Regex re)
    {
        var w = Window();
        if (w == null) return null;
        var hit = AppDriver.All(w, re, ControlType.Button).FirstOrDefault();
        if (hit == null)
        {
            var seen = AppDriver.Describe(w, 20, ControlType.Button);
            if (seen != _logged) { _logged = seen; Log.Write("discord sees buttons: " + seen); }
        }
        return hit;
    }

    /// <summary>True / false from Discord itself, or null when the button can't be read.</summary>
    static bool? State(Regex re, string onName)
    {
        var b = Button(re);
        if (b == null) return null;
        // Newer Discord: a "Mute" toggle that's checked. Older: the label flips to "Unmute".
        return AppDriver.IsOn(b.Element) ?? b.Name.Equals(onName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool? IsMuted() => State(MuteRe, "Unmute");
    public static bool? IsDeafened() => State(DeafenRe, "Undeafen");

    static bool Press(Regex re)
    {
        var b = Button(re);
        return b != null && AppDriver.Activate(b.Element);
    }

    public static bool ToggleMute() => Press(MuteRe);
    public static bool ToggleDeafen() => Press(DeafenRe);
    public static bool Disconnect() => Press(DisconnectRe);

    /// <summary>
    /// Calls someone from your DMs: Discord's quick switcher (Ctrl+K) opens their chat, then the
    /// "Start Voice Call" button.
    /// </summary>
    public static bool CallUser(string who)
    {
        var w = Window();
        if (w == null) return false;
        AppDriver.Focus(w);
        InputSim.WaitForKeysReleased();
        InputSim.Combo(CommandParser.Ctrl, 'K');
        System.Threading.Thread.Sleep(500);
        InputSim.Type(who);
        System.Threading.Thread.Sleep(900);
        InputSim.Combo(CommandParser.Enter);
        var btn = AppDriver.WaitFor(w, new Regex(@"^start (voice )?call$", I), 4000, ControlType.Button);
        if (btn == null)
        {
            Log.Write("discord sees buttons in chat: " + AppDriver.Describe(w, 20, ControlType.Button));
            return false;
        }
        return AppDriver.Activate(btn.Element);
    }

    // ---------------- messages ----------------

    /// <summary>"general | Locked in - Discord" → ("general", "Locked in"); a DM gives ("@name", "").</summary>
    static (string Chat, string Server) Where()
    {
        var w = Window();
        string title;
        try { title = w?.Current.Name ?? ""; } catch { return ("", ""); }
        title = Regex.Replace(title, @"^\(\d+\)\s*|^[•●]\s*|\s*-\s*Discord$", "", I).Trim();
        int bar = title.LastIndexOf(" | ", StringComparison.Ordinal);
        return bar < 0 ? (title, "") : (title[..bar].Trim().TrimStart('#'), title[(bar + 3)..].Trim());
    }

    static void QuickSwitch(string text)
    {
        // Close whatever is open first; if Ctrl+K lands while Discord is still busy the search
        // doesn't open and the text would go into the message box instead.
        InputSim.Combo(CommandParser.Esc);
        System.Threading.Thread.Sleep(250);
        InputSim.Combo(CommandParser.Ctrl, 'K');
        System.Threading.Thread.Sleep(800);
        InputSim.Type(text);
        System.Threading.Thread.Sleep(900);
        InputSim.Combo(CommandParser.Enter);
        System.Threading.Thread.Sleep(1100);
    }

    /// <summary>
    /// Opens a channel in the server (or, failing that, a DM) with Discord's quick switcher and
    /// returns what Discord really opened ("#general in Locked in", "MRGorilla"), checked
    /// against its window title; null if it isn't what you asked for. Sends nothing.
    /// </summary>
    public static string? OpenChat(string who, string server)
    {
        var w = Window();
        if (w == null)
        {
            // Closed to the tray: Discord's own link brings its window back.
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("discord://-/channels/@me") { UseShellExecute = true }); } catch { }
            w = AppDriver.WaitWindow(Procs, 10000);
            if (w == null) return null;
            System.Threading.Thread.Sleep(1500);
        }
        AppDriver.Focus(w);
        InputSim.WaitForKeysReleased();
        // Only search when needed: already there means no typing at all.
        var (chat, srv) = Where();
        bool inServer = srv.Length > 0 && AppDriver.NameScore(server, srv) >= 0.7;
        if (!(inServer && AppDriver.NameScore(who, chat) >= 0.7))
        {
            if (!inServer) QuickSwitch("*" + server); // the server first, so "#general" means its general
            QuickSwitch("#" + who);
            (chat, srv) = Where();
        }
        Log.Write($"discord: asked #{who} in {server}, Discord shows \"{chat}\" in \"{srv}\"");
        if (srv.Length > 0 && AppDriver.NameScore(who, chat) >= 0.7 && AppDriver.NameScore(server, srv) >= 0.7) return $"#{chat} in {srv}";
        QuickSwitch("@" + who); // not a channel there: a person
        (chat, srv) = Where();
        Log.Write($"discord: asked @{who}, Discord shows \"{chat}\"");
        if (srv.Length == 0 && AppDriver.NameScore(who, chat.TrimStart('@')) >= 0.7) return chat.TrimStart('@');
        InputSim.Combo(CommandParser.Esc);
        return null;
    }

    /// <summary>Types into the open chat, but only if it's still the one you said yes to.</summary>
    public static bool SendInOpenChat(string expected, string message)
    {
        var w = Window();
        if (w == null) return false;
        AppDriver.Focus(w);
        InputSim.WaitForKeysReleased();
        var (chat, srv) = Where();
        string now = srv.Length > 0 ? $"#{chat} in {srv}" : chat.TrimStart('@');
        if (now != expected) { Log.Write($"discord: expected {expected}, now on {now}; not sent"); return false; }
        // Put the cursor in the message box and clear any half-written draft, so only your words go.
        InputSim.Combo(CommandParser.Esc);
        System.Threading.Thread.Sleep(150);
        InputSim.Combo(CommandParser.Ctrl, 'A');
        InputSim.Combo(CommandParser.Back);
        System.Threading.Thread.Sleep(150);
        InputSim.Type(message);
        System.Threading.Thread.Sleep(200);
        InputSim.Combo(CommandParser.Enter);
        Log.Write($"discord: sent a message to {expected}");
        return true;
    }

    /// <summary>Joins the voice channel whose name sounds most like what you said. Returns its name.</summary>
    public static string? JoinVoice(string spoken)
    {
        var w = Window();
        if (w == null) return null;
        var channels = Uia.Find(w, ControlType.Hyperlink, ControlType.TreeItem, ControlType.ListItem, ControlType.Button)
            .Select(n => (Node: n, M: VoiceRe.Match(n.Name)))
            .Where(x => x.M.Success)
            .Select(x => (x.Node, Name: x.M.Groups["name"].Value.Trim()))
            .ToList();
        Log.Write("discord sees voice channels: " + string.Join(" | ", channels.Select(c => c.Name).Distinct().Take(15)));
        if (channels.Count == 0) return null;
        var best = string.IsNullOrWhiteSpace(spoken)
            ? channels[0]
            : channels.OrderByDescending(c => AppDriver.NameScore(spoken, c.Name)).First();
        if (!string.IsNullOrWhiteSpace(spoken) && AppDriver.NameScore(spoken, best.Name) < 0.6) return null;
        return AppDriver.Activate(best.Node.Element) ? best.Name : null;
    }
}
