using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DyIsd.Native;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DyIsd.Services;

public sealed record NotificationInfo(string App, string Title, string Body, ImageSource? Logo);

/// <summary>
/// Reads other apps' notifications (WhatsApp, Discord, Outlook...). Windows only allows this for
/// installed (MSIX) apps, and only after you click "Allow" once.
/// </summary>
public sealed class NotificationService
{
    public event Action<NotificationInfo>? Arrived;
    public string Status { get; private set; } = "Not started";
    public bool Working { get; private set; }

    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1.5) };
    readonly HashSet<uint> _seen = new();
    UserNotificationListener? _listener;
    bool _busy, _hooked;

    /// <summary>Starts listening. Pass askPermission to show the Windows permission prompt.</summary>
    public async Task StartAsync(bool askPermission)
    {
        if (Working) return;
        if (!Win32.IsPackaged)
        {
            Status = "Only works in the installed (MSIX) version.";
            return;
        }

        try
        {
            _listener = UserNotificationListener.Current;
            var access = askPermission ? await _listener.RequestAccessAsync() : _listener.GetAccessStatus();
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                Status = access == UserNotificationListenerAccessStatus.Denied
                    ? "Blocked. Turn it on in Windows Settings > Privacy & security > Notifications."
                    : "Not allowed yet. Click Allow access.";
                return;
            }

            // Everything already in the notification center counts as old.
            foreach (var n in await _listener.GetNotificationsAsync(NotificationKinds.Toast)) _seen.Add(n.Id);

            if (!_hooked)
            {
                _poll.Tick += async (_, _) => await PollAsync();
                _hooked = true;
            }
            _poll.Start();
            Working = true;
            Status = "Working";
        }
        catch (Exception ex)
        {
            Status = "Couldn't start: " + ex.Message;
            Log.Error("notifications", ex);
        }
    }

    public void Stop()
    {
        _poll.Stop();
        Working = false;
        if (Status == "Working") Status = "Turned off";
    }

    async Task PollAsync()
    {
        if (_busy || _listener == null) return;
        _busy = true;
        try
        {
            var list = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var current = new HashSet<uint>();
            foreach (var n in list)
            {
                current.Add(n.Id);
                if (!_seen.Add(n.Id)) continue;
                var info = await BuildAsync(n);
                if (info != null) Arrived?.Invoke(info);
            }
            _seen.IntersectWith(current);
        }
        catch (Exception ex)
        {
            Log.Error("notification poll", ex);
        }
        finally
        {
            _busy = false;
        }
    }

    static async Task<NotificationInfo?> BuildAsync(UserNotification n)
    {
        string app = "App";
        try { app = n.AppInfo.DisplayInfo.DisplayName; } catch { }
        if (app == "DyIsd") return null;

        var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
        var texts = binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (texts == null || texts.Count == 0) return null;

        ImageSource? logo = null;
        try
        {
            var reference = n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64));
            using var ras = await reference.OpenReadAsync();
            using var stream = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 64;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            logo = bmp;
        }
        catch { }

        return new NotificationInfo(app, texts[0], string.Join(" · ", texts.Skip(1)), logo);
    }
}
