using System;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace DyIsd.Services;

/// <summary>
/// Reads and changes the laptop screen brightness through WMI.
/// Only works for built-in laptop screens; external monitors are ignored.
/// </summary>
public sealed class BrightnessService : IDisposable
{
    public event Action<int>? Changed;
    public bool Supported { get; private set; }
    public int Level { get; private set; }

    readonly Dispatcher _ui = Application.Current.Dispatcher;
    ManagementEventWatcher? _watcher;

    public void Start()
    {
        try
        {
            Level = Read();
            _watcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"),
                new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _watcher.EventArrived += (_, e) =>
            {
                try
                {
                    int v = Convert.ToInt32(e.NewEvent.Properties["Brightness"].Value);
                    _ui.BeginInvoke(() =>
                    {
                        Level = v;
                        Changed?.Invoke(v);
                    });
                }
                catch { }
            };
            _watcher.Start();
            Supported = true;
        }
        catch (Exception ex)
        {
            Supported = false;
            Log.Write("brightness not available: " + ex.Message);
        }
    }

    static int Read()
    {
        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
        foreach (ManagementObject o in searcher.Get())
            return Convert.ToInt32(o["CurrentBrightness"]);
        throw new InvalidOperationException("no built-in display");
    }

    public void Step(int percent)
    {
        if (!Supported) return;
        int next = Math.Clamp(Level + percent, 0, 100);
        Level = next;
        Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (ManagementObject o in searcher.Get())
                {
                    o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)next });
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("brightness set", ex);
            }
        });
    }

    public void Dispose()
    {
        try { _watcher?.Stop(); _watcher?.Dispose(); } catch { }
    }
}
