using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DyIsd.Native;
using DyIsd.Services;
using DyIsd.Settings;

namespace DyIsd;

/// <summary>Every change saves immediately; the app listens to SettingsStore.Changed.</summary>
public partial class SettingsWindow : Window
{
    readonly App _app;
    bool _loading = true;

    static AppSettings S => SettingsStore.Current;

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        Load();
        Wire();
        _loading = false;
    }

    async void Load()
    {
        PosLeft.IsChecked = S.Position == "left";
        PosCenter.IsChecked = S.Position == "center";
        PosRight.IsChecked = S.Position == "right";
        ThemeSystem.IsChecked = S.Theme == "system";
        ThemeLight.IsChecked = S.Theme == "light";
        ThemeDark.IsChecked = S.Theme == "dark";
        HideFullscreen.IsChecked = S.HideInFullscreen;

        FMedia.IsChecked = S.Features.Media;
        FVolume.IsChecked = S.Features.Volume;
        HideVolumePopup.IsChecked = S.HideWindowsVolumePopup;
        FBrightness.IsChecked = S.Features.Brightness;
        FBattery.IsChecked = S.Features.Battery;
        FNotifications.IsChecked = S.Features.Notifications;
        FFocus.IsChecked = S.Features.FocusTimer;
        FocusMinutes.Text = S.FocusMinutes.ToString();
        FClipboard.IsChecked = S.Features.Clipboard;
        ClipShowText.IsChecked = S.ClipboardShowText;
        FDeadlines.IsChecked = S.Features.Deadlines;
        FDownloads.IsChecked = S.Features.Downloads;
        CalUrl.Text = S.CalendarUrl;
        CalStatus.Text = _app.Calendar.Status;
        NewDate.SelectedDate = DateTime.Today;
        RefreshDeadlines();
        RefreshNotifStatus();

        if (!_app.Brightness.Supported) BrightnessHelp.Text = "Not available on this screen (works on built-in laptop screens only).";

        var version = typeof(App).Assembly.GetName().Version;
        AboutText.Text = $"Version {version?.ToString(3)} · {(Win32.IsPackaged ? "installed version" : "portable version (notifications need the installer)")}.\n" +
                         "Right-click the DyIsd icon in the taskbar corner for the focus timer, pause and quit.";

        // Read this after the await, then set it without triggering the Checked handler.
        bool startup = await StartupService.IsEnabledAsync();
        _loading = true;
        StartWithWindows.IsChecked = startup;
        _loading = false;
    }

    void Wire()
    {
        void Pos(RadioButton r, string v) => r.Checked += (_, _) => Change(() => S.Position = v);
        Pos(PosLeft, "left");
        Pos(PosCenter, "center");
        Pos(PosRight, "right");

        void Theme(RadioButton r, string v) => r.Checked += (_, _) => Change(() => S.Theme = v);
        Theme(ThemeSystem, "system");
        Theme(ThemeLight, "light");
        Theme(ThemeDark, "dark");

        void Toggle(CheckBox box, Action<bool> set)
        {
            box.Checked += (_, _) => Change(() => set(true));
            box.Unchecked += (_, _) => Change(() => set(false));
        }
        Toggle(HideFullscreen, v => S.HideInFullscreen = v);
        Toggle(FMedia, v => S.Features.Media = v);
        Toggle(FVolume, v => S.Features.Volume = v);
        Toggle(HideVolumePopup, v => S.HideWindowsVolumePopup = v);
        Toggle(FBrightness, v => S.Features.Brightness = v);
        Toggle(FBattery, v => S.Features.Battery = v);
        Toggle(FNotifications, v => S.Features.Notifications = v);
        Toggle(FFocus, v => S.Features.FocusTimer = v);
        Toggle(FClipboard, v => S.Features.Clipboard = v);
        Toggle(ClipShowText, v => S.ClipboardShowText = v);
        Toggle(FDeadlines, v => S.Features.Deadlines = v);
        Toggle(FDownloads, v => S.Features.Downloads = v);

        FNotifications.Checked += async (_, _) =>
        {
            if (_loading) return;
            await _app.Notifications.StartAsync(askPermission: true);
            RefreshNotifStatus();
        };
        FNotifications.Unchecked += (_, _) => RefreshNotifStatus();
        NotifAllow.Click += async (_, _) =>
        {
            await _app.Notifications.StartAsync(askPermission: true);
            RefreshNotifStatus();
        };

        FocusMinutes.LostFocus += (_, _) =>
        {
            if (int.TryParse(FocusMinutes.Text, out int m)) Change(() => S.FocusMinutes = Math.Clamp(m, 1, 180));
            FocusMinutes.Text = S.FocusMinutes.ToString();
        };

        StartWithWindows.Checked += async (_, _) => await SetStartup(true);
        StartWithWindows.Unchecked += async (_, _) => await SetStartup(false);

        CalSave.Click += async (_, _) =>
        {
            S.CalendarUrl = CalUrl.Text.Trim();
            SettingsStore.Save();
            CalStatus.Text = "Loading…";
            CalStatus.Text = await _app.Calendar.RefreshAsync();
        };

        AddDeadline.Click += (_, _) => AddManualDeadline();
        OpenLogs.Click += (_, _) =>
        {
            Directory.CreateDirectory(App.LogFolder);
            try { Process.Start(new ProcessStartInfo(App.LogFolder) { UseShellExecute = true }); } catch { }
        };
        TryIsland.Click += (_, _) => _app.ShowWhatsNext();
    }

    void Change(Action apply)
    {
        if (_loading) return;
        apply();
        SettingsStore.Save();
    }

    async System.Threading.Tasks.Task SetStartup(bool on)
    {
        if (_loading) return;
        var message = await StartupService.SetAsync(on);
        StartupNote.Text = message ?? "";
        StartupNote.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
    }

    void RefreshNotifStatus()
    {
        NotifStatus.Text = S.Features.Notifications ? _app.Notifications.Status : "Turned off";
        NotifAllow.Visibility = S.Features.Notifications && Win32.IsPackaged && !_app.Notifications.Working
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    void RefreshDeadlines()
    {
        var items = S.ManualDeadlines.OrderBy(d => d.Due).ToList();
        DeadlineList.ItemsSource = items;
        NoDeadlines.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void AddManualDeadline()
    {
        DeadlineError.Visibility = Visibility.Collapsed;
        var title = NewTitle.Text.Trim();
        if (title.Length == 0) { ShowError("Type what's due first."); return; }
        if (NewDate.SelectedDate is not DateTime date) { ShowError("Pick a date."); return; }
        if (!TimeSpan.TryParseExact(NewTime.Text.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out var time))
        {
            ShowError("Type the time like 23:59 or 9:30.");
            return;
        }

        var due = date.Date + time;
        if (due < DateTime.Now) { ShowError("That time has already passed."); return; }

        S.ManualDeadlines.Add(new ManualDeadline { Title = title, Due = due });
        SettingsStore.Save();
        NewTitle.Text = "";
        RefreshDeadlines();
    }

    void ShowError(string message)
    {
        DeadlineError.Text = message;
        DeadlineError.Visibility = Visibility.Visible;
    }

    void RemoveDeadline_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ManualDeadline d })
        {
            S.ManualDeadlines.Remove(d);
            SettingsStore.Save();
            RefreshDeadlines();
        }
    }
}
