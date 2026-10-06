using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DyIsd.Voice;

/// <summary>One thing Jarvis should do.</summary>
/// <param name="Kind">What to do (see CommandRunner for the list).</param>
/// <param name="Text">The app, site, words to type, search terms…</param>
/// <param name="N">A number: volume level, scroll steps, repeat count, seconds…</param>
/// <param name="Keys">Keys to press together (virtual key codes).</param>
/// <param name="Say">Short label for the island ("New tab").</param>
public sealed record Cmd(string Kind, string Text = "", int N = 0, int[]? Keys = null, string Say = "", DateTime? When = null);

/// <summary>
/// Turns what you said into commands, without any AI: a long list of phrasings for each
/// action. Sentences like "open chrome and search for cats" become two commands.
/// Unknown phrasing comes back as Kind "unknown" so the island can say what it heard.
/// </summary>
public static class CommandParser
{
    const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // ---------------- keys ----------------
    public const int Ctrl = 0x11, Shift = 0x10, Alt = 0x12, Win = 0x5B, Tab = 0x09, Enter = 0x0D, Esc = 0x1B,
        Space = 0x20, Back = 0x08, Del = 0x2E, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28, Home = 0x24,
        End = 0x23, PgUp = 0x21, PgDn = 0x22, F1 = 0x70, Plus = 0xBB, Minus = 0xBD, Period = 0xBE, Comma = 0xBC,
        Slash = 0xBF, PrtSc = 0x2C;

    static int K(char c) => char.ToUpperInvariant(c);
    static int[] Keys(params int[] k) => k;

    // ---------------- entry ----------------

    static readonly string Verbs =
        "open|launch|start|close|quit|exit|search|google|look|find|type|write|dictate|press|hit|scroll|play|pause|resume|skip|go|switch|" +
        "minimi[sz]e|maximi[sz]e|mute|unmute|set|turn|take|lock|show|hide|copy|paste|undo|redo|select|save|refresh|reload|zoom|reopen|" +
        "increase|decrease|lower|raise|move|snap|remind|translate|navigate|empty|enable|disable|answer|decline|send|new|" +
        "message|text|call|ask|tell|join|deafen|undeafen|leave|hang";

    static readonly Regex Splitter2 = new($@"(?:\s*,\s*(?:and\s+|then\s+)?|\s+(?:and\s+then|and|then|after\s+that|also)\s+)(?=(?:{Verbs})\b)", I);
    static readonly Regex TypeStart = new(@"^\s*(?:type|write|dictate)(?:\s+(?:out|down|in))?\b\s*[:,]?\s*", I);
    static readonly Regex WholeStart = new(@"^\s*(?:(?:ask|tell)\s+claude\b|(?:send|write|text|message|msg|whatsapp|ping|tell)\b.*?\b(?:saying|that says|to say|that)\b)", I);
    static readonly Regex TypeTail = new(@"[\s,]*(?:(?:and|then|and\s+then)\s+)?(?:(?:press|hit)\s+(?:the\s+)?(?:enter|return)(?:\s+key)?|send\s+it|and\s+send|submit\s+it)[.!]?\s*$", I);
    static readonly Regex LeadFiller = new(@"^\s*(?:please|can\s+you|could\s+you|would\s+you|will\s+you|can\s+u|kindly|just|go\s+ahead\s+and|i\s+want\s+you\s+to|i\s+would\s+like\s+you\s+to|i'?d\s+like\s+you\s+to|i\s+need\s+you\s+to|hey|ok|okay|so|um+|uh+|now|quickly|jarvis)\b[\s,.]*", I);
    static readonly Regex TrailFiller = new(@"[\s,]*(?:please|for\s+me|right\s+now|now|thanks|thank\s+you|jarvis|quickly)[.!?]*\s*$", I);

    /// <summary>Everything said after the wake word, in order.</summary>
    public static List<Cmd> Parse(string said, DateTime now)
    {
        var result = new List<Cmd>();
        foreach (var part in Split(said))
        {
            var cmd = ParseOne(part, now);
            if (cmd != null) result.Add(cmd);
        }
        if (result.Count == 0) result.Add(new Cmd("unknown", said.Trim()));
        return result;
    }

    /// <summary>"open chrome and search for cats" → ["open chrome", "search for cats"].</summary>
    public static List<string> Split(string said)
    {
        var parts = new List<string>();
        string rest = said.Trim();
        // Whisper often ends with a full stop; sentences in the middle can be commands too.
        rest = Regex.Replace(rest, @"(?<=\w)\.\s+(?=[A-Za-z])", ", ");
        while (rest.Length > 0)
        {
            var stripped = StripLead(rest);
            if (stripped.Length == 0) { parts.Add(rest); break; } // just "okay" / "jarvis": keep it
            rest = stripped;
            if (WholeStart.IsMatch(rest))
            {
                // "ask Claude to …" and "message Rahul saying …": everything after is the text,
                // even if it contains "and open…".
                parts.Add(rest);
                break;
            }
            if (TypeStart.IsMatch(rest))
            {
                // Everything after "type" is the text, except a final "and press enter".
                var tail = TypeTail.Match(rest);
                if (tail.Success && tail.Index > 0)
                {
                    parts.Add(rest[..tail.Index]);
                    parts.Add("press enter");
                }
                else parts.Add(rest);
                break;
            }
            var m = Splitter2.Match(rest, 1);
            if (!m.Success) { parts.Add(rest); break; }
            parts.Add(rest[..m.Index]);
            rest = rest[(m.Index + m.Length)..];
        }
        return parts.Select(p => p.Trim().TrimEnd('.', ',', '!')).Where(p => p.Length > 0).ToList();
    }

    static string StripLead(string s)
    {
        string before;
        do
        {
            before = s;
            s = LeadFiller.Replace(s, "", 1);
        } while (s != before);
        return s.Trim();
    }

