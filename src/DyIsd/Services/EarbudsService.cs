using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace DyIsd.Services;

/// <summary>
/// Pops a card when Bluetooth earbuds or headphones connect or disconnect, with battery level
/// when Windows knows it.
/// </summary>
public sealed class EarbudsService
{
    /// <summary>Name, connected?, battery percent (null if unknown).</summary>
    public event Action<string, bool, int?>? Changed;

    // Windows' "Bluetooth battery level" device property.
    const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    readonly Dispatcher _ui = Application.Current.Dispatcher;
    readonly Dictionary<string, (string Name, bool Connected)> _known = new();
    DeviceWatcher? _watcher;
    bool _ready;

    public void Start()
    {
        try
        {
            // Every paired Bluetooth device; we check which are audio and connected.
            _watcher = DeviceInformation.CreateWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
            _watcher.Added += (w, d) => _ = CheckAsync(d.Id);
            _watcher.Updated += (w, d) => _ = CheckAsync(d.Id);
            _watcher.EnumerationCompleted += (_, _) => _ready = true;
            _watcher.Start();
        }
        catch (Exception ex)
        {
            Log.Error("earbuds", ex);
        }
    }

    async Task CheckAsync(string id)
    {
        try
        {
            using var dev = await BluetoothDevice.FromIdAsync(id);
            if (dev == null || dev.ClassOfDevice.MajorClass != BluetoothMajorClass.AudioVideo) return;
            bool connected = dev.ConnectionStatus == BluetoothConnectionStatus.Connected;
            string name = dev.Name;

            bool changed;
            lock (_known)
            {
                changed = !_known.TryGetValue(id, out var old) || old.Connected != connected;
                _known[id] = (name, connected);
            }
            if (!changed || !_ready) return; // don't pop for devices that were already connected at startup

            // Show the card right away, then again with the battery level once Windows knows it.
            _ = _ui.BeginInvoke(() => Changed?.Invoke(name, connected, null));
            if (!connected) return;
            int? battery = await BatteryAsync(name);
            if (battery != null) _ = _ui.BeginInvoke(() => Changed?.Invoke(name, true, battery));
        }
        catch (ArgumentException) { }  // not a Bluetooth device (Windows lists other devices here too)
        catch (System.Runtime.InteropServices.COMException) { } // Bluetooth was just switched off
        catch (Exception ex)
        {
            Log.Error("earbuds check", ex);
        }
    }

    static async Task<int?> BatteryAsync(string name)
    {
        try
        {
            // Windows needs a moment after connecting before it knows the battery level.
            await Task.Delay(2500);
            var devices = await DeviceInformation.FindAllAsync("", new[] { BatteryKey }, DeviceInformationKind.Device);
            foreach (var d in devices)
            {
                if (!d.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
                if (d.Properties.TryGetValue(BatteryKey, out var v) && v is byte b) return b;
            }
        }
        catch { }
        return null;
    }
}
