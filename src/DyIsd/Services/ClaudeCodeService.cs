using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DyIsd.Controls;
using DyIsd.Island;

namespace DyIsd.Services;

/// <summary>What the Claude Code views show: the session that changed most recently.</summary>
public sealed class ClaudeState : Observable
{
    string _status = "Idle", _task = "", _project = "", _detail = "", _elapsed = "0:00";
    bool _active, _done, _needsYou;
    Mood _mood = Mood.Idle;

    /// <summary>Thinking, Editing, Running, Reading, Searching, Browsing, Needs you, Done.</summary>
    public string Status { get => _status; set => Set(ref _status, value); }
    /// <summary>What you asked it, first line.</summary>
    public string Task { get => _task; set => Set(ref _task, value); }
    public string Project { get => _project; set { if (Set(ref _project, value)) Raise(nameof(Header)); } }
    /// <summary>The file it's editing, the command it's running…</summary>
    public string Detail { get => _detail; set => Set(ref _detail, value); }
    public string Elapsed { get => _elapsed; set => Set(ref _elapsed, value); }
    public string Header => _project.Length > 0 ? "Claude Code · " + _project : "Claude Code";
    public bool IsActive { get => _active; set => Set(ref _active, value); }
    public bool IsDone { get => _done; set { if (Set(ref _done, value)) Raise(nameof(Working)); } }
    public bool NeedsYou { get => _needsYou; set { if (Set(ref _needsYou, value)) Raise(nameof(Working)); } }
    public bool Working => _active && !_done && !_needsYou;
    public Mood Mood { get => _mood; set => Set(ref _mood, value); }
    public DateTime Since { get; set; }
    public string[] HostProcesses { get; set; } = Array.Empty<string>();
    /// <summary>The hooks are in Claude Code's settings.</summary>
    public bool Connected { get => _connected; set => Set(ref _connected, value); }
    bool _connected;
    public int Sessions { get => _sessions; set => Set(ref _sessions, value); }
    int _sessions;
}

/// <summary>
/// Claude Code's status on the island (Thinking → Editing → Done), like the iPhone shows a
/// delivery. Claude Code runs "hooks" (small commands) at each step; DyIsd adds one that
/// sends the step here with curl. It listens on 127.0.0.1 only, so nothing outside this laptop
/// can reach it. Which app Claude Code runs in (terminal, VS Code, the Claude app) is found
/// from the connecting process, so the island hides while you're in that app and right-click
/// jumps to it.
/// </summary>
public sealed class ClaudeCodeService : IDisposable
{
    public const int Port = 47811;
    const string Marker = "127.0.0.1:47811/claude";

    public ClaudeState State { get; } = new();
    public event Action? ActiveChanged;
    /// <summary>A session finished (for the chime and the happy face).</summary>
    public event Action? Finished;

    sealed class Session
    {
        public string Id = "", Project = "", Task = "", Status = "Thinking", Detail = "";
        public DateTime Since = DateTime.Now, Last = DateTime.Now, DoneAt;
        public bool Done, NeedsYou;
        public string[] Hosts = Array.Empty<string>();
    }

    readonly Dictionary<string, Session> _sessions = new();
    readonly Dispatcher _ui = Application.Current.Dispatcher;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    TcpListener? _listener;
    Session? _shown;

    /// <summary>How long "Done" stays on the island.</summary>
    static readonly TimeSpan DoneFor = TimeSpan.FromSeconds(6);
    /// <summary>No word from a working session for this long: it was closed without saying.</summary>
    static readonly TimeSpan Silent = TimeSpan.FromMinutes(15);