    /// <summary>Lowercase, no punctuation (except in times, numbers and web addresses), common spellings fixed.</summary>
    public static string Norm(string s)
    {
        s = s.ToLowerInvariant().Replace('’', '\'').Replace("'", "");
        s = s.Replace("%", " percent ");
        s = Regex.Replace(s, @"(?<!\d):|:(?!\d)", " ");
        s = Regex.Replace(s, @"\.(?!\w)|(?<!\w)\.", " ");
        s = Regex.Replace(s, "[,!?;\"“”()]", " ");
        s = Regex.Replace(s, @"\bwi\s*-?\s*fi\b", "wifi");
        s = Regex.Replace(s, "[®™©]", "");
        s = Regex.Replace(s, @"\bscreen\s+shot\b", "screenshot");
        s = Regex.Replace(s, @"\byou\s+tube\b", "youtube");
        s = Regex.Replace(s, @"\bblue\s+tooth\b", "bluetooth");
        s = Regex.Replace(s, @"\be-mail\b", "email");
        s = Regex.Replace(s, @"\bvs\s+code\b", "vscode");
        s = Regex.Replace(s, @"\bwhat\s+is\b", "whats");
        s = Regex.Replace(s, @"\bwhat\s+are\b", "whats");
        s = Regex.Replace(s, @"\b(\w+)\s*-\s*(\w+)\b", m => m.Groups[1].Value + " " + m.Groups[2].Value);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    // ---------------- one command ----------------

    sealed record Rule(Regex Re, Func<Match, string, Cmd?> Make);
    static readonly List<Rule> Rules = BuildRules();

    static Cmd? ParseOne(string original, DateTime now)
    {
        string orig = original.Trim();

        // "type / send hi to Abhishek on WhatsApp" is a WhatsApp message, not typing.
        var wa = Regex.Match(orig.TrimEnd('.', '!'), @"^\s*(?:type|write|send|text)\s+(?<msg>.+?)\s+to\s+(?<who>[\w ]{2,30}?)\s+(?:on|in|via|through)\s+whats\s?app$", I);
        if (wa.Success) return new Cmd("whatsapp-msg", wa.Groups["who"].Value.Trim(), Say: wa.Groups["msg"].Value.Trim());

        // Typing keeps your exact words, capitals and punctuation.
        if (TypeStart.IsMatch(orig))
        {
            var text = TypeStart.Replace(orig, "", 1).Trim();
            if (text.EndsWith('.') && !text.EndsWith("..")) text = text[..^1];
            text = Regex.Replace(text, @"\s*\bnew line\b\s*", "\n", I);
            text = Regex.Replace(text, @"\s*\bnew paragraph\b\s*", "\n\n", I);
            return text.Length == 0 ? null : new Cmd("type", text, Say: "Typed");
        }

        // "play cry for me" is a song title: keep "for me" after play / search.
        var filler = Regex.IsMatch(orig, @"^\W*(?:play|search|google|look up|find|youtube)\b", I)
            ? Regex.Replace(orig, @"[\s,]*(?:please|right\s+now|thanks|thank\s+you|jarvis)[.!?]*\s*$", "", I)
            : TrailFiller.Replace(orig, "");
        var trimmed = StripLead(filler);
        if (trimmed.Length > 0) orig = trimmed;
        var s = Norm(orig);
        if (s.Length == 0) return null;

        // Reminders and deadlines need dates parsed relative to now.
        var reminder = ParseReminder(s, orig, now);
        if (reminder != null) return reminder;

        if (NamedShortcuts.TryGetValue(s, out var sc)) return new Cmd("keys", Keys: sc.Keys, Say: sc.Say);
        foreach (var (re, keys, say) in PatternShortcuts)
        {
            var m = re.Match(s);
            if (!m.Success) continue;
            var k = keys(m);
            if (k.Length > 0) return new Cmd("keys", Keys: k, Say: say(m));
        }

        foreach (var rule in Rules)
        {
            var m = rule.Re.Match(s);
            if (!m.Success) continue;
            var cmd = rule.Make(m, orig);
            if (cmd != null) return cmd;
        }

        // Just a name ("spotify", "youtube"): open it if it's an app or a site.
        if (s.Split(' ').Length <= 4) return new Cmd("bare", s);
        return new Cmd("unknown", orig);
    }

    // ---------------- shortcuts ----------------

    static Dictionary<string, (int[] Keys, string Say)> BuildNamed()
    {
        var d = new Dictionary<string, (int[], string)>();
        void Add(string phrases, string say, params int[] keys)
        {
            foreach (var p in phrases.Split('|')) d[p] = (keys, say);
        }

        // tabs and browsing
        Add("new tab|open new tab|open a new tab|open tab|open a tab|open our tab|open another tab|another tab|make a new tab|create new tab", "New tab", Ctrl, K('T'));
        Add("close tab|close this tab|close the tab|close current tab|close that tab|kill tab|close the current tab", "Closed tab", Ctrl, K('W'));
        Add("reopen tab|reopen the tab|reopen closed tab|reopen last tab|reopen the last tab|restore tab|restore the tab|restore closed tab|restore last tab|bring back tab|bring back the tab|bring back that tab|undo close tab|open closed tab|open last closed tab|reopen that tab", "Reopened tab", Ctrl, Shift, K('T'));
        Add("next tab|switch tab|go to next tab|tab right|move to next tab", "Next tab", Ctrl, Tab);
        Add("previous tab|prev tab|go to previous tab|tab left|go back a tab", "Previous tab", Ctrl, Shift, Tab);
        Add("last tab|go to last tab|go to the last tab", "Last tab", Ctrl, K('9'));
        Add("new window|open new window|open a new window", "New window", Ctrl, K('N'));
        Add("incognito|incognito window|new incognito window|open incognito|open incognito window|go incognito|private window|new private window|open private window|incognito tab|new incognito tab", "Incognito", Ctrl, Shift, K('N'));
        Add("refresh|reload|refresh page|refresh the page|reload page|reload the page|refresh this|reload this|refresh it|reload it|refresh this page|reload this page", "Refreshed", 0x74);
        Add("hard refresh|hard reload|force refresh|force reload", "Hard refresh", Ctrl, 0x74);
        Add("go back|back|previous page|go to previous page|go back a page|back page", "Back", Alt, Left);
        Add("go forward|forward|next page|go forward a page", "Forward", Alt, Right);
        Add("zoom in|make it bigger|bigger text|increase zoom|enlarge", "Zoom in", Ctrl, Plus);
        Add("zoom out|make it smaller|smaller text|decrease zoom", "Zoom out", Ctrl, Minus);
        Add("reset zoom|zoom reset|actual size|normal zoom|zoom to 100 percent|zoom 100 percent", "Zoom reset", Ctrl, K('0'));
        Add("full screen|fullscreen|go full screen|make it full screen|full screen mode|enter full screen|toggle full screen", "Full screen", 0x7A);
        Add("exit full screen|leave full screen|exit fullscreen|get out of full screen", "Exit full screen", Esc);
        Add("bookmark|bookmark this|bookmark this page|bookmark page|save bookmark|add bookmark", "Bookmarked", Ctrl, K('D'));
        Add("history|open history|show history|browser history|show my history", "History", Ctrl, K('H'));
        Add("downloads page|open downloads page|show downloads page|browser downloads", "Downloads page", Ctrl, K('J'));
        Add("address bar|go to address bar|focus address bar|select address bar|url bar|go to url bar", "Address bar", Ctrl, K('L'));
        Add("find|find on page|find in page|search this page|search on this page|find in this page", "Find", Ctrl, K('F'));
        Add("dev tools|developer tools|open dev tools|open developer tools|inspect|inspect element", "Dev tools", 0x7B);
        Add("view source|page source|show page source", "Source", Ctrl, K('U'));
        Add("clear browsing data|clear history|clear browser history", "Clear data", Ctrl, Shift, Del);

        // editing
        Add("copy|copy that|copy this|copy it|copy selection|copy the selection|copy text|copy the text", "Copied", Ctrl, K('C'));
        Add("paste|paste it|paste that|paste this|paste here|paste text", "Pasted", Ctrl, K('V'));
        Add("paste without formatting|paste plain text|paste as plain text", "Pasted plain", Ctrl, Shift, K('V'));
        Add("cut|cut that|cut this|cut it|cut selection", "Cut", Ctrl, K('X'));
        Add("undo|undo that|undo it|undo this|undo last|undo the last thing|ctrl z|control z", "Undo", Ctrl, K('Z'));
        Add("redo|redo that|redo it|ctrl y|control y", "Redo", Ctrl, K('Y'));
        Add("select all|select everything|highlight all|highlight everything|select all text|select the whole thing", "Selected all", Ctrl, K('A'));
        Add("save|save it|save this|save file|save the file|save that|save my work|save document|save the document", "Saved", Ctrl, K('S'));
        Add("save as|save it as|save file as|save this as", "Save as", Ctrl, Shift, K('S'));
        Add("print|print this|print it|print page|print the page|print document", "Print", Ctrl, K('P'));
        Add("bold|make it bold|bold that|make that bold|bold text", "Bold", Ctrl, K('B'));
        Add("italic|italics|make it italic|italicize|italicize that|make that italic", "Italic", Ctrl, K('I'));
        Add("underline|underline that|underline it|make it underlined", "Underline", Ctrl, K('U'));
        Add("enter|press enter|hit enter|return|press return|send|send it|submit|submit it|new line|next line|go to next line|line break", "Enter", Enter);
        Add("escape|press escape|hit escape|esc|press esc", "Escape", Esc);
        Add("tab key|press tab|hit tab", "Tab", Tab);
        Add("space|press space|hit space|spacebar|press spacebar|space bar", "Space", Space);
        Add("delete|delete that|delete this|delete it|press delete|remove that|delete selection", "Deleted", Del);
        Add("backspace|back space|press backspace|hit backspace|erase|erase that", "Backspace", Back);
        Add("delete word|delete last word|delete the last word|delete previous word|remove last word|erase last word|erase word", "Deleted word", Ctrl, Back);
        Add("delete line|delete this line|delete the line|clear line|clear this line", "Deleted line", Home, Shift, End, Del);
        Add("go to start|go to the start|go to beginning|go to the beginning|start of line|beginning of line|home key|press home", "Start", Home);
        Add("go to end|go to the end|end of line|end key|press end", "End", End);
        Add("rename|rename it|rename this|rename file|rename that", "Rename", 0x71);
        Add("properties|show properties|open properties", "Properties", Alt, Enter);
        Add("new folder|create new folder|make a new folder|create a folder|make new folder|create folder", "New folder", Ctrl, Shift, K('N'));
        Add("open file|open a file|open file dialog", "Open file", Ctrl, K('O'));
        Add("new file|new document|create new file|create a new file|new doc", "New file", Ctrl, K('N'));
        Add("comment line|comment this line|comment out|toggle comment|comment that", "Comment", Ctrl, Slash);
        Add("format document|format code|format the code|format this|format file", "Format", Shift, Alt, K('F'));
        Add("command palette|open command palette|show command palette", "Command palette", Ctrl, Shift, K('P'));
        Add("quick open|go to file", "Quick open", Ctrl, K('P'));
        Add("open terminal in vscode|toggle terminal|show terminal|open the terminal panel", "Terminal", Ctrl, 0xC0);

        // windows and desktop
        Add("show desktop|show the desktop|go to desktop|go to the desktop|minimize everything|minimize all|minimise everything|minimise all|hide everything|hide all windows|hide all|clear the screen|desktop", "Desktop", Win, K('D'));
        Add("snap left|snap to left|snap to the left|move this to the left|move window left|move it left|put this on the left|move window to the left|left side|snap window left", "Snapped left", Win, Left);
        Add("snap right|snap to right|snap to the right|move this to the right|move window right|move it right|put this on the right|move window to the right|right side|snap window right", "Snapped right", Win, Right);
        Add("switch window|switch windows|switch app|switch apps|alt tab|next window|previous window|previous app|last app|go to last app|switch to last app|switch to previous app|go back to last app", "Switched", Alt, Tab);
        Add("task view|show all windows|show windows|all windows|show me all windows|open task view|timeline", "Task view", Win, Tab);
        Add("new desktop|create new desktop|new virtual desktop|create a desktop|add desktop|add a desktop", "New desktop", Win, Ctrl, K('D'));
        Add("next desktop|switch desktop|desktop right|go to next desktop", "Next desktop", Win, Ctrl, Right);
        Add("previous desktop|desktop left|go to previous desktop|last desktop", "Previous desktop", Win, Ctrl, Left);
        Add("close desktop|close this desktop|remove desktop", "Closed desktop", Win, Ctrl, 0x73);
        Add("start menu|open start|open start menu|show start menu|windows menu|open the start menu|press windows key|windows key", "Start", Win);
        Add("notifications|open notifications|show notifications|notification center|action center|open notification center|show my notifications|open action center", "Notifications", Win, K('N'));
        Add("quick settings|open quick settings|show quick settings|control center windows", "Quick settings", Win, K('A'));
        Add("emoji|emojis|emoji panel|emoji keyboard|emoji picker|open emoji|open emoji panel|open emojis|show emojis|insert emoji", "Emoji", Win, Period);
        Add("clipboard|clipboard history|open clipboard|open clipboard history|show clipboard|show clipboard history|paste history", "Clipboard", Win, K('V'));
        Add("task manager|open task manager|show task manager|start task manager|launch task manager", "Task Manager", Ctrl, Shift, Esc);
        Add("run|run dialog|run box|open run|open run dialog|open the run box", "Run", Win, K('R'));
        Add("voice typing|dictation|start dictation|start voice typing|windows dictation", "Voice typing", Win, K('H'));
        Add("game bar|xbox game bar|open game bar|show game bar", "Game Bar", Win, K('G'));
        Add("project|project screen|second screen|display mode|duplicate screen|extend screen|change display mode", "Project", Win, K('P'));
        Add("windows search|open search|open windows search|search windows|search my computer|search my pc", "Search", Win, K('S'));
        Add("magnifier|magnifier on|open magnifier|turn on magnifier|zoom in screen", "Magnifier", Win, Plus);
        Add("magnifier off|close magnifier|turn off magnifier", "Magnifier off", Win, Esc);
        Add("file explorer|open file explorer|explorer|open explorer|open my files|my files|open files|this pc|open this pc|my computer|open my computer", "File Explorer", Win, K('E'));
        Add("windows settings|open windows settings|pc settings|open pc settings|settings|open settings|system settings|open system settings|computer settings|laptop settings", "Settings", Win, K('I'));
        Add("screenshot|take screenshot|take a screenshot|take a screen shot|screen shot|snip|snip it|snip the screen|capture screen|capture the screen|screen capture|grab the screen|clip the screen|snipping|take a snip|screenshot this", "Screenshot", Win, Shift, K('S'));
        Add("full screenshot|full screen screenshot|screenshot everything|screenshot the whole screen|print screen|save screenshot|screenshot whole screen|take a full screenshot", "Saved screenshot", Win, PrtSc);
        Add("screen record|record screen|record my screen|start screen recording|record the screen|screen recording", "Screen record", Win, Shift, K('R'));
        Add("close all windows|close everything", "Closed", Alt, 0x73);
        Add("lock keyboard shortcuts|show keyboard shortcuts", "Shortcuts", Ctrl, K('/'));
        return d;
    }

    static readonly Dictionary<string, (int[] Keys, string Say)> NamedShortcuts = BuildNamed();

    static readonly (Regex Re, Func<Match, int[]> Keys, Func<Match, string> Say)[] PatternShortcuts =
    {
        // "go to tab 3", "tab three", "third tab"
        (new Regex(@"^(?:go to |switch to |open )?(?:the )?(?:tab (?:number )?(\w+)|(\w+) tab)$", I),
            m => TabNumber(m) is int n ? Keys(Ctrl, '0' + n) : Array.Empty<int>(),
            m => $"Tab {TabNumber(m)}"),
    };

    static int? TabNumber(Match m)
    {
        var w = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        var n = Number(w) ?? Ordinal(w);
        return n is >= 1 and <= 9 ? n : null;
    }

    // ---------------- every other command ----------------

    const string Num = @"(\d+(?:\.\d+)?|(?:(?:zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fourty|fifty|sixty|seventy|eighty|ninety|hundred|a hundred|one hundred)(?:[ -](?:one|two|three|four|five|six|seven|eight|nine))?))";
    const string Units = @"(?:percent|per cent|points?|%|notches|times|steps)";
    const string Sites = @"youtube|google|amazon|flipkart|wikipedia|wiki|github|maps|google maps|reddit|stack overflow|stackoverflow|images|google images|pictures|spotify|linkedin|twitter|x|myntra|chatgpt|chat gpt|perplexity|leetcode|news|google news|scholar|google scholar|bing|duckduckgo|instagram|pinterest|quora|imdb|netflix|prime video|hotstar|swiggy|zomato|npm|pypi|stack|geeksforgeeks|geeks for geeks|w3schools|medium|dev to|hugging face|kaggle|internshala|unstop|naukri|indeed|udemy|coursera";

    static List<Rule> BuildRules()
    {
        var r = new List<Rule>();
        void Add(string pattern, Func<Match, string, Cmd?> make) => r.Add(new Rule(new Regex("^(?:" + pattern + ")$", I), make));

        // ----- the island and Jarvis itself -----
        Add(@"(?:hide|pause|close|minimi[sz]e) (?:the )?(?:island|dynamic island|dyisd)|go away island", (_, _) => new Cmd("island", "hide"));
        Add(@"(?:show|unhide|resume|bring back|open) (?:the )?(?:island|dynamic island|dyisd)(?: again| back)?|bring (?:it|that|the island|the music|the timer|everything) back|bring back (?:it|that|everything|what i hid|the hidden|hidden)|show (?:it|that|everything|what i hid|hidden(?: stuff)?) again|unhide(?: it| that| everything)?|undo (?:the )?(?:flick|hide)|show hidden(?: stuff)?",
            (_, _) => new Cmd("island", "show"));
        Add(@"(?:move|put|snap) (?:the )?island (?:to )?(?:the )?(left|right|center|centre|middle)(?: side)?", (m, _) => new Cmd("island", m.Groups[1].Value is "left" or "right" ? m.Groups[1].Value : "center"));
        Add(@"(?:open |show )?(?:the |your )?(?:control center|control centre|island settings|dyisd settings|jarvis settings|your settings)", (_, _) => new Cmd("island", "settings"));
        Add(@"stop listening|go to sleep|sleep jarvis|jarvis sleep|go sleep|be quiet|shut up|stop listening for (?:your name|jarvis|me)|dont listen|stop hearing", (_, _) => new Cmd("jarvis", "sleep"));
        Add(@"start listening|wake up|listen (?:for|to) (?:me|your name|jarvis)|keep listening", (_, _) => new Cmd("jarvis", "wake"));
        Add(@"(?:help|what can you do|what can i say|what do you do|commands|show commands|list commands|what are your commands|how do i use you)", (_, _) => new Cmd("help"));
        Add(@"(?:thank you|thanks|thank you so much|thanks a lot|good job|nice|great|awesome|perfect|cool)(?: jarvis)?", (_, _) => new Cmd("thanks"));
        Add(@"(?:hello|hi|hey|hey there|hi there|good morning|good afternoon|good evening|yo|sup|whats up)(?: jarvis)?", (_, _) => new Cmd("hello"));
        Add(@"(?:how are you|how are you doing|hows it going|you good)", (_, _) => new Cmd("howareyou"));
        Add(@"(?:who are you|what are you|whats your name|introduce yourself)", (_, _) => new Cmd("whoami"));
        Add(@"(?:never ?mind|cancel|cancel that|forget it|forget that|nothing|no|nope|ignore that|ignore|abort)", (_, _) => new Cmd("cancel"));
        Add(@"(?:yes|yeah|yep|yup|sure|ok|okay|do it|confirm|confirmed|go ahead|ok do it|okay do it|affirmative|of course|yes please|correct)", (_, _) => new Cmd("yes"));

        // ----- calls -----
        Add(@"(?:answer|pick up|accept|take|receive|attend)(?: (?:the|it|this|that))?(?: (?:call|phone))?(?: it)?", (_, _) => new Cmd("answer"));
        Add(@"(?:decline|reject|ignore|cut|dismiss|refuse)(?: (?:the|it|this|that))? (?:call|phone)|decline|reject|decline it|reject it", (_, _) => new Cmd("decline"));
        Add(@"(?:hang up|end (?:the )?call|leave (?:the )?call|disconnect(?: the call)?|cut the call)", (_, _) => new Cmd("hangup"));
        Add(@"(?:mute|unmute|toggle mute on) (?:me|my mic|my microphone|the mic|the microphone|mic|microphone|myself|my voice)|(?:mute|unmute) (?:me )?(?:in|on) (?:the )?(?:call|discord|whatsapp|meeting)|mic (?:on|off)|microphone (?:on|off)",
            (m, _) => new Cmd("mic", m.Value.Contains("unmute") || m.Value.EndsWith(" on") ? "unmute" : "mute"));
        // ----- Discord -----
        // Whisper often writes "deafen" as "defend", "deaf in", "death in", "the fan".
        Add(@"(?:un ?)?(?:deafen|deafin|deafan|defend|defen|defin|defined|deaf in|deaf and|death in|deafening)(?: me| myself| it)?(?: (?:in|on) discord)?|(?:toggle |turn (?:on|off) )?(?:deafen|deaf mode)|(?:deafen|defend) (?:me|myself)|(?:mute|turn off) (?:discord audio|discord sound|everyone|all voices)",
            (_, _) => new Cmd("discord", "deafen"));
        Add(@"(?:leave|exit|quit)(?: the)? (?:voice|vc|voice chat|voice channel|call|channel|discord call)(?: (?:on|in) discord)?", (_, _) => new Cmd("hangup"));
        Add(@"join(?: (?:the|a|my))?(?: (.+?))?",
            (m, _) =>
            {
                var ch = Regex.Replace(" " + m.Groups[1].Value + " ", @"\b(voice|vc|chat|channel|call|room|on|in|and|at|discord|the|server)\b", " ").Trim();
                return new Cmd("discord-join", Regex.Replace(ch, @"\s+", " "));
            });
        Add(@"(?:call|ring|voice call) (.+?) (?:on|in|via|using|through) discord|discord call (.+)", (m, _) => new Cmd("discord-call", First(m)));
        Add(@"(?:open|go to|get in|get into|hop in|hop into|enter) (?:the )?(?:voice|vc|voice chat|voice channel)(?: (?:on|in) discord)?", (_, _) => new Cmd("discord-join", ""));

        // ----- WhatsApp -----
        Add(@"(?:video call|voice call|call|ring|phone|facetime) (.+?) (?:on|in|via|using|through) whatsapp|whatsapp (?:video |voice )?call (?:to )?(.+)",
            (m, _) => new Cmd("whatsapp-call", First(m), N: m.Value.Contains("video") ? 1 : 0));
        Add(@"(?:send|write|text|message|msg|whatsapp|ping|tell) .+", (_, o) => WhatsAppMessage(o));
        // "call Abhishek" with no app: WhatsApp (it always asks first)
        Add(@"(?:call|ring|phone|video call|voice call) (?!it\b|that\b|this\b|me\b|off\b|back\b|him\b|her\b|them\b)(.{2,30})",
            (m, _) => new Cmd("whatsapp-call", m.Groups[1].Value, N: m.Value.StartsWith("video") ? 1 : 0));

        // ----- Claude -----
        Add(@"(?:ask|tell) claude.*|(?:new|start a new|start new|open a new|open new|start a) (?:claude )?(?:chat|session|conversation)(?: (?:in|on|with) claude)?.*",
            (_, o) => AskClaude(o));
        // "play my chill vibes playlist", "play playlist gym on apple music"
        Add(@"(?:play|start|put on|shuffle) (?:my |the |our )?(?:playlist (?:called |named )?(.+?)|(.+?) playlist)(?: (?:on|in|from) (?:apple music|music|itunes))?",
            (m, _) => new Cmd("applemusic-playlist", First(m)));
        // "play apple music", "play a song from apple music": start the app and press play.
        Add(@"(?:play|start|put on|open and play)(?: (?:some|a|my|the))? (?:apple music|music app|the music app|itunes|spotify|my music)|(?:play|start|put on)(?: (?:some|a|any|my|the))? (?:song|songs|music|track)s? (?:on|in|from|with) (?:apple music|music|itunes|spotify|the music app)",
            (m, _) => new Cmd("play-app", m.Value.Contains("spotify") ? "spotify" : "apple music"));
        // "play believer on apple music": search Apple Music and play the first result.
        Add(@"play (.+?)(?: (?:from|in|on) (?:my )?(?:library|songs|music|collection|playlist))? (?:on|in|from|with|using) (?:apple music|apple|apples music|app music|apple music app|music|itunes|i tunes|the music app)(?: (?:from|in) (?:my )?(?:library|songs|music|collection))?|play (.+?) (?:from|in) my (?:library|songs|music|collection)",
            (m, _) => new Cmd("applemusic", Regex.Replace(First(m), @"\s+(?:from|in|on) (?:my )?(?:library|songs|music|collection)$", "")));

        // ----- questions Jarvis answers itself -----
        Add(@"(?:whats|tell me|say) (?:the )?time(?: (?:now|right now|is it|it is))?|what time is it(?: now| right now)?|time(?: now| please| check)?|current time|what time it is|whats the time now|time right now",
            (_, _) => new Cmd("time"));
        Add(@"(?:whats|tell me) (?:the |todays )?date(?: today)?|what day is (?:it|today)(?: today)?|todays date|date(?: today)?|whats today|which day is (?:it|today)|what date is (?:it|today)|whats the day today|what is the day",
            (_, _) => new Cmd("date"));
        Add(@"(?:how much )?battery(?: level| left| percentage| status| life| remaining)?|how much (?:charge|battery|juice)(?: is left| do i have| left| remaining)?|whats (?:the |my )?battery(?: level| percentage| at| status)?|battery check|check (?:the |my )?battery|how is (?:the |my )?battery|hows (?:the |my )?battery",
            (_, _) => new Cmd("battery"));
        Add(@"whats (?:playing|this song|the song|this track|this|the name of this song|this music|this video)(?: playing| called| now)?|(?:what|which) song is (?:this|playing|that)|now playing|whats on|who sings this|name (?:of )?(?:this|the) song|what am i listening to",
            (_, _) => new Cmd("playing"));
        Add(@"whats next|what do i have(?: today| next| due| coming up| this week)?|(?:any|my) (?:deadlines|reminders)(?: today| this week| coming up)?|(?:show|list|tell me|read) (?:my |the |all )?(?:deadlines|reminders|upcoming|schedule|assignments)|whats due(?: today| next| this week)?|upcoming(?: deadlines)?|whats (?:on )?my schedule|whats coming up|next deadline|whats my next deadline|do i have anything (?:due|today|tomorrow)",
            (_, _) => new Cmd("deadlines"));

        // ----- timers -----
        Add(@"(?:stop|end|cancel|kill|finish|clear|remove|turn off|delete) (?:the |my )?(?:focus|timer|focus session|focus mode|countdown|focus timer|session)(?: session| timer| mode)?",
            (_, _) => new Cmd("timer-stop"));
        Add(@"(?:pause|resume|continue|unpause) (?:the |my )?(?:focus|timer|focus session|countdown|focus timer)", (_, _) => new Cmd("timer-pause"));
        Add(@"(?:add|give me|extend(?: (?:the )?(?:timer|focus))? by|extend|plus) (?:5|five) (?:more )?minutes?(?: more)?(?: to (?:the )?(?:timer|focus))?|(?:5|five) more minutes", (_, _) => new Cmd("timer-add"));
        Add(@"(?:start|begin|enter|turn on|activate) (?:a |my |the )?focus(?: session| mode| timer| time)?(?: for (.+))?|focus(?: mode| time)?(?: on)?(?: for (.+))?|lets focus(?: for (.+))?|(?:start|begin) (?:a )?(?:pomodoro|study session|work session)(?: for (.+))?|pomodoro|study mode|study time",
            (m, _) =>
            {
                var g = new[] { 1, 2, 3, 4 }.Select(i => m.Groups[i]).FirstOrDefault(x => x.Success);
                int secs = g == null ? 0 : Duration(g.Value) ?? -1;
                return secs < 0 ? null : new Cmd("timer", "Focus", secs);
            });
        Add(@"(?:set|start|create|put|make|begin|run)(?: me)? (?:a |an |the )?(?:timer|countdown|alarm)(?: for| of| on)? (.+)|(?:set |start |create )?(?:a |an )?(.+?) (?:timer|countdown)|timer (?:for )?(.+)|count ?down (?:from |for )?(.+)",
            (m, _) =>
            {
                var g = new[] { 1, 2, 3, 4 }.Select(i => m.Groups[i]).First(x => x.Success);
                var secs = Duration(g.Value);
                return secs is > 0 ? new Cmd("timer", "Timer", secs.Value) : null;
            });
        Add(@"(?:open |show |start |use )?(?:the )?(?:stopwatch|stop watch)", (_, _) => new Cmd("open", "clock", Say: "Clock"));
        Add(@"(?:set|start|create|make)(?: me)? (?:a |an |the )?(?:timer|countdown|alarm)|timer", (_, _) => new Cmd("ask", "How long? Say \u201Cset a timer for 5 minutes\u201D"));

        // ----- volume -----
        Add($@"(?:(?:set|change|put|make|turn|adjust|bring) )?(?:the )?(?:volume|sound|audio)(?: level)?(?: (?:to|at|on))? {Num}(?: {Units})?|(?:volume|sound) {Num}(?: {Units})?",
            (m, _) => Number(First(m)) is int v ? new Cmd("volume-set", N: Math.Clamp(v, 0, 100)) : null);
        Add($@"(?:(?:set|turn|put) (?:the )?)?(?:volume|sound) (?:to )?(?:max|maximum|full|all the way up|hundred|highest)|(?:max|maximum|full|highest) (?:volume|sound)|(?:turn|crank|pump|blast) (?:it|the volume|the sound|the music|volume|music) (?:all the way )?up (?:to )?(?:max|full|maximum|all the way)|max it out|full blast",
            (_, _) => new Cmd("volume-set", N: 100));
        Add(@"(?:half|50 percent) (?:volume|sound)|(?:volume|sound) (?:to )?half(?: way)?|(?:set |put )?(?:the )?volume (?:to |at )?half",
            (_, _) => new Cmd("volume-set", N: 50));
        Add(@"(?:volume|sound) (?:to )?(?:zero|minimum|min|lowest)|(?:minimum|lowest) volume|no (?:volume|sound)",
            (_, _) => new Cmd("volume-set", N: 0));
        Add($@"(?:(?:turn|crank|pump|bring|push|make) (?:it|the volume|the sound|the music|volume|sound|music|the audio|this) up|(?:volume|sound|audio)(?: level)? up|increase (?:the )?(?:volume|sound|audio)|raise (?:the )?(?:volume|sound)|louder|(?:make it|a bit|little|lil|bit|much) louder|more (?:volume|sound)|turn up(?: the)?(?: volume| sound| music| it)?|i cant hear(?: anything| it)?|up the volume|higher volume|volume higher|boost (?:the )?volume)(?: (?:by|to) {Num}(?: {Units})?)?(?: (a lot|a little|a bit|little|bit|slightly|more))?",
            (m, _) => new Cmd("volume-step", N: Step(m, 10)));
        Add($@"(?:(?:turn|bring|make) (?:it|the volume|the sound|the music|volume|sound|music|the audio|this) down|(?:volume|sound|audio)(?: level)? down|decrease (?:the )?(?:volume|sound|audio)|lower (?:the )?(?:volume|sound|audio|it|music)|reduce (?:the )?(?:volume|sound)|quieter|softer|(?:make it|a bit|little|lil|bit|much) (?:quieter|softer)|less (?:volume|sound)|turn down(?: the)?(?: volume| sound| music| it)?|too loud|its too loud|lower volume|volume lower|down the volume)(?: (?:by|to) {Num}(?: {Units})?)?(?: (a lot|a little|a bit|little|bit|slightly|more))?",
            (m, _) => new Cmd("volume-step", N: -Step(m, 10)));
        Add(@"(?:mute|silence|quiet|shush)(?: (?:the )?(?:volume|sound|audio|speakers?|computer|pc|laptop|everything|it|all|music|media|system))?|turn (?:the )?(?:sound|volume|audio) off|turn off (?:the )?(?:sound|volume|audio)|silent mode(?: on)?|(?:turn on|enable) silent(?: mode)?|go silent|no sound",
            (_, _) => new Cmd("mute", "mute"));
        Add(@"unmute(?: (?:the )?(?:volume|sound|audio|speakers?|computer|pc|laptop|everything|it|all|music|media|system))?|turn (?:the )?(?:sound|volume|audio) (?:back )?on|turn on (?:the )?(?:sound|volume|audio)|silent mode off|(?:turn off|disable) silent(?: mode)?|sound on|ring mode",
            (_, _) => new Cmd("mute", "unmute"));

        // ----- brightness -----
        Add($@"(?:(?:set|change|put|make|turn|adjust) )?(?:the )?(?:screen )?(?:brightness|display brightness)(?: level)?(?: (?:to|at))? {Num}(?: {Units})?|brightness {Num}(?: {Units})?",
            (m, _) => Number(First(m)) is int v ? new Cmd("brightness-set", N: Math.Clamp(v, 0, 100)) : null);
        Add(@"(?:max|maximum|full) brightness|brightness (?:to )?(?:max|maximum|full|hundred)|(?:make (?:the screen|it) )?as bright as possible",
            (_, _) => new Cmd("brightness-set", N: 100));
        Add(@"(?:min|minimum|lowest) brightness|brightness (?:to )?(?:min|minimum|lowest|zero)|(?:make (?:the screen|it) )?as dim as possible",
            (_, _) => new Cmd("brightness-set", N: 0));
        Add($@"(?:(?:turn|bring|make) (?:the )?(?:brightness|screen) up|(?:screen )?brightness up|increase (?:the )?(?:screen )?brightness|raise (?:the )?brightness|brighter|(?:make (?:it|the screen) )?brighter|more brightness|brighten(?: (?:it|the screen|up))?|its too dark|too dark|i cant see)(?: (?:by|to) {Num}(?: {Units})?)?(?: (a lot|a little|a bit|little|bit|slightly|more))?",
            (m, _) => new Cmd("brightness-step", N: Step(m, 15)));
        Add($@"(?:(?:turn|bring|make) (?:the )?(?:brightness|screen) down|(?:screen )?brightness down|decrease (?:the )?(?:screen )?brightness|lower (?:the )?brightness|reduce (?:the )?brightness|dimmer|dim(?: (?:it|the screen|the display|down|the brightness|brightness|the light))?|(?:make (?:it|the screen) )?(?:dimmer|darker)|less brightness|too bright|its too bright)(?: (?:by|to) {Num}(?: {Units})?)?(?: (a lot|a little|a bit|little|bit|slightly|more))?",
            (m, _) => new Cmd("brightness-step", N: -Step(m, 15)));

        // ----- music & video -----
        Add(@"(?:play|resume|continue|unpause|start)(?: (?:the |my )?(?:music|song|songs|video|it|playback|media|track|audio|playing|again|that))?(?: again)?|play music|music on|play some music|press play",
            (_, _) => new Cmd("media", "play"));
        Add(@"(?:pause|hold|freeze)(?: (?:the |my |this )?(?:music|song|video|it|playback|media|track|audio|that|youtube|this))?|stop (?:the |my |this )?(?:music|song|video|playback|media|track|audio|playing|youtube)|music off|press pause|(?:turn off|switch off|shut off|stop) (?:the )?(?:youtube|music|song|video|apple music|spotify|the music)",
            (_, _) => new Cmd("media", "pause"));
        Add(@"stop|stop it|stop that|stop this", (_, _) => new Cmd("stop"));
        Add(@"(?:next|skip)(?: (?:the |this )?(?:song|track|video|one|this|it|music|ahead))?|play (?:the )?next(?: (?:song|track|video|one))?|(?:go to|change) (?:the )?next (?:song|track|video)|change (?:the |this )?(?:song|track)|skip (?:this|it)|another song|different song",
            (_, _) => new Cmd("media", "next"));
        Add(@"(?:previous|prev|last)(?: (?:song|track|video|one))?|play (?:the )?(?:previous|last)(?: (?:song|track|video|one))?|(?:go )?back (?:a|one) (?:song|track|video)|go to (?:the )?previous (?:song|track|video)|(?:play )?(?:the )?song before",
            (_, _) => new Cmd("media", "prev"));
        Add(@"(?:restart|replay)(?: (?:the |this )?(?:song|track|video|it|music))?|(?:play|start) (?:it|this|the song|the video) (?:again|from the (?:start|beginning))|from the (?:top|start|beginning)",
            (_, _) => new Cmd("media", "restart"));
        Add(@"(?:turn (?:on|off) |toggle |enable |disable )?shuffle(?: (?:on|off|mode|it|the songs|songs|music|my music|playlist))?|(?:turn (?:on|off) )?(?:shuffle mode)|mix it up",
            (_, _) => new Cmd("media", "shuffle"));
        Add(@"(?:turn (?:on|off) |toggle |enable |disable )?(?:repeat|loop)(?: (?:on|off|mode|it|this|this song|the song|one|all|the track|this track))?|(?:put|play) (?:it|this|the song) on (?:repeat|loop)|play (?:it|this) again and again",
            (_, _) => new Cmd("media", "repeat"));
        // "play believer": plays the top YouTube video (not just the search page).
        Add(@"play (.+?) (?:on|in|from|with|using) (?:youtube|yt)|(?:youtube|yt) play (.+)|play (.+)",
            (m, o) =>
            {
                var q = First(m);
                bool youtube = Regex.IsMatch(m.Value, @"\b(youtube|yt)\b");
                return q.Length == 0 ? null : new Cmd("yt-play", q, N: youtube ? 1 : 0, Say: $"YouTube · {q}");
            });

        // ----- search -----
        Add($@"(?:search|look up|lookup|find|search for|look for|google)(?: for)? (.+?) (?:on|in|at|using|from|with) ({Sites})",
            (m, _) => SiteSearch(m.Groups[2].Value, m.Groups[1].Value));
        Add($@"(?:search|look up|lookup|find|look|check)(?: on| in)? ({Sites}) (?:for|about) (.+)|({Sites}) search(?: for)? (.+)|search ({Sites}) (.+)",
            (m, _) =>
            {
                var site = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[5].Value;
                var q = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[4].Success ? m.Groups[4].Value : m.Groups[6].Value;
                return SiteSearch(site, q);
            });
        Add(@"(?:youtube|yt) (.+)", (m, _) => SiteSearch("youtube", m.Groups[1].Value));
        // "open friends on prime video", "watch interstellar on netflix (in chrome)"
        Add($@"(?:open|watch|show|show me|find|stream|put on|look for|get) (.+?) (?:on|in|from) (?:the )?({Sites})(?: (?:at|in|on|using) (?:chrome|the browser|browser))?",
            (m, _) => SiteSearch(m.Groups[2].Value, m.Groups[1].Value));
        Add(@"(?:find|search(?: for)?|look for) (.+?) (?:on|in) (?:this|the) page|find on (?:this )?page (.+)", (m, _) => new Cmd("find", First(m), Say: "Find on page"));
        Add(@"(?:images?|pictures?|photos?|pics) of (.+)|show me (?:images?|pictures?|photos?|pics) of (.+)", (m, _) => SiteSearch("images", First(m)));
        Add(@"(?:get |show me |give me )?directions to (.+)|(?:how (?:do i|to|can i) get) to (.+)|route to (.+)",
            (m, _) => new Cmd("url", "https://www.google.com/maps/dir/?api=1&destination=" + Uri.EscapeDataString(First(m)), Say: $"Directions · {First(m)}"));
        Add(@"(?:where is|wheres|show me|find) (.+?) on (?:the )?map|map of (.+)|where is (.+)|wheres (.+)",
            (m, _) => SiteSearch("maps", First(m)));
        Add(@"(?:whats )?(?:the )?weather(?: like)?(?: (?:in|at|for) (.+?))?(?: (today|tomorrow|this week|now|outside))?|(?:is it|will it) (?:going to )?(?:rain|be hot|be cold|be sunny)(?: (?:today|tomorrow))?(?: in (.+))?|(?:hows|how is) the weather(?: (?:in|at) (.+?))?(?: (today|tomorrow))?|temperature(?: (?:in|at|outside) (.+))?|forecast(?: for (.+))?",
            (m, o) => new Cmd("url", Google(m.Value.Contains("weather") || m.Value.Contains("forecast") || m.Value.Contains("temperature") ? m.Value : "weather " + m.Value), Say: "Weather"));
        Add(@"translate (.+?) (?:to|into|in) (\w+)|how (?:do (?:you|i)|to) say (.+?) in (\w+)|whats (.+?) in (\w+)",
            (m, _) =>
            {
                string text = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[5].Value;
                string lang = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[4].Success ? m.Groups[4].Value : m.Groups[6].Value;
                if (!Langs.TryGetValue(lang, out var code)) return m.Groups[5].Success ? null : new Cmd("url", Google($"translate {text} to {lang}"), Say: "Translate");
                return new Cmd("url", $"https://translate.google.com/?sl=auto&tl={code}&text={Uri.EscapeDataString(text)}&op=translate", Say: $"Translate to {lang}");
            });
        Add(@"(?:define|definition of|meaning of|whats the meaning of|what does) (.+?)(?: mean)?", (m, _) => new Cmd("url", Google("define " + m.Groups[1].Value), Say: $"Define {m.Groups[1].Value}"));
        Add(@"(?:whats |calculate |compute |how much is |solve |work out )?(.+)",
            (m, _) =>
            {
                var v = Calc(m.Groups[1].Value);
                return v == null ? null : new Cmd("calc", v.Value.ToString("0.####", CultureInfo.InvariantCulture), Say: m.Groups[1].Value);
            });
        Add(@"(?:search|google|look up|lookup|search for|search up|search the web for|search online for|look online for|web search)(?: for)? (.+)",
            (m, _) => new Cmd("url", Google(m.Groups[1].Value), Say: $"Google · {m.Groups[1].Value}"));

        // ----- scrolling -----
        Add(@"scroll (?:to (?:the )?)?(top|bottom|beginning|end|start|very top|very bottom)(?: of (?:the )?page)?|(?:go|jump|take me) to (?:the )?(?:very )?(top|bottom)(?: of (?:the )?page)?|back to (?:the )?top",
            (m, _) =>
            {
                var w = First(m);
                bool top = w.Length == 0 || w.Contains("top") || w is "beginning" or "start";
                return new Cmd("keys", Keys: Keys(Ctrl, top ? Home : End), Say: top ? "Top" : "Bottom");
            });
        Add($@"(?:scroll|go|move|swipe)(?: the page)? (up|down)(?: (?:by )?{Num}(?: (?:times|notches|steps|pages|lines))?)?(?: (a lot|a little|a bit|little|bit|slightly|more|much|fast|all the way))?|(?:scroll)(?: (?:by )?{Num})?|keep scrolling(?: (up|down))?|scroll more|(?:scroll|go) (?:up|down) more",
            (m, _) =>
            {
                bool up = m.Value.Contains(" up");
                int steps = 5;
                foreach (Group g in m.Groups)
                    if (g.Success && Number(g.Value) is int n && g.Value.Trim().Length > 0 && g.Index > 0) steps = n * 3;
                if (m.Value.Contains("a lot") || m.Value.Contains("much") || m.Value.Contains("fast")) steps = 15;
                if (m.Value.Contains("all the way")) steps = 60;
                if (m.Value.Contains("little") || m.Value.Contains("bit") || m.Value.Contains("slightly")) steps = 2;
                return new Cmd("scroll", N: up ? steps : -steps, Say: up ? "Scrolled up" : "Scrolled down");
            });
        Add(@"page (up|down)|(?:next|previous) page down", (m, _) => new Cmd("keys", Keys: Keys(m.Groups[1].Value == "up" ? PgUp : PgDn), Say: "Page " + m.Groups[1].Value));

        // ----- radios, theme, power -----
        Add(@"(?:turn|switch|toggle|put|set) (on|off) (?:the |my )?(wifi|wireless|internet|bluetooth)|(?:turn|switch|toggle|put|set) (?:the |my )?(wifi|wireless|internet|bluetooth) (on|off)|(wifi|bluetooth|wireless) (on|off)|(enable|disable|connect|disconnect|start|stop) (?:the |my )?(wifi|bluetooth|wireless|internet)|toggle (?:the )?(wifi|bluetooth)",
            (m, _) =>
            {
                string v = m.Value;
                string radio = v.Contains("bluetooth") ? "bluetooth" : "wifi";
                int state = Regex.IsMatch(v, @"\b(on|enable|connect|start)\b") ? 1 : Regex.IsMatch(v, @"\b(off|disable|disconnect|stop)\b") ? 0 : -1;
                return new Cmd("radio", radio, state);
            });
        Add(@"(?:turn (?:on|off) |toggle |enable |disable |switch (?:on |off )?)?(?:airplane|aeroplane|flight) mode(?: (?:on|off))?",
            (m, _) => new Cmd("radio", "all", m.Value.Contains("off") || m.Value.Contains("disable") ? 1 : m.Value.Contains("on") || m.Value.Contains("enable") ? 0 : -1));
        Add(@"(?:turn on |switch to |enable |go |use |set )?(dark|light|night|white)(?: mode| theme)(?: on)?|(?:turn off|disable) (dark|light) (?:mode|theme)|(?:toggle|switch) (?:the )?(?:theme|dark mode|light mode|mode)|(?:make (?:it|windows|the screen|everything) )(dark|darker|light|lighter)",
            (m, _) =>
            {
                string v = m.Value;
                if (v.StartsWith("turn off") || v.StartsWith("disable")) return new Cmd("theme", v.Contains("dark") ? "light" : "dark");
                if (v.Contains("dark") || v.Contains("night")) return new Cmd("theme", "dark");
                if (v.Contains("light") || v.Contains("white")) return new Cmd("theme", "light");
                return new Cmd("theme", "toggle");
            });
        Add(@"(?:turn (?:on|off) |toggle |enable |disable )?night light(?: (?:on|off))?|(?:turn (?:on|off) )?(?:blue light|eye comfort|reading mode)(?: filter)?",
            (_, _) => new Cmd("url", "ms-settings:nightlight", Say: "Night light settings"));
        Add(@"lock(?: (?:the|my|this))?(?: (?:computer|pc|laptop|screen|it|windows|system|device))?|lock up", (_, _) => new Cmd("power", "lock"));
        Add(@"(?:put )?(?:the |my )?(?:computer|pc|laptop|system) (?:to )?sleep|sleep (?:the |my )?(?:computer|pc|laptop|system)|(?:put (?:the |my )?(?:computer|pc|laptop|system) )?(?:in(?:to)? )?sleep mode|hibernate",
            (_, _) => new Cmd("power", "sleep"));
        Add(@"(?:shut ?down|power off|turn off|switch off)(?: (?:the|my|this))?(?: (?:computer|pc|laptop|system|device|windows))?", (_, _) => new Cmd("power", "shutdown"));
        Add(@"(?:restart|reboot)(?: (?:the|my|this))?(?: (?:computer|pc|laptop|system|device|windows))?", (_, _) => new Cmd("power", "restart"));
        Add(@"(?:sign|log) ?(?:out|off)(?: of (?:windows|the computer|my account))?", (_, _) => new Cmd("power", "signout"));
        Add(@"(?:empty|clear|clean)(?: out)? (?:the |my )?(?:recycle bin|trash|bin|recycling bin|garbage)", (_, _) => new Cmd("recycle"));

        // ----- windows -----
        Add(@"(?:minimi[sz]e|hide)(?: (?:this|the|it|current|that))?(?: (?:window|app|one))?", (_, _) => new Cmd("window", "minimize", Say: "Minimized"));
        Add(@"(?:maximi[sz]e|make (?:it|this|the window) (?:big|bigger|full|large))(?: (?:this|the|it|current|that))?(?: (?:window|app|one))?", (_, _) => new Cmd("window", "maximize", Say: "Maximized"));
        Add(@"(?:restore|unmaximi[sz]e|restore down)(?: (?:this|the|it|current))?(?: window)?", (_, _) => new Cmd("window", "restore", Say: "Restored"));
        Add(@"(?:close|quit|exit|kill|shut)(?: (?:this|the|that|it|current|the current))?(?: (?:window|app|application|program|one|thing))?",
            (_, _) => new Cmd("keys", Keys: Keys(Alt, 0x73), Say: "Closed"));
        Add(@"(?:close|quit|exit|kill|shut down|end|stop|terminate|force close|force quit) (?:all |the |my )?(.+?)(?: (?:app|application|program|window|windows|process|completely))?",
            (m, _) => new Cmd("close-app", m.Groups[1].Value));

        // ----- generic key presses -----
        Add(@"(?:press|hit|push|tap|click|type the key|use)(?: the)? (.+?)(?: key| keys| button)?(?: (\w+) times| (once|twice|thrice))?",
            (m, _) =>
            {
                var keys = KeyCombo(m.Groups[1].Value);
                if (keys == null) return null;
                int n = m.Groups[2].Success ? Number(m.Groups[2].Value) ?? 1 : m.Groups[3].Value switch { "twice" => 2, "thrice" => 3, _ => 1 };
                return new Cmd("keys", Keys: keys, N: Math.Clamp(n, 1, 30), Say: Pretty(m.Groups[1].Value) + (n > 1 ? $" ×{n}" : ""));
            });

        // ----- open / switch to -----
        Add(@"(?:switch to|go to|jump to|bring up|show me|show|focus|focus on|take me to|back to|go back to|change to|move to) (?:the |my )?(.+?)(?: (?:app|application|window|website|site|page|program|tab))?",
            (m, _) => new Cmd("switch", m.Groups[1].Value));
        Add(@"(?:open|launch|start|run|fire up|boot up|load|pull up|open up|get me|give me|i need|navigate to|visit|browse to|browse|go on|get on|start up) (?:the |my |up |a |an )?(.+?)(?: (?:app|application|website|site|page|program|for me|in chrome|in the browser|on chrome))?",
            (m, _) => new Cmd("open", m.Groups[1].Value));
        // Open-ended questions: hand them to Google.
        Add(@"(?:what|who|whom|whose|where|when|why|how|which|whats|whos|hows|wheres|whens|is|are|was|were|does|do|did|can|could|should|will|would|tell me about|explain|i want to know) .+",
            (m, o) => new Cmd("url", Google(o), Say: "Google"));

        return r;
    }

    static string First(Match m)
    {
        for (int i = 1; i < m.Groups.Count; i++)
            if (m.Groups[i].Success && m.Groups[i].Value.Trim().Length > 0) return m.Groups[i].Value.Trim();
        return "";
    }

    /// <summary>"by 20", "a lot", "a little" → how much to change.</summary>
    static int Step(Match m, int normal)
    {
        foreach (Group g in m.Groups)
        {
            if (!g.Success || g.Index == 0) continue;
            if (Regex.IsMatch(g.Value, @"^\s*an?\b")) continue; // "a lot", "a bit" aren't the number one
            if (Number(g.Value) is int n) return Math.Clamp(n, 1, 100);
        }
        var v = m.Value;
        if (Regex.IsMatch(v, @"\b(a lot|much|more)\b")) return normal * 2;
        if (Regex.IsMatch(v, @"\b(little|bit|slightly|lil)\b")) return Math.Max(2, normal / 2);
        return normal;
    }

    // ---------------- WhatsApp & Claude (keep your exact words) ----------------

    static readonly Regex[] MessageForms =
    {
        new(@"^(?:send|write)(?: an?)?(?: whatsapp)? (?:message|msg|text)(?: on whatsapp)? to (?<who>.+?)(?: on whatsapp| via whatsapp)?\s*(?:,\s*)?(?:saying|that says|that|telling (?:him|her|them)|to say)\s*[:,]?\s*(?<msg>.+)$", I),
        new(@"^(?:message|text|msg|whatsapp|ping|tell)(?: to)? (?<who>.+?)(?: on whatsapp| via whatsapp| in whatsapp)?\s*(?:,\s*)?(?:saying|that says|that|to say|telling (?:him|her|them))\s*[:,]?\s*(?<msg>.+)$", I),
        new(@"^send (?<msg>.+?) to (?<who>.+?) (?:on|via|in|through) whatsapp$", I),
        new(@"^(?:message|text|whatsapp) (?<who>[A-Za-z]+)[,:]?\s+(?<msg>.{2,})$", I),
    };

    static Cmd? WhatsAppMessage(string orig)
    {
        var t = orig.Trim().TrimEnd('.', '!');
        foreach (var re in MessageForms)
        {
            var m = re.Match(t);
            if (!m.Success) continue;
            var who = Regex.Replace(m.Groups["who"].Value, @"\s+(?:on|via|in) whatsapp$", "", I).Trim(' ', ',');
            var msg = Regex.Replace(m.Groups["msg"].Value, @"\s+(?:on|via|in) whatsapp$", "", I).Trim();
            if (who.Length == 0 || msg.Length == 0) continue;
            if (Regex.IsMatch(who, @"^(me|us|claude|jarvis|you|him|her|them|everyone)$", I)) return null;
            return new Cmd("whatsapp-msg", who, Say: msg);
        }
        return null;
    }

    /// <summary>"ask Claude to explain recursion and send it" → prompt, new chat?, send?</summary>
    static Cmd? AskClaude(string orig)
    {
        var t = orig.Trim().TrimEnd('.', '!');
        bool newChat = Regex.IsMatch(t, @"\bnew (?:claude )?(?:chat|session|conversation)\b", I);
        bool send = false;
        var tail = Regex.Match(t, @"[\s,]*(?:and|then|and then)\s+(?:send|submit)(?:\s+it)?$", I);
        if (tail.Success) { send = true; t = t[..tail.Index]; }
        var m = Regex.Match(t, @"^(?:ask|tell) claude(?: (?:in|on) a new (?:chat|conversation))?(?:\s*(?:to|about|that|,|:|whether|if))?\s*(?<p>.*)$", I);
        string prompt = m.Success ? m.Groups["p"].Value.Trim() : "";
        if (!m.Success)
        {
            // "new claude chat (and ask …)"
            var rest = Regex.Match(t, @"(?:and |then )?(?:ask|tell)(?: it| claude)?(?: to| about)?\s*(?<p>.+)$", I);
            if (rest.Success) prompt = rest.Groups["p"].Value.Trim();
        }
        return new Cmd("claude", prompt, N: (send ? 1 : 0) | (newChat ? 2 : 0));
    }

    // ---------------- web ----------------

    public static string Google(string q) => "https://www.google.com/search?q=" + Uri.EscapeDataString(q.Trim());
    public static string YouTube(string q) => "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(q.Trim());

    static Cmd SiteSearch(string site, string q)
    {
        q = q.Trim();
        string e = Uri.EscapeDataString(q);
        string url = site switch
        {
            "youtube" => YouTube(q),
            "amazon" => "https://www.amazon.in/s?k=" + e,
            "flipkart" => "https://www.flipkart.com/search?q=" + e,
            "myntra" => "https://www.myntra.com/" + Uri.EscapeDataString(q.Replace(' ', '-')),
            "wikipedia" or "wiki" => "https://en.wikipedia.org/wiki/Special:Search?search=" + e,
            "github" => "https://github.com/search?q=" + e,
            "maps" or "google maps" => "https://www.google.com/maps/search/" + e,
            "reddit" => "https://www.reddit.com/search/?q=" + e,
            "stack overflow" or "stackoverflow" or "stack" => "https://stackoverflow.com/search?q=" + e,
            "images" or "google images" or "pictures" => "https://www.google.com/search?tbm=isch&q=" + e,
            "spotify" => "https://open.spotify.com/search/" + e,
            "linkedin" => "https://www.linkedin.com/search/results/all/?keywords=" + e,
            "twitter" or "x" => "https://x.com/search?q=" + e,
            "chatgpt" or "chat gpt" => "https://chatgpt.com/?q=" + e,
            "perplexity" => "https://www.perplexity.ai/search?q=" + e,
            "leetcode" => "https://leetcode.com/problemset/?search=" + e,
            "news" or "google news" => "https://news.google.com/search?q=" + e,
            "scholar" or "google scholar" => "https://scholar.google.com/scholar?q=" + e,
            "bing" => "https://www.bing.com/search?q=" + e,
            "duckduckgo" => "https://duckduckgo.com/?q=" + e,
            "instagram" => "https://www.instagram.com/explore/search/keyword/?q=" + e,
            "pinterest" => "https://www.pinterest.com/search/pins/?q=" + e,
            "quora" => "https://www.quora.com/search?q=" + e,
            "imdb" => "https://www.imdb.com/find/?q=" + e,
            "netflix" => "https://www.netflix.com/search?q=" + e,
            "prime video" => "https://www.primevideo.com/search/?phrase=" + e,
            "hotstar" => "https://www.hotstar.com/in/explore?search_query=" + e,
            "swiggy" => "https://www.swiggy.com/search?query=" + e,
            "zomato" => "https://www.zomato.com/search?q=" + e,
            "npm" => "https://www.npmjs.com/search?q=" + e,
            "pypi" => "https://pypi.org/search/?q=" + e,
            "geeksforgeeks" or "geeks for geeks" => "https://www.geeksforgeeks.org/search/?gq=" + e,
            "w3schools" => Google("site:w3schools.com " + q),
            "medium" => "https://medium.com/search?q=" + e,
            "dev to" => "https://dev.to/search?q=" + e,
            "hugging face" => "https://huggingface.co/search/full-text?q=" + e,
            "kaggle" => "https://www.kaggle.com/search?q=" + e,
            "internshala" => "https://internshala.com/internships/keywords-" + Uri.EscapeDataString(q.Replace(' ', '-')),
            "unstop" => "https://unstop.com/search?searchTerm=" + e,
            "naukri" => "https://www.naukri.com/" + Uri.EscapeDataString(q.Replace(' ', '-')) + "-jobs",
            "indeed" => "https://in.indeed.com/jobs?q=" + e,
            "udemy" => "https://www.udemy.com/courses/search/?q=" + e,
            "coursera" => "https://www.coursera.org/search?query=" + e,
            _ => Google(q),
        };
        string name = site switch { "wiki" => "Wikipedia", "yt" => "YouTube", _ => char.ToUpperInvariant(site[0]) + site[1..] };
        return new Cmd("url", url, Say: $"{name} · {q}");
    }

    static readonly Dictionary<string, string> Langs = new()
    {
        ["hindi"] = "hi", ["tamil"] = "ta", ["english"] = "en", ["french"] = "fr", ["spanish"] = "es", ["german"] = "de",
        ["japanese"] = "ja", ["korean"] = "ko", ["chinese"] = "zh-CN", ["arabic"] = "ar", ["telugu"] = "te", ["malayalam"] = "ml",
        ["kannada"] = "kn", ["bengali"] = "bn", ["marathi"] = "mr", ["gujarati"] = "gu", ["urdu"] = "ur", ["russian"] = "ru",
        ["italian"] = "it", ["portuguese"] = "pt", ["punjabi"] = "pa", ["sanskrit"] = "sa", ["turkish"] = "tr", ["dutch"] = "nl",
        ["thai"] = "th", ["vietnamese"] = "vi", ["indonesian"] = "id", ["greek"] = "el", ["latin"] = "la", ["sinhala"] = "si",
    };

    // ---------------- key names ----------------

    static readonly Dictionary<string, int> KeyNames = new()
    {
        ["control"] = Ctrl, ["ctrl"] = Ctrl, ["ctl"] = Ctrl, ["shift"] = Shift, ["alt"] = Alt, ["alter"] = Alt, ["option"] = Alt,
        ["windows"] = Win, ["win"] = Win, ["window"] = Win, ["super"] = Win, ["start"] = Win, ["meta"] = Win,
        ["enter"] = Enter, ["return"] = Enter, ["escape"] = Esc, ["esc"] = Esc, ["tab"] = Tab, ["space"] = Space, ["spacebar"] = Space,
        ["backspace"] = Back, ["delete"] = Del, ["del"] = Del, ["insert"] = 0x2D, ["home"] = Home, ["end"] = End,
        ["pageup"] = PgUp, ["pagedown"] = PgDn, ["up"] = Up, ["down"] = Down, ["left"] = Left, ["right"] = Right,
        ["plus"] = Plus, ["equals"] = Plus, ["equal"] = Plus, ["minus"] = Minus, ["dash"] = Minus, ["hyphen"] = Minus,
        ["period"] = Period, ["dot"] = Period, ["fullstop"] = Period, ["comma"] = Comma, ["slash"] = Slash, ["backslash"] = 0xDC,
        ["semicolon"] = 0xBA, ["quote"] = 0xDE, ["backtick"] = 0xC0, ["tilde"] = 0xC0, ["printscreen"] = PrtSc, ["capslock"] = 0x14,
        ["playpause"] = 0xB3, ["menu"] = 0x5D, ["bracket"] = 0xDB, ["leftbracket"] = 0xDB, ["rightbracket"] = 0xDD,
        ["zero"] = '0', ["one"] = '1', ["two"] = '2', ["three"] = '3', ["four"] = '4', ["five"] = '5', ["six"] = '6',
        ["seven"] = '7', ["eight"] = '8', ["nine"] = '9',
    };

    /// <summary>"control shift t", "alt f4", "ctrl+c", "page down" → key codes, or null.</summary>
    public static int[]? KeyCombo(string spoken)
    {
        var s = " " + spoken.ToLowerInvariant().Replace('+', ' ') + " ";
        foreach (var (a, b) in new[] { ("page up", "pageup"), ("page down", "pagedown"), ("print screen", "printscreen"), ("caps lock", "capslock"),
                     ("back space", "backspace"), ("space bar", "spacebar"), ("windows key", "windows"), ("window key", "windows"),
                     ("up arrow", "up"), ("down arrow", "down"), ("left arrow", "left"), ("right arrow", "right"), ("arrow up", "up"),
                     ("arrow down", "down"), ("arrow left", "left"), ("arrow right", "right"), ("full stop", "fullstop"),
                     ("play pause", "playpause"), ("left bracket", "leftbracket"), ("right bracket", "rightbracket"), (" and ", " "),
                     (" the ", " "), (" key ", " "), (" keys ", " "), (" button ", " "), (" plus ", " plus ") })
            s = s.Replace(" " + a.Trim() + " ", " " + b.Trim() + " ");
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;
        var keys = new List<int>();
        for (int i = 0; i < words.Length; i++)
        {
            var w = words[i];
            // "plus" between keys joins them ("control plus c"); at the end it's the + key.
            if (w == "plus" && i > 0 && i < words.Length - 1) continue;
            if (KeyNames.TryGetValue(w, out var vk)) keys.Add(vk);
            else if (Regex.IsMatch(w, @"^f([1-9]|1[0-2])$")) keys.Add(F1 + int.Parse(w[1..]) - 1);
            else if (w.Length == 1 && char.IsLetterOrDigit(w[0])) keys.Add(char.ToUpperInvariant(w[0]));
            else return null;
        }
        return keys.Count is > 0 and <= 4 ? keys.ToArray() : null;
    }

    static string Pretty(string spoken)
    {
        var parts = spoken.Replace('+', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w is not ("and" or "the" or "key" or "plus" or "keys"))
            .Select(w => w.Length <= 3 ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..]);
        return string.Join("+", parts);
    }

