using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DyIsd.Controls;
using DyIsd.Services;
using DyIsd.Settings;

namespace DyIsd;

/// <summary>
/// The black panel that drops down from the island: focus timer, deadlines, calendar link,
/// feature on/off tiles, position and options. Opens from the tray icon or the island.
/// </summary>
public partial class ControlCenter : Window
{
    readonly App _app;
    readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(30) };
    static AppSettings S => SettingsStore.Current;

    // What's being typed into the "add deadline" form.
    DateTime? _day;
    TimeSpan? _time;
    static readonly int[] DefaultBefore = { 1440, 120, 30 };
    readonly HashSet<int> _before = new(DefaultBefore);
    string _repeat = "none";
    TimeSpan _repeatAt = new(9, 0, 0);
    readonly List<DateTime> _extra = new();
    DateTime _extraDay = DateTime.Today.AddDays(1);

    public DateTime ClosedAt { get; private set; }

    public ControlCenter(App app)
    {
        _app = app;
        InitializeComponent();
        try { Logo.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/icon.png")); } catch { }

        BuildFocusChips();
        BuildDayChips();
        BuildTimeChips();
        BuildReminderChips();
        BuildTiles();
        BuildPositions();
        BuildOptions();
        RefreshAll();

        NewTitle.TextChanged += (_, _) => UpdatePreview();
        NewTitle.KeyDown += (_, e) => { if (e.Key == Key.Enter && AddBtn.IsEnabled) Add_Click(this, new RoutedEventArgs()); };
        CustomTime.TextChanged += (_, _) =>
        {
            if (TryParseTime(CustomTime.Text, out var t)) { _time = t; UncheckAll(TimeChips); }
            else if (CustomTime.Text.Length > 0) _time = null;
            UpdatePreview();
        };

        RepeatTime.TextChanged += (_, _) =>
        {
            if (TryParseTime(RepeatTime.Text, out var t)) _repeatAt = t;
            else if (RepeatTime.Text.Length == 0) _repeatAt = new TimeSpan(9, 0, 0);
            UpdatePreview();
        };
        ExtraTime.TextChanged += (_, _) => UpdatePreview();

        _refresh.Tick += (_, _) => { RefreshDeadlines(); RefreshFocus(); };
        _app.Timer.PropertyChanged += OnTimerChanged;
        Deactivated += (_, _) => Close();
        Closed += (_, _) =>
        {
            ClosedAt = DateTime.Now;
            _refresh.Stop();
            _app.Timer.PropertyChanged -= OnTimerChanged;
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        var v = typeof(App).Assembly.GetName().Version;
        Footer.Text = $"DyIsd {v?.ToString(3)} · right-click the tray icon for quick actions";
    }

    void OnTimerChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e) => RefreshFocus();

    /// <summary>Opens under the island, on the same side, with a spring.</summary>
    public void Open(Rect islandArea, string pos)
    {
        Top = islandArea.Top + 44;
        Left = pos switch
        {
            "left" => islandArea.Left,
            "right" => islandArea.Right - Width,
            _ => islandArea.Left + (islandArea.Width - Width) / 2,
        };
        Panel.RenderTransformOrigin = new Point(pos == "left" ? 0.05 : pos == "right" ? 0.95 : 0.5, 0);
        // Fit small laptop screens: scroll inside the panel instead of running off the bottom.
        Scroller.MaxHeight = Math.Min(640, SystemParameters.WorkArea.Bottom - Top - 40);
        Show();
        Activate();
        _refresh.Start();

        if (!SystemParameters.ClientAreaAnimation) return;
        var spring = new SpringEase();
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = spring });
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = spring });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    void RefreshAll()
    {
        RefreshFocus();
        RefreshDeadlines();
        RefreshCalendar();
        RefreshTiles();
        RefreshPositions();
        PauseBtn.Content = _app.IslandPaused ? "Resume island" : "Pause island";
        UpdatePreview();
    }

    // ================= focus =================

    void BuildFocusChips()
    {
        foreach (int m in new[] { 15, 25, 45, 60 })
        {
            var chip = Chip($"{m} min");
            chip.IsChecked = S.FocusMinutes == m;
            chip.Click += (_, _) =>
            {
                S.FocusMinutes = m;
                SettingsStore.Save();
                foreach (ToggleButton c in FocusChips.Children) c.IsChecked = ReferenceEquals(c, chip);
            };
            FocusChips.Children.Add(chip);
        }
    }

    void RefreshFocus()
    {
        var t = _app.Timer;
        FocusTitle.Text = t.IsActive ? $"Focus · {t.RemainingText} left" : "Focus session";
        FocusBtn.Content = t.IsActive ? "End" : "Start";
        FocusBtn.Style = (Style)FindResource(t.IsActive ? "Btn" : "WhiteBtn");
        FocusBtn.IsEnabled = S.Features.FocusTimer;
    }

    void Focus_Click(object sender, RoutedEventArgs e)
    {
        _app.ToggleFocus();
        RefreshFocus();
    }

    // ================= deadlines =================

    static Brush Urgency(TimeSpan left) =>
        ThemeService.Brush(left < TimeSpan.FromHours(3) ? "Bad" : left < TimeSpan.FromDays(1) ? "Orange" : "Good");

    void RefreshDeadlines()
    {
        DeadlineList.Children.Clear();
        var now = DateTime.Now;
        var items = _app.Calendar.Upcoming().Take(8).ToList();
        if (items.Count == 0)
        {
            DeadlineList.Children.Add(new TextBlock
            {
                Text = "Nothing coming up. Add a deadline below.",
                Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(14, 13, 14, 13),
            });
            return;
        }

        bool first = true;
        foreach (var item in items)
        {
            var left = item.AllDay ? item.Start.Date.AddDays(1) - now : item.Start - now;
            var row = new Grid { Margin = new Thickness(0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });

            row.Children.Add(new Border { Background = Urgency(left), CornerRadius = new CornerRadius(0, 3, 3, 0), Margin = new Thickness(0, 8, 0, 8) });

            var text = new StackPanel { Margin = new Thickness(12, 9, 8, 9) };
            text.Children.Add(new TextBlock { Text = item.Title, FontWeight = FontWeights.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock
            {
                Text = When(item) + (!item.IsDeadline ? " · Calendar"
                    : item.Manual?.Repeat == "daily" ? " · daily reminder"
                    : item.Manual?.Repeat == "weekly" ? " · weekly reminder" : ""),
                Style = (Style)FindResource("Sub"),
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var chip = new Border
            {
                Background = ThemeService.Brush("Chip"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = item.AllDay && left < TimeSpan.FromDays(1) ? "Today" : Human(left), Foreground = Urgency(left), FontWeight = FontWeights.SemiBold, FontSize = 12 },
            };
            Grid.SetColumn(chip, 2);
            row.Children.Add(chip);

            if (item.IsDeadline)
            {
                var del = new Button { Style = (Style)FindResource("GhostIcon"), Content = "", ToolTip = "Remove", VerticalAlignment = VerticalAlignment.Center };
                var key = item.Key;
                del.Click += (_, _) =>
                {
                    S.ManualDeadlines.RemoveAll(d => $"m|{d.Title}|{d.Due:o}" == key);
                    SettingsStore.Save();
                    RefreshDeadlines();
                };
                Grid.SetColumn(del, 3);
                row.Children.Add(del);
            }

            var wrap = new Border
            {
                Child = row,
                BorderBrush = ThemeService.Brush("Chip"),
                BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0),
            };
            first = false;
            DeadlineList.Children.Add(wrap);
        }
    }

    static string When(UpcomingItem i)
    {
        if (i.AllDay) return DayName(i.Start) + " · All day";
        return DayName(i.Start) + " · " + i.Start.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    static string DayName(DateTime d)
    {
        var days = (d.Date - DateTime.Today).Days;
        return days == 0 ? "Today" : days == 1 ? "Tomorrow" : d.ToString("ddd, d MMM", CultureInfo.InvariantCulture);
    }

    static string Human(TimeSpan t)
    {
        if (t.TotalMinutes < 60) return $"{Math.Max(1, (int)Math.Round(t.TotalMinutes))} min";
        if (t.TotalHours < 48)
        {
            int h = (int)t.TotalHours, m = (int)Math.Round(t.TotalMinutes - h * 60);
            return m == 0 || h >= 10 ? $"{h}h" : $"{h}h {m}m";
        }
        return $"{(int)Math.Round(t.TotalDays)} days";
    }

    // ---- add form ----

    void BuildDayChips()
    {
        foreach (var (label, offset) in new[] { ("Today", 0), ("Tomorrow", 1), ("Next week", 7) })
        {
            var chip = Chip(label);
            chip.Click += (_, _) =>
            {
                _day = DateTime.Today.AddDays(offset);
                UncheckAll(DayChips);
                chip.IsChecked = true;
                UpdatePreview();
            };
            DayChips.Children.Add(chip);
        }
    }

    void DayPrev_Click(object s, RoutedEventArgs e) => StepDay(-1);
    void DayNext_Click(object s, RoutedEventArgs e) => StepDay(1);

    void StepDay(int by)
    {
        var d = (_day ?? DateTime.Today).AddDays(by);
        if (d < DateTime.Today) return;
        _day = d;
        int offset = (d - DateTime.Today).Days;
        int i = 0;
        foreach (ToggleButton c in DayChips.Children) c.IsChecked = new[] { 0, 1, 7 }[i++] == offset;
        UpdatePreview();
    }

    void BuildTimeChips()
    {
        foreach (var (label, time) in new[] { ("9:00 AM", new TimeSpan(9, 0, 0)), ("5:00 PM", new TimeSpan(17, 0, 0)), ("11:59 PM", new TimeSpan(23, 59, 0)) })
        {
            var chip = Chip(label);
            chip.Click += (_, _) =>
            {
                _time = time;
                UncheckAll(TimeChips);
                chip.IsChecked = true;
                CustomTime.Text = "";
                _time = time;
                UpdatePreview();
            };
            TimeChips.Children.Insert(TimeChips.Children.Count - 1, chip);
        }
    }

    static bool TryParseTime(string s, out TimeSpan t) =>
        TimeSpan.TryParseExact(s.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out t) && t < TimeSpan.FromDays(1);

    DateTime? DraftDue => _day is DateTime d && _time is TimeSpan t ? d.Date + t : null;

    void UpdatePreview()
    {
        DayLabel.Text = _day is DateTime d ? DayName(d) + (d.Date > DateTime.Today.AddDays(1) ? "" : d.ToString(" · d MMM", CultureInfo.InvariantCulture)) : "Pick a day";
        var due = DraftDue;
        bool hasTitle = NewTitle.Text.Trim().Length > 0;
        AddBtn.IsEnabled = hasTitle && due > DateTime.Now;
        AddPreview.Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
        if (!hasTitle) AddPreview.Text = "";
        else if (due == null) AddPreview.Text = _day == null ? "Pick a day" : "Pick a time";
        else if (due <= DateTime.Now) { AddPreview.Text = "That time has already passed"; AddPreview.Foreground = ThemeService.Brush("Bad"); }
        else AddPreview.Text = $"Due {DayName(due.Value)} at {due.Value.ToString("h:mm tt", CultureInfo.InvariantCulture)}. " +
                               CalendarService.Describe(_before, _repeat, _repeatAt, _extra);

        ExtraDayLabel.Text = DayName(_extraDay);
        ExtraAdd.IsEnabled = TryParseTime(ExtraTime.Text, out var et) && _extraDay.Date + et > DateTime.Now && (due == null || _extraDay.Date + et < due);
    }

    // ---- reminders ----

    void BuildReminderChips()
    {
        foreach (var (label, minutes) in new[] { ("1 week", 10080), ("1 day", 1440), ("2 hours", 120), ("30 min", 30), ("When due", 0) })
        {
            var chip = Chip(label);
            chip.IsChecked = _before.Contains(minutes);
            chip.Click += (_, _) =>
            {
                if (chip.IsChecked == true) _before.Add(minutes);
                else _before.Remove(minutes);
                UpdatePreview();
            };
            BeforeChips.Children.Add(chip);
        }

        foreach (var (label, value) in new[] { ("Off", "none"), ("Daily", "daily"), ("Weekly", "weekly") })
        {
            var chip = Chip(label);
            chip.IsChecked = value == _repeat;
            chip.Click += (_, _) =>
            {
                _repeat = value;
                foreach (var c in RepeatChips.Children)
                    if (c is ToggleButton t) t.IsChecked = ReferenceEquals(t, chip);
                RepeatTimeRow.Visibility = value == "none" ? Visibility.Collapsed : Visibility.Visible;
                UpdatePreview();
            };
            RepeatChips.Children.Insert(RepeatChips.Children.Count - 1, chip);
        }
    }

    void ExtraPrev_Click(object s, RoutedEventArgs e)
    {
        if (_extraDay > DateTime.Today) _extraDay = _extraDay.AddDays(-1);
        UpdatePreview();
    }

    void ExtraNext_Click(object s, RoutedEventArgs e)
    {
        _extraDay = _extraDay.AddDays(1);
        UpdatePreview();
    }

    void ExtraAdd_Click(object s, RoutedEventArgs e)
    {
        if (!TryParseTime(ExtraTime.Text, out var t)) return;
        var at = _extraDay.Date + t;
        if (!_extra.Contains(at)) _extra.Add(at);
        RefreshExtraChips();
        UpdatePreview();
    }

    void RefreshExtraChips()
    {
        ExtraDates.Children.Clear();
        foreach (var at in _extra.OrderBy(d => d).ToList())
        {
            var chip = new Button
            {
                Style = (Style)FindResource("Btn"),
                Padding = new Thickness(10, 4, 8, 4),
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = "Remove",
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = DayName(at) + at.ToString(" · h:mm tt", CultureInfo.InvariantCulture), FontSize = 12 },
                        new TextBlock { Text = "\uE711", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 9, Margin = new Thickness(8, 1, 0, 0), Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            chip.Click += (_, _) =>
            {
                _extra.Remove(at);
                RefreshExtraChips();
                UpdatePreview();
            };
            ExtraDates.Children.Add(chip);
        }
    }

    void ResetReminders()
    {
        _before.Clear();
        foreach (var m in DefaultBefore) _before.Add(m);
        int i = 0;
        int[] order = { 10080, 1440, 120, 30, 0 };
        foreach (ToggleButton c in BeforeChips.Children) c.IsChecked = _before.Contains(order[i++]);
        _repeat = "none";
        foreach (var c in RepeatChips.Children)
            if (c is ToggleButton t) t.IsChecked = (string)t.Content == "Off";
        RepeatTimeRow.Visibility = Visibility.Collapsed;
        RepeatTime.Text = "";
        _repeatAt = new TimeSpan(9, 0, 0);
        _extra.Clear();
        ExtraTime.Text = "";
        RefreshExtraChips();
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        if (DraftDue is not DateTime due || NewTitle.Text.Trim().Length == 0) return;
        S.ManualDeadlines.Add(new ManualDeadline
        {
            Title = NewTitle.Text.Trim(),
            Due = due,
            RemindBeforeMinutes = _before.OrderByDescending(m => m).ToList(),
            Repeat = _repeat,
            RepeatAt = _repeatAt,
            RemindOn = _extra.Where(d => d < due).OrderBy(d => d).ToList(),
            Created = DateTime.Now,
        });
        SettingsStore.Save();
        NewTitle.Text = "";
        CustomTime.Text = "";
        _day = null;
        _time = null;
        UncheckAll(DayChips);
        UncheckAll(TimeChips);
        ResetReminders();
        RefreshDeadlines();
        UpdatePreview();
        _app.Island?.ShowMessage("", "Good", "Deadline added", 200, 1600);
    }

    // ---- calendar link ----

    void RefreshCalendar()
    {
        bool hasLink = S.CalendarUrl.Trim().Length > 0;
        bool ok = hasLink && _app.Calendar.Status.StartsWith("Connected");
        CalDot.Fill = !hasLink ? new SolidColorBrush(Color.FromRgb(0x63, 0x63, 0x66)) : ThemeService.Brush(ok ? "Good" : "Bad");
        CalBtn.Content = hasLink ? "Remove" : "Paste link";
        CalTitle.Text = !hasLink ? "No calendar connected" : ok ? "Google Calendar connected" : "Calendar link not working";
        CalSub.Text = !hasLink
            ? "In Google Calendar: Settings → your calendar → Integrate calendar → copy \"Secret address in iCal format\", then press Paste link."
            : _app.Calendar.Status + (ok ? " Classes remind you 1 hour and 10 min before." : "");
    }

    async void Calendar_Click(object sender, RoutedEventArgs e)
    {
        if (S.CalendarUrl.Trim().Length > 0)
        {
            S.CalendarUrl = "";
            SettingsStore.Save();
            await _app.Calendar.RefreshAsync();
            RefreshCalendar();
            RefreshDeadlines();
            return;
        }

        string text = "";
        try { text = Clipboard.GetText().Trim(); } catch { }
        bool looksRight = (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
                          && (text.Contains("ical", StringComparison.OrdinalIgnoreCase) || text.Contains(".ics", StringComparison.OrdinalIgnoreCase));
        if (!looksRight)
        {
            CalTitle.Text = "Copy the link first";
            CalSub.Text = "Your clipboard doesn't have a calendar link. Copy the \"Secret address in iCal format\" from Google Calendar, then press Paste link again.";
            CalDot.Fill = ThemeService.Brush("Orange");
            return;
        }

        S.CalendarUrl = text;
        SettingsStore.Save();
        CalTitle.Text = "Checking the link…";
        CalSub.Text = "";
        CalBtn.IsEnabled = false;
        await _app.Calendar.RefreshAsync();
        CalBtn.IsEnabled = true;
        RefreshCalendar();
        RefreshDeadlines();
    }

    // ================= feature tiles =================

    sealed record TileDef(string Name, string Glyph, Color Color, Func<bool> Get, Action<bool> Set);

    static readonly TileDef[] TileDefs =
    {
        new("Now playing", "", Color.FromRgb(0xFF, 0x37, 0x5F), () => S.Features.Media, v => S.Features.Media = v),
        new("AI apps", "", Color.FromRgb(0xD9, 0x77, 0x57), () => S.Features.AiApps, v => S.Features.AiApps = v),
        new("Volume", "", Color.FromRgb(0x0A, 0x84, 0xFF), () => S.Features.Volume, v => S.Features.Volume = v),
        new("Brightness", "", Color.FromRgb(0xFF, 0x9F, 0x0A), () => S.Features.Brightness, v => S.Features.Brightness = v),
        new("Battery", "", Color.FromRgb(0x30, 0xD1, 0x58), () => S.Features.Battery, v => S.Features.Battery = v),
        new("Downloads", "", Color.FromRgb(0x0A, 0x84, 0xFF), () => S.Features.Downloads, v => S.Features.Downloads = v),
        new("Clipboard", "", Color.FromRgb(0x5E, 0x5C, 0xE6), () => S.Features.Clipboard, v => S.Features.Clipboard = v),
        new("Earbuds", "", Color.FromRgb(0x64, 0xD2, 0xFF), () => S.Features.Earbuds, v => S.Features.Earbuds = v),
        new("Mic & camera", "", Color.FromRgb(0xFF, 0x9F, 0x0A), () => S.Features.Privacy, v => S.Features.Privacy = v),
        new("Deadlines", "", Color.FromRgb(0xFF, 0x45, 0x3A), () => S.Features.Deadlines, v => S.Features.Deadlines = v),
        new("Focus timer", "", Color.FromRgb(0xBF, 0x5A, 0xF2), () => S.Features.FocusTimer, v => S.Features.FocusTimer = v),
    };

    void BuildTiles()
    {
        foreach (var def in TileDefs)
        {
            var circle = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), VerticalAlignment = VerticalAlignment.Center };
            circle.Child = new TextBlock { Text = def.Glyph, Style = (Style)FindResource("Icon"), FontSize = 15, Foreground = Brushes.White };
            var name = new TextBlock { Text = def.Name, FontWeight = FontWeights.SemiBold, FontSize = 13 };
            var state = new TextBlock { Style = (Style)FindResource("Sub") };
            var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(name);
            text.Children.Add(state);
            var content = new DockPanel();
            DockPanel.SetDock(circle, Dock.Left);
            content.Children.Add(circle);
            content.Children.Add(text);

            var tile = new Border
            {
                Background = ThemeService.Brush("Card"), CornerRadius = new CornerRadius(16), Padding = new Thickness(11, 10, 11, 10),
                Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand, Child = content, MinHeight = 56,
                Tag = (def, circle, state),
            };
            tile.MouseEnter += (_, _) => tile.Background = ThemeService.Brush("CardHover");
            tile.MouseLeave += (_, _) => tile.Background = ThemeService.Brush("Card");
            tile.MouseLeftButtonUp += (_, _) =>
            {
                def.Set(!def.Get());
                SettingsStore.Save();
                RefreshTiles();
                RefreshFocus();
            };
            Tiles.Children.Add(tile);
        }
        Tiles.Margin = new Thickness(0, 0, -8, -8); // tiles carry their own right/bottom gap
    }

    void RefreshTiles()
    {
        foreach (Border tile in Tiles.Children)
        {
            var (def, circle, state) = ((TileDef, Border, TextBlock))tile.Tag;
            bool on = def.Get();
            circle.Background = on ? ThemeService.Solid(def.Color) : ThemeService.Brush("ChipOn");
            state.Text = on ? "On" : "Off";
        }
    }

    // ================= position =================

    void BuildPositions()
    {
        foreach (var pos in new[] { "left", "center", "right" })
        {
            var screen = new Border { Width = 44, Height = 14, CornerRadius = new CornerRadius(3), Background = ThemeService.Brush("Chip") };
            screen.Child = new Border
            {
                Width = 14, Height = 5, CornerRadius = new CornerRadius(2.5), Background = Brushes.White, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(3, 2, 3, 0),
                HorizontalAlignment = pos == "left" ? HorizontalAlignment.Left : pos == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Center,
            };
            var stack = new StackPanel();
            stack.Children.Add(screen);
            stack.Children.Add(new TextBlock { Text = char.ToUpper(pos[0]) + pos[1..], HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0), FontWeight = FontWeights.SemiBold, FontSize = 12.5 });
            var btn = new Button { Style = (Style)FindResource("Btn"), Content = stack, Tag = pos, Padding = new Thickness(0, 8, 0, 7), Margin = new Thickness(1.5, 0, 1.5, 0) };
            btn.Click += (_, _) =>
            {
                S.Position = pos;
                SettingsStore.Save();
                RefreshPositions();
                Close(); // the island moved; reopen under it from the tray if needed
            };
            PosButtons.Children.Add(btn);
        }
    }

    void RefreshPositions()
    {
        foreach (Button b in PosButtons.Children)
            b.Background = (string)b.Tag == S.Position ? ThemeService.Brush("ChipOn") : Brushes.Transparent;
    }

    // ================= options =================

    void BuildOptions()
    {
        AddOption("Replace the Windows volume pop-up", "Only the island shows when you press the volume keys.",
            () => S.HideWindowsVolumePopup, v => S.HideWindowsVolumePopup = v);
        AddOption("Show copied text", "Off: just says \"Copied\". Passwords are never shown.",
            () => S.ClipboardShowText, v => S.ClipboardShowText = v);
        AddOption("Hide in full-screen", "Games and videos stay clean. Volume still shows.",
            () => S.HideInFullscreen, v => S.HideInFullscreen = v);

        var startup = AddOption("Start with Windows", null, () => false, _ => { });
        startup.IsEnabled = false;
        _ = LoadStartupAsync(startup);
    }

    async Task LoadStartupAsync(ToggleButton sw)
    {
        sw.IsChecked = await StartupService.IsEnabledAsync();
        sw.IsEnabled = true;
        sw.Click += async (_, _) =>
        {
            var message = await StartupService.SetAsync(sw.IsChecked == true);
            if (message != null)
            {
                sw.IsChecked = false;
                _app.Island?.ShowMessage("", "Orange", message, 420, 5000);
            }
        };
    }

    ToggleButton AddOption(string title, string? help, Func<bool> get, Action<bool> set)
    {
        var sw = new ToggleButton { Style = (Style)FindResource("Switch"), IsChecked = get(), VerticalAlignment = VerticalAlignment.Center };
        if (title != "Start with Windows")
        {
            sw.Click += (_, _) =>
            {
                set(sw.IsChecked == true);
                SettingsStore.Save();
            };
        }
        var text = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13.5 });
        if (help != null) text.Children.Add(new TextBlock { Text = help, Style = (Style)FindResource("Sub") });
        var row = new DockPanel { Margin = new Thickness(14, 11, 14, 11) };
        DockPanel.SetDock(sw, Dock.Right);
        row.Children.Add(sw);
        row.Children.Add(text);
        Options.Children.Add(new Border
        {
            Child = row,
            BorderBrush = ThemeService.Brush("Chip"),
            BorderThickness = new Thickness(0, Options.Children.Count == 0 ? 0 : 1, 0, 0),
        });
        return sw;
    }

    // ================= footer =================

    void Pause_Click(object sender, RoutedEventArgs e)
    {
        _app.IslandPaused = !_app.IslandPaused;
        PauseBtn.Content = _app.IslandPaused ? "Resume island" : "Pause island";
    }

    void Quit_Click(object sender, RoutedEventArgs e) => _app.Quit();

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ================= helpers =================

    ToggleButton Chip(string text) => new() { Style = (Style)FindResource("Chip"), Content = text };

    static void UncheckAll(Panel panel)
    {
        foreach (var c in panel.Children)
            if (c is ToggleButton t) t.IsChecked = false;
    }
}
