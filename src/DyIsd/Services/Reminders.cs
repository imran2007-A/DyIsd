using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Threading;
using DyIsd.Island;
using DyIsd.Settings;

namespace DyIsd.Services;

/// <summary>One reminder: a line of text, maybe a time, ticked off or not.</summary>
public sealed class Reminder : Observable
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    string _text = "";
    [JsonPropertyName("text")] public string Text { get => _text; set => Set(ref _text, value); }
    DateTime? _due;
    /// <summary>Stored in UTC (what the iPhone page and Supabase use).</summary>
    [JsonPropertyName("due")] public DateTime? Due { get => _due; set { if (Set(ref _due, value?.ToUniversalTime())) Raise(nameof(WhenText)); } }
    bool _done;
    [JsonPropertyName("done")] public bool Done { get => _done; set => Set(ref _done, value); }
    [JsonPropertyName("done_at")] public DateTime? DoneAt { get; set; }
    /// <summary>Last change, Unix ms: the newest copy wins when the laptop and phone disagree.</summary>
    [JsonPropertyName("updated")] public long Updated { get; set; } = Now();
    [JsonPropertyName("deleted")] public bool Deleted { get; set; }
    /// <summary>The island already popped it up (not synced).</summary>
    [JsonIgnore] public bool Alerted { get; set; }

    [JsonIgnore] public string WhenText
    {
        get
        {
            if (Due is not { } u) return "";
            var d = u.ToLocalTime();
            return d.Date == DateTime.Today ? d.ToString("h:mm tt")
                 : d.Date == DateTime.Today.AddDays(1) ? "Tmrw " + d.ToString("h:mm tt")
                 : d.ToString("d MMM h:mm tt");
        }
    }

    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public void Touch() => Updated = Now();
}

/// <summary>
/// Your reminders: a checklist on the island (Home → Reminders) that Jarvis can add to and tick
/// off. Ones with a time pop up on the island when due. Ticked ones stay (struck through) until
/// the day ends. Saved in reminders.json and, if you've set up Supabase, synced with the iPhone
/// page every 20 seconds.
/// </summary>
public sealed class ReminderStore
{
    /// <summary>What the island shows: not deleted, to-do first (soonest first), then done ones.</summary>
    public ObservableCollection<Reminder> Items { get; } = new();
    public event Action<Reminder>? Due;
    public event Action? Changed;