    // ---------------- numbers ----------------

    static readonly Dictionary<string, int> Small = new()
    {
        ["zero"] = 0, ["one"] = 1, ["a"] = 1, ["an"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13,
        ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fourty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70,
        ["eighty"] = 80, ["ninety"] = 90, ["couple"] = 2, ["few"] = 3, ["several"] = 5,
    };

    /// <summary>"25", "twenty five", "a hundred" → number; null if it isn't one.</summary>
    public static int? Number(string text)
    {
        var t = text.Trim().ToLowerInvariant().Replace("-", " ");
        if (t.Length == 0) return null;
        var digits = Regex.Match(t, @"^\d+(?:\.\d+)?");
        if (digits.Success) return (int)Math.Round(double.Parse(digits.Value, CultureInfo.InvariantCulture));
        int total = 0;
        bool any = false;
        foreach (var w in t.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (w is "and" or "of") continue;
            if (w == "hundred") { total = Math.Max(1, total) * 100; any = true; continue; }
            if (Small.TryGetValue(w, out var n)) { total += n; any = true; continue; }
            break;
        }
        return any ? total : null;
    }

    static int? Ordinal(string w) => w switch
    {
        "first" or "1st" => 1, "second" or "2nd" => 2, "third" or "3rd" => 3, "fourth" or "4th" => 4, "fifth" or "5th" => 5,
        "sixth" or "6th" => 6, "seventh" or "7th" => 7, "eighth" or "8th" => 8, "ninth" or "9th" => 9, "last" => 9, _ => null,
    };

    /// <summary>"25 minutes", "an hour and a half", "1 hour 30 minutes", "90 seconds" → seconds.</summary>
    public static int? Duration(string text)
    {
        var t = " " + text.ToLowerInvariant().Replace("-", " ") + " ";
        t = Regex.Replace(t, @"\b(?:half an hour|half hour|half a hour)\b", " 30 minutes ");
        t = Regex.Replace(t, @"\b(?:quarter of an hour|quarter hour)\b", " 15 minutes ");
        t = Regex.Replace(t, @"\b(an?|one|\d+) (hours?|hrs?) and a half\b", m => $" {(Number(m.Groups[1].Value) ?? 1) * 60 + 30} minutes ");
        t = Regex.Replace(t, @"\b(an?|one|\d+) and a half (hours?|hrs?)\b", m => $" {(Number(m.Groups[1].Value) ?? 1) * 60 + 30} minutes ");
        t = Regex.Replace(t, @"\b(an?|one|\d+) and a half (minutes?|mins?)\b", m => $" {(Number(m.Groups[1].Value) ?? 1) * 60 + 30} seconds ");
        int total = 0;
        bool any = false;
        foreach (Match m in Regex.Matches(t, @"((?:\d+(?:\.\d+)?|[a-z]+(?: [a-z]+)?)) (hours?|hrs?|h|minutes?|mins?|m|seconds?|secs?|s)\b"))
        {
            var numText = m.Groups[1].Value;
            int? n = Number(numText);
            if (n == null)
            {
                // "twenty five minutes" may have caught "for twenty"; try the last word only.
                var last = numText.Split(' ').Last();
                n = Number(last);
            }
            if (n == null) continue;
            var u = m.Groups[2].Value;
            int mult = u.StartsWith('h') ? 3600 : u.StartsWith('m') ? 60 : 1;
            total += n.Value * mult;
            any = true;
        }
        if (!any)
        {
            // A bare number is minutes ("focus for 40").
            var bare = Number(text);
            if (bare is > 0) return bare.Value * 60;
            return null;
        }
        return total;
    }

    // ---------------- reminders ----------------

    static readonly Regex ReminderStart = new(@"^(?:remind me|set (?:a |an )?reminder|add (?:a |an )?reminder|create (?:a |an )?reminder|make (?:a |an )?reminder|reminder|dont let me forget|(?:add|create|set|new|make) (?:a |an )?(deadline|due date)|i have (?:an? )?(.+?) due|(.+?) is due)\b\s*(?:to |for |about |of |that |called |named )?(.*)$", I);

    static readonly string[] Months = { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };
    static readonly string[] Days = { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };

    static Cmd? ParseReminder(string s, string original, DateTime now)
    {
        var m = ReminderStart.Match(s);
        if (!m.Success) return null;
        bool deadline = m.Groups[1].Success || m.Groups[2].Success || m.Groups[3].Success;
        string rest = m.Groups[2].Success ? m.Groups[2].Value + " " + m.Groups[4].Value
                    : m.Groups[3].Success ? m.Groups[3].Value + " " + m.Groups[4].Value
                    : m.Groups[4].Value;
        rest = " " + rest + " ";

        DateTime? date = null;
        TimeSpan? time = null;
        TimeSpan? offset = null;

        string Cut(string pattern, Action<Match> use)
        {
            var mm = Regex.Match(rest, pattern, I);
            if (!mm.Success) return rest;
            use(mm);
            return rest.Remove(mm.Index, mm.Length).Insert(mm.Index, " ");
        }

        // "in 10 minutes", "in an hour", "in 2 days"
        rest = Cut(@"\s(?:in|after) ((?:\d+|[a-z]+(?: [a-z]+)?|half an?)) (minutes?|mins?|hours?|hrs?|days?|weeks?)\b", mm =>
        {
            var u = mm.Groups[2].Value;
            int n = mm.Groups[1].Value.StartsWith("half") ? 0 : Number(mm.Groups[1].Value) ?? 1;
            offset = u.StartsWith("min") ? TimeSpan.FromMinutes(n)
                   : u.StartsWith('h') ? (mm.Groups[1].Value.StartsWith("half") ? TimeSpan.FromMinutes(30) : TimeSpan.FromHours(n))
                   : u.StartsWith('d') ? TimeSpan.FromDays(n) : TimeSpan.FromDays(7 * n);
        });
        rest = Cut(@"\s(?:in )?half an hour\b", _ => offset = TimeSpan.FromMinutes(30));
        // "at 5", "at 5:30 pm", "at 17:00", "by 9 am", "5 pm"
        rest = Cut(@"\s(?:at|by|around|before)?\s?(\d{1,2})(?:[:.](\d{2}))?\s?(am|pm|a m|p m|in the morning|in the evening|at night|in the afternoon|tonight)?(?=\s|$)", mm =>
        {
            int h = int.Parse(mm.Groups[1].Value);
            int min = mm.Groups[2].Success ? int.Parse(mm.Groups[2].Value) : 0;
            string ap = mm.Groups[3].Value.Replace(" ", "");
            bool explicitAt = Regex.IsMatch(mm.Value, @"^\s(at|by|around|before)");
            if (!explicitAt && ap.Length == 0 && !mm.Groups[2].Success) return; // a bare number isn't a time
            if (ap is "pm" or "intheevening" or "atnight" or "intheafternoon" or "tonight") { if (h < 12) h += 12; }
            else if (ap is "am" or "inthemorning") { if (h == 12) h = 0; }
            else if (h is >= 1 and <= 7) h += 12; // "at 5" means 5 pm for a student
            if (h < 24 && min < 60) time = new TimeSpan(h, min, 0);
        });
        rest = Cut(@"\s(?:at )?(noon|midday|midnight)\b", mm => time = mm.Groups[1].Value == "midnight" ? new TimeSpan(23, 59, 0) : new TimeSpan(12, 0, 0));
        rest = Cut(@"\s(?:in the |this )?(morning|afternoon|evening|tonight|night)\b", mm =>
        {
            time ??= mm.Groups[1].Value switch { "morning" => new TimeSpan(9, 0, 0), "afternoon" => new TimeSpan(15, 0, 0), "evening" => new TimeSpan(18, 0, 0), _ => new TimeSpan(21, 0, 0) };
            if (mm.Groups[1].Value == "tonight") date ??= now.Date;
        });
        rest = Cut(@"\s(?:the )?day after tomorrow\b", _ => date = now.Date.AddDays(2));
        rest = Cut(@"\s(?:by |on |for )?tomorrow\b", _ => date = now.Date.AddDays(1));
        rest = Cut(@"\s(?:by |on |for )?today\b", _ => date = now.Date);
        rest = Cut($@"\s(?:by |on |for |this |next |coming )*(?:(next) )?({string.Join("|", Days)})\b", mm =>
        {
            int target = Array.IndexOf(Days, mm.Groups[2].Value);
            int diff = (target - (int)now.DayOfWeek + 7) % 7;
            if (diff == 0) diff = 7;
            if (mm.Value.Contains("next") && diff < 7 && mm.Groups[1].Success) diff += 0; // "next friday" = the coming one
            date = now.Date.AddDays(diff);
        });
        rest = Cut($@"\s(?:by |on |for )?(?:the )?(\d{{1,2}})(?:st|nd|rd|th)?(?: of)? ({string.Join("|", Months)})\b|\s(?:by |on |for )?({string.Join("|", Months)}) (\d{{1,2}})(?:st|nd|rd|th)?\b", mm =>
        {
            int day = int.Parse(mm.Groups[1].Success ? mm.Groups[1].Value : mm.Groups[4].Value);
            int month = Array.IndexOf(Months, mm.Groups[2].Success ? mm.Groups[2].Value : mm.Groups[3].Value) + 1;
            int year = now.Year;
            if (day < 1 || day > DateTime.DaysInMonth(year, month)) return;
            var d = new DateTime(year, month, day);
            if (d < now.Date) d = d.AddYears(1);
            date = d;
        });
        rest = Cut(@"\s(?:by |on |for )?(?:the )?(\d{1,2})(?:st|nd|rd|th)\b", mm =>
        {
            int day = int.Parse(mm.Groups[1].Value);
            var d = new DateTime(now.Year, now.Month, 1);
            if (day < now.Day) d = d.AddMonths(1);
            if (day <= DateTime.DaysInMonth(d.Year, d.Month)) date = d.AddDays(day - 1);
        });
        rest = Cut(@"\s(?:next|in a) week\b", _ => date = now.Date.AddDays(7));

        string title = Regex.Replace(rest, @"\s+", " ").Trim();
        title = Regex.Replace(title, @"^(?:to|for|about|of|that|i need to|i have to|i should|i must)\s+", "", I);
        title = Regex.Replace(title, @"\s+(?:on|at|by|for|due|is due)$", "", I).Trim();
        if (title.Length == 0) title = deadline ? "Deadline" : "Reminder";
        // Keep your own capitals ("DBMS", not "dbms") by finding the same words in what you said.
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape);
        var same = Regex.Match(original, string.Join(@"[\W_]+", words), I);
        if (same.Success) title = same.Value;
        title = char.ToUpperInvariant(title[0]) + title[1..];

        DateTime when;
        if (offset != null) when = now + offset.Value;
        else if (date == null && time == null) return new Cmd("remind-ask", title, N: deadline ? 1 : 0);
        else
        {
            var d = date ?? now.Date;
            var t = time ?? (deadline ? new TimeSpan(23, 59, 0) : new TimeSpan(9, 0, 0));
            when = d + t;
            if (when <= now && date == null) when = when.AddDays(1); // "at 5" when it's 6 pm → tomorrow
        }
        return new Cmd("remind", title, N: deadline ? 1 : 0, When: when);
    }

