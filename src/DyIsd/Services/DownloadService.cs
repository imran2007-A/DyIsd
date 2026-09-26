using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using DyIsd.Island;
using DyIsd.Native;

namespace DyIsd.Services;

/// <summary>
/// Watches the Downloads folder. Browsers write to a temporary file (.crdownload for
/// Chrome/Edge/Brave, .part for Firefox) and rename it when done, so that's what we look for.
/// </summary>
public sealed class DownloadService : IDisposable
{
    public DownloadState State { get; } = new();
    public event Action? ActiveChanged;
    /// <summary>Full path of the finished file.</summary>
    public event Action<string>? Completed;
    public string Folder { get; private set; } = "";

    static readonly string[] TempExtensions = { ".crdownload", ".part", ".partial", ".download", ".opdownload" };
    readonly Dispatcher _ui = Application.Current.Dispatcher;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    FileSystemWatcher? _watcher;
    string? _current;
    int _missingTicks;

    public void Start()
    {
        Folder = Win32.DownloadsFolder();
        if (!Directory.Exists(Folder)) return;
        try
        {
            _watcher = new FileSystemWatcher(Folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };
            _watcher.Created += (_, e) => _ui.BeginInvoke(() => OnTempSeen(e.FullPath));
            _watcher.Changed += (_, e) => _ui.BeginInvoke(() => OnTempSeen(e.FullPath));
            _watcher.Renamed += (_, e) => _ui.BeginInvoke(() => OnRenamed(e.OldFullPath, e.FullPath));
            _watcher.Deleted += (_, e) => _ui.BeginInvoke(() => OnDeleted(e.FullPath));
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Log.Error("downloads", ex);
        }
        _timer.Tick += (_, _) => Update();
        _timer.Start();
    }

    static bool IsTemp(string path) => TempExtensions.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase));

    void OnTempSeen(string path)
    {
        if (!IsTemp(path)) return;
        if (_current != path)
        {
            _current = path;
            SetActive(true);
        }
        Update();
    }

    void OnRenamed(string oldPath, string newPath)
    {
        if (IsTemp(newPath))
        {
            // Chrome renames "Unconfirmed 123.crdownload" to "notes.pdf.crdownload".
            if (_current == null || _current == oldPath)
            {
                _current = newPath;
                SetActive(true);
            }
            Update();
            return;
        }

        if (!IsTemp(oldPath)) return;
        if (_current == oldPath)
        {
            _current = null;
            SetActive(false);
        }
        Completed?.Invoke(newPath);
    }

    void OnDeleted(string path)
    {
        if (path != _current) return;
        _current = null;
        SetActive(false); // cancelled
    }

    void Update()
    {
        if (_current == null) return;
        if (!File.Exists(_current))
        {
            if (++_missingTicks > 6)
            {
                _current = null;
                SetActive(false);
            }
            return;
        }
        _missingTicks = 0;
        long size = 0;
        try { size = new FileInfo(_current).Length; } catch { }
        State.FileName = CleanName(_current);
        State.ShortText = FormatSize(size);
        State.SizeText = FormatSize(size) + " downloaded";
    }

    void SetActive(bool active)
    {
        _missingTicks = 0;
        if (State.IsActive == active) return;
        State.IsActive = active;
        if (active) State.ActiveSince = System.DateTime.Now;
        ActiveChanged?.Invoke();
    }

    static string CleanName(string path)
    {
        var name = Path.GetFileName(path);
        foreach (var ext in TempExtensions)
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) name = name[..^ext.Length];
        return name.StartsWith("Unconfirmed ", StringComparison.OrdinalIgnoreCase) ? "Starting download…" : name;
    }

    static string FormatSize(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" :
        bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} MB" :
        $"{bytes / 1024.0:0} KB";

    public void Dispose() => _watcher?.Dispose();
}
