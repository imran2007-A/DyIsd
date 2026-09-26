using System;
using System.Threading.Tasks;
using DyIsd.Native;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace DyIsd.Services;

/// <summary>Turns "start with Windows" on or off, for both the installed and the portable version.</summary>
public static class StartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string TaskId = "DyIsdStartup"; // must match packaging/AppxManifest.xml

    public static async Task<bool> IsEnabledAsync()
    {
        try
        {
            if (Win32.IsPackaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("DyIsd") != null;
        }
        catch (Exception ex)
        {
            Log.Error("startup read", ex);
            return false;
        }
    }

    /// <summary>Returns a message to show the user if Windows refused, otherwise null.</summary>
    public static async Task<string?> SetAsync(bool on)
    {
        try
        {
            if (Win32.IsPackaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                if (!on)
                {
                    task.Disable();
                    return null;
                }
                var state = await task.RequestEnableAsync();
                return state == StartupTaskState.DisabledByUser
                    ? "Windows blocked this. Turn DyIsd on in Windows Settings > Apps > Startup."
                    : null;
            }

            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue("DyIsd", $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue("DyIsd", false);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("startup set", ex);
            return "Couldn't change this: " + ex.Message;
        }
    }
}