    // ---------------- maths ----------------

    /// <summary>"25 times 4", "15 percent of 2400", "square root of 144" → the answer, or null.</summary>
    public static double? Calc(string text)
    {
        var t = " " + text.ToLowerInvariant() + " ";
        if (!Regex.IsMatch(t, @"\d|\b(one|two|three|four|five|six|seven|eight|nine|ten|twenty|hundred)\b")) return null;
        t = Regex.Replace(t, @"(\d+(?:\.\d+)?)\s*percent of", "$1 * 0.01 *");
        t = Regex.Replace(t, @"\bsquare root of\b", " sqrt ");
        t = Regex.Replace(t, @"\b(?:to the power of|raised to|power)\b", " ^ ");
        t = Regex.Replace(t, @"\bsquared\b", " ^ 2 ");
        t = Regex.Replace(t, @"\bcubed\b", " ^ 3 ");
        t = Regex.Replace(t, @"\b(?:plus|add|and)\b", " + ");
        t = Regex.Replace(t, @"\b(?:minus|less|subtract)\b", " - ");
        t = Regex.Replace(t, @"\b(?:times|multiplied by|into|x)\b", " * ");
        t = Regex.Replace(t, @"\b(?:divided by|over|by)\b", " / ");
        t = Regex.Replace(t, @"\b(?:mod|modulo)\b", " % ");
        // Number words → digits, longest first ("twenty five" before "twenty").
        t = Regex.Replace(t, @"\b(?:(?:twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)(?: (?:one|two|three|four|five|six|seven|eight|nine))?|zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen)(?: hundred)?\b",
            mm => Number(mm.Value)?.ToString() ?? mm.Value);
        t = t.Replace("×", "*").Replace("÷", "/").Replace(",", "");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        if (!Regex.IsMatch(t, @"^[\d\s.+\-*/^%()sqrt]+$")) return null;
        if (!Regex.IsMatch(t, @"[+\-*/^%]|sqrt")) return null; // just a number isn't a sum
        try
        {
            int i = 0;
            var tokens = Regex.Matches(t, @"\d+(?:\.\d+)?|sqrt|[+\-*/^%()]").Select(x => x.Value).ToList();
            double v = Expr(tokens, ref i);
            return i == tokens.Count && !double.IsNaN(v) && !double.IsInfinity(v) ? v : null;
        }
        catch { return null; }
    }