    public void Start()
    {
        _tick.Tick += (_, _) => Tick();
        State.Connected = HooksInstalled();
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            _ = AcceptLoop(_listener);
            Log.Write($"claude code: listening on 127.0.0.1:{Port} (hooks {(State.Connected ? "on" : "not set up")})");
        }
        catch (Exception ex)
        {
            Log.Error("claude code listener", ex);
        }
    }

    async Task AcceptLoop(TcpListener l)
    {
        while (true)
        {
            TcpClient c;
            try { c = await l.AcceptTcpClientAsync(); }
            catch { return; } // stopped
            _ = Task.Run(() => Handle(c));
        }
    }

    void Handle(TcpClient c)
    {
        using (c)
        {
            try
            {
                c.ReceiveTimeout = 2000;
                var s = c.GetStream();
                var body = ReadRequest(s);
                // Who's asking, while curl is still connected and waiting for the reply.
                var hosts = body == null ? Array.Empty<string>() : HostsOf(((IPEndPoint)c.Client.RemoteEndPoint!).Port);
                var w = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                s.Write(w, 0, w.Length);
                if (body == null) return;
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement.Clone();
                _ui.BeginInvoke(() => OnEvent(root, hosts));
            }
            catch (Exception ex)
            {
                Log.Write("claude code: bad request " + ex.Message);
            }
        }
    }

    /// <summary>Just enough HTTP for curl's POST: headers, then Content-Length bytes of body.</summary>
    static string? ReadRequest(NetworkStream s)
    {
        var buf = new List<byte>();
        var one = new byte[4096];
        int headerEnd = -1, length = 0;
        while (true)
        {
            int n = s.Read(one, 0, one.Length);
            if (n <= 0) break;
            buf.AddRange(one.AsSpan(0, n).ToArray());
            if (headerEnd < 0)
            {
                var text = Encoding.ASCII.GetString(buf.ToArray());
                headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd >= 0)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(text[..headerEnd], @"content-length:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    length = m.Success ? int.Parse(m.Groups[1].Value) : 0;
                    if (length > 1_000_000) return null;
                }
            }
            if (headerEnd >= 0 && buf.Count >= headerEnd + 4 + length) break;
        }
        if (headerEnd < 0) return null;
        return Encoding.UTF8.GetString(buf.ToArray(), headerEnd + 4, Math.Min(length, buf.Count - headerEnd - 4));
    }

    // ---------------- events → status ----------------

    void OnEvent(JsonElement e, string[] hosts)
    {
        string Str(JsonElement el, string name) => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        var kind = Str(e, "hook_event_name");
        var id = Str(e, "session_id");
        if (id.Length == 0) return;
        if (!_sessions.TryGetValue(id, out var s))
        {
            // DyIsd started mid-session: pick it up from the next real step.
            if (kind is "SessionEnd" or "Notification" or "PostToolUse") return;
            s = _sessions[id] = new Session { Id = id };
        }
        s.Last = DateTime.Now;
        if (hosts.Length > 0) s.Hosts = hosts;
        var cwd = Str(e, "cwd");
        if (cwd.Length > 0) s.Project = Path.GetFileName(cwd.TrimEnd('\\', '/'));

        switch (kind)
        {
            case "UserPromptSubmit":
                s.Task = FirstLine(Str(e, "prompt"));
                s.Since = DateTime.Now;
                s.Status = "Thinking";
                s.Detail = "";
                s.Done = s.NeedsYou = false;
                break;
            case "PreToolUse":
                if (s.Done) { s.Done = false; s.Since = DateTime.Now; }
                s.NeedsYou = false;
                var tool = Str(e, "tool_name");
                e.TryGetProperty("tool_input", out var input);
                (s.Status, s.Detail) = Describe(tool, input, Str);
                break;
            case "PostToolUse":
                if (s.Done) break;
                s.NeedsYou = false;
                s.Status = "Thinking";
                break;
            case "Notification":
                var msg = Str(e, "message");
                // "Claude needs your permission to use Bash" — not the 60-second "waiting for input" nudge.
                if (s.Done || !System.Text.RegularExpressions.Regex.IsMatch(msg, "permission|approve|confirm", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) break;
                s.NeedsYou = true;
                s.Status = "Needs you";
                s.Detail = FirstLine(msg);
                break;
            case "Stop":
                s.Done = true;
                s.NeedsYou = false;
                s.Status = "Done";
                s.Detail = "";
                s.DoneAt = DateTime.Now;
                Finished?.Invoke();
                break;
            case "SessionEnd":
                _sessions.Remove(id);
                break;
            default:
                return;
        }
        if (kind == "SessionEnd") Log.Write($"claude code: {s.Project} session ended");
        else Log.Write($"claude code: {s.Project} {kind} → {s.Status}{(s.Detail.Length > 0 ? " · " + s.Detail : "")}"
                       + (kind == "UserPromptSubmit" ? $" (runs in: {string.Join(" < ", s.Hosts)})" : ""));
        Refresh(prefer: kind == "SessionEnd" ? null : s);
    }

    static (string, string) Describe(string tool, JsonElement input, Func<JsonElement, string, string> str)
    {
        string file = Path.GetFileName(str(input, "file_path").Length > 0 ? str(input, "file_path") : str(input, "notebook_path"));
        return tool switch
        {
            "Edit" or "MultiEdit" or "Write" or "NotebookEdit" => ("Editing", file),
            "Read" => ("Reading", file),
            "Bash" or "PowerShell" => ("Running", Short(str(input, "description").Length > 0 ? str(input, "description") : str(input, "command"))),
            "Grep" or "Glob" => ("Searching", Short(str(input, "pattern"))),
            "WebFetch" or "WebSearch" => ("Browsing", Short(str(input, "url").Length > 0 ? str(input, "url") : str(input, "query"))),
            "Task" or "Agent" => ("Delegating", Short(str(input, "description"))),
            "TodoWrite" => ("Planning", ""),
            _ when tool.StartsWith("mcp__") => ("Using tools", tool.Split("__").Last().Replace('_', ' ')),
            _ => ("Working", tool),
        };
    }

    static string FirstLine(string s)
    {
        var line = s.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return line.Length > 90 ? line[..88] + "…" : line;
    }

    static string Short(string s) => s.Length > 48 ? s[..46] + "…" : s.Replace('\n', ' ');

    void Tick()
    {
        foreach (var s in _sessions.Values.ToList())
            if (!s.Done && DateTime.Now - s.Last > Silent) _sessions.Remove(s.Id);
        Refresh(prefer: null);
    }

    /// <summary>Shows the session that matters most: one needing you, then working, then just done.</summary>
    void Refresh(Session? prefer)
    {
        Session? Visible(Session s) => !s.Done || DateTime.Now - s.DoneAt < DoneFor ? s : null;
        var live = _sessions.Values.Select(Visible).Where(s => s != null).Cast<Session>().ToList();
        var pick = live.FirstOrDefault(s => s.NeedsYou)
                   ?? (prefer != null && live.Contains(prefer) ? prefer : null)
                   ?? live.Where(s => !s.Done).OrderByDescending(s => s.Last).FirstOrDefault()
                   ?? live.OrderByDescending(s => s.DoneAt).FirstOrDefault();
        State.Sessions = _sessions.Values.Count(s => !s.Done);
        bool was = State.IsActive;
        if (pick != null)
        {
            if (!ReferenceEquals(pick, _shown) || !was) State.Since = pick.Since;
            _shown = pick;
            State.Project = pick.Project;
            State.Task = pick.Task;
            State.Status = pick.Status;
            State.Detail = pick.Detail;
            State.IsDone = pick.Done;
            State.NeedsYou = pick.NeedsYou;
            State.HostProcesses = pick.Hosts;
            var t = (pick.Done ? pick.DoneAt : DateTime.Now) - pick.Since;
            State.Elapsed = t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
            State.Mood = pick.Done ? Mood.Happy : pick.NeedsYou ? Mood.Alert : pick.Status == "Thinking" ? Mood.Think : Mood.Busy;
            State.IsActive = true;
        }
        else
        {
            _shown = null;
            State.IsActive = false;
        }
        if (State.IsActive) _tick.Start(); else _tick.Stop();
        if (was != State.IsActive) ActiveChanged?.Invoke();
    }

    // ---------------- which app is Claude Code running in ----------------

    /// <summary>
    /// The apps above the curl that just connected (curl → bash → claude → Windows Terminal…),
    /// found from the connection's owner and each process's parent.
    /// </summary>
    static string[] HostsOf(int clientPort)
    {
        try
        {
            int pid = OwnerOf(clientPort);
            if (pid <= 0) return Array.Empty<string>();
            var parents = Processes();
            var names = new List<string>();
            for (int i = 0; i < 12 && parents.TryGetValue(pid, out var p); i++)
            {
                var n = Path.GetFileNameWithoutExtension(p.Exe).ToLowerInvariant();
                if (n is "explorer" or "services" or "svchost" or "wininit" or "sihost" or "userinit") break;
                if (n is not ("curl" or "bash" or "sh" or "cmd" or "conhost" or "openconsole")) names.Add(n);
                if (p.Parent == pid) break;
                pid = p.Parent;
            }
            return names.Distinct().ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TcpRow { public uint State, LocalAddr, LocalPort, RemoteAddr, RemotePort, Pid; }

    [DllImport("iphlpapi.dll")]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int af, int tableClass, uint reserved);

    /// <summary>The process that owns the client end of a connection to our port.</summary>
    static int OwnerOf(int clientPort)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 5, 0); // AF_INET, TCP_TABLE_OWNER_PID_ALL
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, 2, 5, 0) != 0) return 0;
            int count = Marshal.ReadInt32(buf);
            int rowSize = Marshal.SizeOf<TcpRow>();
            for (int i = 0; i < count; i++)
            {
                var r = Marshal.PtrToStructure<TcpRow>(buf + 4 + i * rowSize);
                int local = (int)(((r.LocalPort & 0xFF) << 8) | ((r.LocalPort >> 8) & 0xFF));
                int remote = (int)(((r.RemotePort & 0xFF) << 8) | ((r.RemotePort >> 8) & 0xFF));
                if (local == clientPort && remote == Port) return (int)r.Pid;
            }
            return 0;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry
    {
        public uint Size, Usage, Pid;
        public IntPtr HeapId;
        public uint ModuleId, Threads, Parent;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref ProcessEntry e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref ProcessEntry e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    static Dictionary<int, (int Parent, string Exe)> Processes()
    {
        var map = new Dictionary<int, (int, string)>();
        var snap = CreateToolhelp32Snapshot(2, 0); // TH32CS_SNAPPROCESS
        if (snap == new IntPtr(-1)) return map;
        try
        {
            var e = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
                map[(int)e.Pid] = ((int)e.Parent, e.Exe);
        }
        finally { CloseHandle(snap); }
        return map;
    }

    // ---------------- connecting Claude Code ----------------

    static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    /// <summary>
    /// The hook command. Claude Code on Windows runs hooks in Git Bash, which has curl. It gives
    /// up after a second and never fails, so Claude Code isn't slowed or stopped when DyIsd
    /// isn't running.
    /// </summary>
    const string Command = "curl -s -m 1 --data-binary @- http://" + Marker + " >/dev/null 2>&1 || true";
    static readonly string[] Events = { "UserPromptSubmit", "PreToolUse", "PostToolUse", "Notification", "Stop", "SessionEnd" };

    public static bool HooksInstalled()
    {
        try { return File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Contains(Marker); }
        catch { return false; }
    }

    /// <summary>Adds DyIsd's hooks to ~/.claude/settings.json (keeps everything else; backs it up once).</summary>
    public bool Connect()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            JsonObject root = new();
            if (File.Exists(SettingsPath))
            {
                var text = File.ReadAllText(SettingsPath);
                if (!File.Exists(SettingsPath + ".before-dyisd")) File.WriteAllText(SettingsPath + ".before-dyisd", text);
                if (text.Trim().Length > 0) root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? new JsonObject();
            }
            var hooks = root["hooks"] as JsonObject ?? new JsonObject();
            root["hooks"] = hooks;
            foreach (var ev in Events)
            {
                var list = hooks[ev] as JsonArray ?? new JsonArray();
                hooks[ev] = list;
                if (list.ToJsonString().Contains(Marker)) continue;
                var entry = new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = Command, ["timeout"] = 3 }) };
                if (ev is "PreToolUse" or "PostToolUse") entry["matcher"] = "*";
                list.Add(entry);
            }
            File.WriteAllText(SettingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            State.Connected = true;
            Log.Write("claude code: hooks added to " + SettingsPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("claude code connect", ex);
            return false;
        }
    }

    /// <summary>Takes DyIsd's hooks back out, leaving any others.</summary>
    public bool Disconnect()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return true;
            var root = JsonNode.Parse(File.ReadAllText(SettingsPath), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
            if (root?["hooks"] is JsonObject hooks)
            {
                foreach (var ev in hooks.Select(p => p.Key).ToList())
                {
                    if (hooks[ev] is not JsonArray list) continue;
                    foreach (var item in list.ToList())
                        if (item != null && item.ToJsonString().Contains(Marker)) list.Remove(item);
                    if (list.Count == 0) hooks.Remove(ev);
                }
                if (hooks.Count == 0) root.Remove("hooks");
                File.WriteAllText(SettingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            State.Connected = false;
            Log.Write("claude code: hooks removed");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("claude code disconnect", ex);
            return false;
        }
    }

    public void Dispose()
    {
        try { _listener?.Stop(); } catch { }
    }
}
