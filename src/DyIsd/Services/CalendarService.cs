using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Threading;
using DyIsd.Settings;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace DyIsd.Services;

public sealed record UpcomingItem(string Key, string Title, DateTime Start, bool AllDay, string? Location, bool IsDeadline, ManualDeadline? Manual = null);

/// <summary>
/// Reads classes and deadlines from a calendar link (Google Calendar's "secret address in iCal
/// format") plus deadlines typed in settings, and raises reminders before they happen.
/// </summary>
public sealed class CalendarService
{
    /// <summary>The item, its headline and a detail line.</summary>
    public event Action<UpcomingItem, string, string>? Alert;
    public string Status { get; private set; } = "No calendar link yet.";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    readonly DispatcherTimer _check = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(15) };
    readonly HashSet<string> _fired = new();
    List<UpcomingItem> _feed = new();

    // Reminder lead times.
    static readonly TimeSpan[] EventLeads = { TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(10) };

    public void Start()
    {
        _check.Tick += (_, _) => Check();
        _refresh.Tick += async (_, _) => await RefreshAsync();
        _check.Start();
        _refresh.Start();
        _ = RefreshAsync();
    }

    public async Task<string> RefreshAsync()
    {
        var url = (SettingsStore.Current.CalendarUrl ?? "").Trim();
        if (url.Length == 0)
        {
            _feed = new();
            return Status = "No calendar link yet.";
        }
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url[9..];

        try
        {
            var text = await Http.GetStringAsync(url);
            _feed = await Task.Run(() => Parse(text));
            Status = $"Connected. {_feed.Count} events in the next 7 days.";
        }
        catch (Exception ex)
        {
            Status = "Couldn't load the link: " + ex.Message;
            Log.Error("calendar", ex);
        }
        Check();
        return Status;
    }

    static List<UpcomingItem> Parse(string ics)
    {
        var cal = Calendar.Load(ics);
        var from = new CalDateTime(DateTime.UtcNow.AddHours(-2), "UTC");
        var to = new CalDateTime(DateTime.UtcNow.AddDays(7), "UTC");
        var list = new List<UpcomingItem>();
        foreach (var occ in cal.GetOccurrences(from, to))
        {
            if (occ.Source is not CalendarEvent ev) continue;
            bool allDay = ev.IsAllDay;
            DateTime start = allDay ? occ.Period.StartTime.Value.Date : occ.Period.StartTime.AsSystemLocal;
            list.Add(new UpcomingItem($"{ev.Uid}|{start:o}", ev.Summary ?? "(no title)", start, allDay, ev.Location, false));
        }
        return list.OrderBy(i => i.Start).ToList();
    }

    /// <summary>Everything still ahead, soonest first (for the Control Center list).</summary>
    public List<UpcomingItem> Upcoming()
    {
        var now = DateTime.Now;
        return All().Where(i => i.AllDay ? i.Start.Date >= now.Date : i.Start > now).OrderBy(i => i.Start).ToList();
    }

    IEnumerable<UpcomingItem> All()
    {
        foreach (var i in _feed) yield return i;
        foreach (var d in SettingsStore.Current.ManualDeadlines)
            yield return new UpcomingItem($"m|{d.Title}|{d.Due:o}", d.Title, d.Due, false, null, true, d);
    }

    void Check()
    {
        var now = DateTime.Now;
        foreach (var item in All())
        {
            if (item.AllDay)
            {
                // All-day items (like "Assignment due") remind once at 8 AM that day.
                var at = item.Start.Date.AddHours(8);
                if (now >= at && now < at.AddMinutes(5) && _fired.Add(item.Key + "|day"))
                    Alert?.Invoke(item, $"Today: {item.Title}", "All day");
                continue;
            }

            if (item.Manual is { } m)
            {
                CheckManual(item, m, now);
                continue;
            }

            foreach (var lead in EventLeads)
                Fire(item, "b" + lead.TotalMinutes, item.Start - lead, now);
        }
    }

    /// <summary>Deadlines you added: your chosen "before" times, repeats and extra dates.</summary>
    void CheckManual(UpcomingItem item, ManualDeadline m, DateTime now)
    {
        foreach (int min in m.RemindBeforeMinutes)
            Fire(item, "b" + min, item.Start.AddMinutes(-min), now);

        foreach (var on in m.RemindOn)
            Fire(item, "on" + on.Ticks, on, now);

        if (m.Repeat is "daily" or "weekly")
        {
            var at = now.Date + m.RepeatAt;
            // Weekly nudges land on the same weekday as the deadline.
            bool rightDay = m.Repeat == "daily" || (item.Start.Date - now.Date).Days % 7 == 0;
            if (rightDay && at < item.Start && at >= m.Created)
                Fire(item, "r" + now.ToString("yyyyMMdd"), at, now);
        }
    }

    /// <summary>Raises the reminder once, if its time came within the last 3 minutes.</summary>
    void Fire(UpcomingItem item, string key, DateTime at, DateTime now)
    {
        if (now < at || now >= at.AddMinutes(3) || !_fired.Add(item.Key + "|" + key)) return;
        var left = item.Start - now;
        string headline = left.TotalMinutes < 1 ? $"{item.Title} is due now" : Headline(item, left);
        Alert?.Invoke(item, headline, Detail(item));
    }

    /// <summary>Plain-English summary of a deadline's reminders, e.g. "1 day and 2 hours before, and daily at 9:00 AM".</summary>
    public static string Describe(IEnumerable<int> before, string repeat, TimeSpan repeatAt, IEnumerable<DateTime> on)
    {
        var parts = new List<string>();
        var mins = before.OrderByDescending(m => m).ToList();
        if (mins.Count > 0)
        {
            var names = mins.Where(m => m > 0).Select(BeforeName).ToList();
            string text = names.Count > 0 ? JoinAnd(names) + " before" : "";
            if (mins.Contains(0)) text = text.Length > 0 ? text + " and when it's due" : "when it's due";
            parts.Add(text);
        }
        var time = DateTime.Today.Add(repeatAt).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
        if (repeat == "daily") parts.Add($"every day at {time}");
        if (repeat == "weekly") parts.Add($"every week at {time}");
        foreach (var d in on.OrderBy(d => d))
            parts.Add("on " + d.ToString("ddd d MMM, h:mm tt", System.Globalization.CultureInfo.InvariantCulture));
        return parts.Count == 0 ? "No reminders. It will only show in the list." : "Reminds you " + JoinAnd(parts) + ".";
    }

    public static string BeforeName(int minutes) => minutes switch
    {
        >= 10080 when minutes % 10080 == 0 => minutes == 10080 ? "1 week" : $"{minutes / 10080} weeks",
        >= 1440 when minutes % 1440 == 0 => minutes == 1440 ? "1 day" : $"{minutes / 1440} days",
        >= 60 when minutes % 60 == 0 => minutes == 60 ? "1 hour" : $"{minutes / 60} hours",
        _ => $"{minutes} min",
    };

    static string JoinAnd(IList<string> items) =>
        items.Count <= 1 ? string.Concat(items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];

    /// <summary>The next thing coming up, for the tray's "What's next".</summary>
    public (string Title, string Detail)? Next()
    {
        var now = DateTime.Now;
        var next = All()
            .Where(i => i.AllDay ? i.Start.Date >= now.Date : i.Start > now)
            .OrderBy(i => i.Start)
            .FirstOrDefault();
        if (next == null) return null;
        if (next.AllDay) return (next.Start.Date == now.Date ? $"Today: {next.Title}" : next.Title, next.Start.ToString("ddd, d MMM") + " · All day");
        return (Headline(next, next.Start - now), Detail(next));
    }

    static string Headline(UpcomingItem i, TimeSpan left) =>
        i.IsDeadline ? $"{i.Title} due in {Human(left)}" : $"{i.Title} in {Human(left)}";

    static string Detail(UpcomingItem i)
    {
        string when = i.Start.Date == DateTime.Today ? i.Start.ToString("h:mm tt") : i.Start.ToString("ddd, d MMM · h:mm tt");
        return string.IsNullOrWhiteSpace(i.Location) ? when : $"{when} · {i.Location}";
    }

    static string Human(TimeSpan t)
    {
        if (t.TotalMinutes < 1) return "less than a minute";
        if (t.TotalHours < 1) return $"{(int)Math.Round(t.TotalMinutes)} min";
        if (t.TotalDays < 2)
        {
            int h = (int)t.TotalHours, m = (int)Math.Round(t.TotalMinutes - h * 60);
            if (m == 60) { h++; m = 0; }
            return m == 0 ? $"{h}h" : $"{h}h {m}m";
        }
        return $"{(int)Math.Round(t.TotalDays)} days";
    }
}
