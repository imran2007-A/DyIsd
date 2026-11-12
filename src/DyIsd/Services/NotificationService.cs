using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DyIsd.Island;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DyIsd.Services;

/// <summary>
/// Recent Windows notifications (WhatsApp messages, mail, reminders) for the Home panel's
/// Notifications tab. Uses Windows' own notification listener, which asks you once for
/// permission and only works in the installed app (Windows ties it to the app's identity).
/// Read only when the tab is open; nothing is kept or sent anywhere.
/// </summary>
public static class NotificationService
{
    public static async Task<(List<NoteItem> Items, string Status)> ReadAsync()
    {
        if (!Native.Win32.IsPackaged) return (new(), "Works in the installed DyIsd (Install.bat)");
        try
        {
            var listener = UserNotificationListener.Current;
            var access = listener.GetAccessStatus();
            if (access == UserNotificationListenerAccessStatus.Unspecified) access = await listener.RequestAccessAsync();
            if (access != UserNotificationListenerAccessStatus.Allowed)
                return (new(), "Allow it in Settings › Privacy › Notifications");
            var list = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            var items = new List<NoteItem>();
            foreach (var n in list.OrderByDescending(n => n.CreationTime))
            {
                try
                {
                    var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                    var texts = binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new();
                    if (texts.Count == 0) continue;
                    string app = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
                    var when = n.CreationTime.LocalDateTime;
                    items.Add(new NoteItem(app, texts[0], string.Join(" · ", texts.Skip(1)),
                        when.Date == DateTime.Today ? when.ToString("HH:mm") : when.ToString("d MMM")));
                }
                catch { }
            }
            return (items, "");
        }
        catch (Exception ex)
        {
            Log.Error("notifications", ex);
            return (new(), "Couldn't read notifications");
        }
    }
}