    readonly List<Reminder> _all = new();
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(20) };
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };
    static string FilePath => Path.Combine(Log.Dir, "reminders.json");
    bool _syncing;

    public ReminderStore()
    {
        try { if (File.Exists(FilePath)) _all.AddRange(JsonSerializer.Deserialize<List<Reminder>>(File.ReadAllText(FilePath)) ?? new()); }
        catch (Exception ex) { Log.Error("reminders load", ex); }
        foreach (var r in _all) r.Alerted = r.Due is { } d && d <= DateTime.UtcNow; // don't replay old ones at start-up
        Refresh();
    }

    public void Start()
    {
        _tick.Tick += async (_, _) => await TickAsync();
        _tick.Start();
        _ = TickAsync();
    }

    public int Left => _all.Count(r => !r.Deleted && !r.Done);

    public Reminder Add(string text, DateTime? due)
    {
        var r = new Reminder { Text = text.Trim(), Due = due };
        _all.Add(r);
        Save();
        _ = SyncAsync();
        return r;
    }

    public void SetDone(Reminder r, bool done)
    {
        r.Done = done;
        r.DoneAt = done ? DateTime.UtcNow : null;
        r.Touch();
        Save();
        _ = SyncAsync();
    }

    public void Remove(Reminder r)
    {
        r.Deleted = true;
        r.Touch();
        Save();
        _ = SyncAsync();
    }

    public Reminder? ById(string id) => _all.FirstOrDefault(r => r.Id == id && !r.Deleted);

    /// <summary>The reminder you mean by "tick off the milk one": the closest wording.</summary>
    public Reminder? Find(string spoken, bool done = false)
    {
        var said = Norm(spoken);
        if (said.Length == 0) return null;
        var saidWords = said.Split(' ');
        Reminder? best = null;
        double bestScore = 0;
        foreach (var r in _all.Where(r => !r.Deleted && r.Done == done))
        {
            var t = Norm(r.Text);
            double score = t == said ? 2 : t.Contains(said) || said.Contains(t) ? 1.5
                : saidWords.Count(w => w.Length > 2 && t.Split(' ').Any(x => x == w || (x.Length > 3 && Voice.Fuzzy.Score(x, w) >= 0.8))) / (double)Math.Max(1, saidWords.Count(w => w.Length > 2));
            if (score > bestScore) { bestScore = score; best = r; }
        }
        return bestScore >= 0.5 ? best : null;
    }

    static string Norm(string s) => System.Text.RegularExpressions.Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9 ]", "").Trim();

    void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(_all, Json)); }
        catch (Exception ex) { Log.Error("reminders save", ex); }
        Refresh();
    }

    void Refresh()
    {
        var show = _all.Where(r => !r.Deleted)
            .OrderBy(r => r.Done)
            .ThenBy(r => r.Done ? 0 : r.Due?.Ticks ?? long.MaxValue)
            .ThenByDescending(r => r.Done ? r.DoneAt?.Ticks ?? 0 : 0)
            .ToList();
        if (!show.SequenceEqual(Items))
        {
            Items.Clear();
            foreach (var r in show) Items.Add(r);
        }
        Changed?.Invoke();
    }

    async Task TickAsync()
    {
        // Ticked off before today: cleared (deleted, so the phone clears them too).
        bool cleared = false;
        foreach (var r in _all.Where(r => r.Done && !r.Deleted && r.DoneAt is { } at && at.ToLocalTime().Date < DateTime.Today))
        {
            r.Deleted = true;
            r.Touch();
            cleared = true;
        }
        if (cleared) Save();

        foreach (var r in _all.Where(r => !r.Deleted && !r.Done && !r.Alerted && r.Due is { } d && d <= DateTime.UtcNow))
        {
            r.Alerted = true;
            Due?.Invoke(r);
        }
        await SyncAsync();
    }

    // ---------------- Supabase sync ----------------

    public static bool SyncOn => SettingsStore.Current.SupabaseUrl.Trim().Length > 0 && SettingsStore.Current.SupabaseKey.Trim().Length > 0;
    public string SyncStatus { get; private set; } = "";

    HttpRequestMessage Req(HttpMethod m, string path)
    {
        var s = SettingsStore.Current;
        var q = new HttpRequestMessage(m, s.SupabaseUrl.Trim().TrimEnd('/') + "/rest/v1/" + path);
        q.Headers.Add("apikey", s.SupabaseKey.Trim());
        q.Headers.Add("Authorization", "Bearer " + s.SupabaseKey.Trim());
        return q;
    }

    /// <summary>
    /// Both ways, newest copy wins: download everything (it's a small list), take the phone's
    /// newer edits, then upload the laptop's newer ones.
    /// </summary>
    public async Task SyncAsync()
    {
        if (!SyncOn || _syncing) return;
        _syncing = true;
        try
        {
            using var get = await Http.SendAsync(Req(HttpMethod.Get, "reminders?select=*"));
            var body = await get.Content.ReadAsStringAsync();
            if (!get.IsSuccessStatusCode) { SyncStatus = $"Supabase said {(int)get.StatusCode}: {body}"; Log.Write("reminders sync: " + SyncStatus); return; }
            var remote = JsonSerializer.Deserialize<List<Reminder>>(body) ?? new();
            bool changed = false;
            foreach (var rr in remote)
            {
                var mine = _all.FirstOrDefault(x => x.Id == rr.Id);
                if (mine == null) { rr.Alerted = rr.Due is { } d && d <= DateTime.UtcNow; _all.Add(rr); changed = true; }
                else if (rr.Updated > mine.Updated)
                {
                    mine.Text = rr.Text; mine.Due = rr.Due; mine.Done = rr.Done; mine.DoneAt = rr.DoneAt; mine.Deleted = rr.Deleted; mine.Updated = rr.Updated;
                    changed = true;
                }
            }
            var push = _all.Where(x => remote.FirstOrDefault(r => r.Id == x.Id) is not { } r || x.Updated > r.Updated).ToList();
            if (push.Count > 0)
            {
                var post = Req(HttpMethod.Post, "reminders?on_conflict=id");
                post.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
                post.Content = new StringContent(JsonSerializer.Serialize(push, Json), Encoding.UTF8, "application/json");
                using var res = await Http.SendAsync(post);
                if (!res.IsSuccessStatusCode) { SyncStatus = $"Supabase said {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}"; Log.Write("reminders sync: " + SyncStatus); return; }
            }
            if (changed) Save();
            SyncStatus = $"Synced {DateTime.Now:HH:mm}";
        }
        catch (Exception ex) { SyncStatus = "Couldn't reach Supabase: " + ex.Message; }
        finally { _syncing = false; }
    }
}