    static double Expr(List<string> t, ref int i)
    {
        double v = Term(t, ref i);
        while (i < t.Count && t[i] is "+" or "-")
        {
            var op = t[i++];
            double r = Term(t, ref i);
            v = op == "+" ? v + r : v - r;
        }
        return v;
    }

    static double Term(List<string> t, ref int i)
    {
        double v = Power(t, ref i);
        while (i < t.Count && t[i] is "*" or "/" or "%")
        {
            var op = t[i++];
            double r = Power(t, ref i);
            v = op == "*" ? v * r : op == "/" ? v / r : v % r;
        }
        return v;
    }

    static double Power(List<string> t, ref int i)
    {
        double v = Unary(t, ref i);
        if (i < t.Count && t[i] == "^") { i++; v = Math.Pow(v, Power(t, ref i)); }
        return v;
    }

    static double Unary(List<string> t, ref int i)
    {
        if (i >= t.Count) throw new FormatException();
        var s = t[i++];
        if (s == "-") return -Unary(t, ref i);
        if (s == "sqrt") return Math.Sqrt(Unary(t, ref i));
        if (s == "(")
        {
            double v = Expr(t, ref i);
            if (i < t.Count && t[i] == ")") i++;
            return v;
        }
        return double.Parse(s, CultureInfo.InvariantCulture);
    }
}
