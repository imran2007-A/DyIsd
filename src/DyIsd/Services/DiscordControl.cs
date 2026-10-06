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
