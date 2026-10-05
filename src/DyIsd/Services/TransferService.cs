using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using DyIsd.Island;
using DyIsd.Settings;
using Microsoft.Win32;

namespace DyIsd.Services;

/// <summary>
/// Bluetooth and Nearby Share file transfers on the island, with progress.
///
/// Windows has no way for apps to ask about these, so DyIsd reads the progress bar off the
/// transfer window (Bluetooth File Transfer, the Share window, or the "receiving" notification)
/// with UI Automation. Files that arrive through Nearby Share into a folder other than Downloads
/// are also noticed. Best effort: what each window looks like is logged to tune it.
/// </summary>
public sealed class TransferService : IDisposable
{
    public TransferState State { get; } = new();
    public event Action? ActiveChanged;
    /// <summary>A transfer finished: title ("Sent over Bluetooth"), file name.</summary>
    public event Action<string, string>? Done;

    static readonly Regex Windows = new(@"^(bluetooth file transfer|nearby shar|share|new notification|sharing)", RegexOptions.IgnoreCase);
    static readonly Regex Relevant = new(@"nearby|shar|bluetooth|receiv|sending|sent|transfer", RegexOptions.IgnoreCase);
    static readonly Regex FileLike = new(@"\.[A-Za-z0-9]{1,5}$");

    readonly Dispatcher _ui = Application.Current.Dispatcher;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    FileSystemWatcher? _nearShare;
    bool _busy;
    int _misses;
    double _lastProgress;
    string _logged = "";

    public void Start()
    {
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        WatchNearShareFolder();
    }

    void Poll()
    {
        if (_busy) return;
        if (!SettingsStore.Current.Features.Transfers)
        {
            if (State.IsActive) Finish(false);
            return;
        }
        _busy = true;
        Task.Run(Scan).ContinueWith(t =>
        {
            _busy = false;
            if (t.Exception != null) Log.Error("transfer", t.Exception.InnerException ?? t.Exception);
        });
    }

    void Scan()
    {
        var windows = Uia.WindowsOf("fsquirt").Concat(Uia.TopWindows(w =>
        {
            var n = w.Current.Name ?? "";
            return Windows.IsMatch(n);
        })).ToList();

        foreach (var w in windows)
        {
            string wname;
            try { wname = w.Current.Name ?? ""; } catch { continue; }
            var nodes = Uia.Find(w, ControlType.ProgressBar, ControlType.Text);
            var bar = nodes.FirstOrDefault(n => n.Type == ControlType.ProgressBar.ProgrammaticName);
            if (bar == null) continue;
            var texts = nodes.Where(n => n.Type == ControlType.Text.ProgrammaticName && n.Name.Length > 0).Select(n => n.Name).ToList();
            bool bluetooth = wname.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) || texts.Any(t => t.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase));
            if (!bluetooth && !Relevant.IsMatch(wname + " " + string.Join(" ", texts))) continue;

            var all = string.Join(" | ", texts.Take(8));
            if (all != _logged)
            {
                _logged = all;
                Log.Write($"transfer sees \"{wname}\": {all}");
            }

            bool receiving = texts.Any(t => Regex.IsMatch(t, @"receiv|incoming|from ", RegexOptions.IgnoreCase));
            string title = (receiving ? "Receiving" : "Sending") + (bluetooth ? " · Bluetooth" : " · Nearby Share");
            string detail = texts.FirstOrDefault(t => FileLike.IsMatch(t.Trim())) ?? texts.FirstOrDefault(t => t.Length < 60 && t != wname) ?? "";
            double? progress = Uia.Progress(bar.Element);
            _ui.BeginInvoke(() => Show(title, detail, progress));
            return;
        }
        _ui.BeginInvoke(() =>
        {
            if (State.IsActive && ++_misses >= 2) Finish(_lastProgress >= 0.9);
        });
    }

    void Show(string title, string detail, double? progress)
    {
        _misses = 0;
        State.Title = title;
        State.Detail = detail;
        State.KnowsProgress = progress != null;
        State.Progress = progress ?? 0;
        State.PercentText = progress == null ? "" : $"{(int)Math.Round(progress.Value * 100)}%";
        if (progress != null) _lastProgress = progress.Value;
        if (State.IsActive) return;
        State.Since = DateTime.Now;
        State.IsActive = true;
        ActiveChanged?.Invoke();
    }

    void Finish(bool success)
    {
        _misses = 0;
        if (!State.IsActive) return;
        State.IsActive = false;
        ActiveChanged?.Invoke();
        if (success) Done?.Invoke(State.Title.StartsWith("Receiving") ? "Received" + State.Title[9..] : "Sent" + State.Title[7..], State.Detail);
        _lastProgress = 0;
    }

    /// <summary>Nearby Share can save into its own folder; notice files landing there.</summary>
    void WatchNearShareFolder()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CDP");
            var folder = k?.GetValue("NearShareFileSaveLocation") as string;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            if (string.Equals(Path.GetFullPath(folder).TrimEnd('\\'), Path.GetFullPath(Native.Win32.DownloadsFolder()).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
            _nearShare = new FileSystemWatcher(folder) { EnableRaisingEvents = true };
            _nearShare.Created += (_, e) => _ui.BeginInvoke(async () =>
            {
                await Task.Delay(1500);
                if (SettingsStore.Current.Features.Transfers && File.Exists(e.FullPath)) Done?.Invoke("Received · Nearby Share", Path.GetFileName(e.FullPath));
            });
            Log.Write("watching Nearby Share folder: " + folder);
        }
        catch (Exception ex)
        {
            Log.Write("nearby share folder: " + ex.Message);
        }
    }

    public void Dispose() => _nearShare?.Dispose();
}
