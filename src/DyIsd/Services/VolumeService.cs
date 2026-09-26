using System;
using System.Windows;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DyIsd.Services;

/// <summary>
/// Watches and changes the system volume through Windows Core Audio (via the NAudio library).
/// Follows the default speaker, so plugging in headphones keeps working.
/// </summary>
public sealed class VolumeService : IMMNotificationClient, IDisposable
{
    /// <summary>Level 0..1 and muted flag. Raised on the UI thread.</summary>
    public event Action<float, bool>? Changed;

    public bool IsReady => _device != null;
    public float Level { get; private set; }
    public bool Muted { get; private set; }

    readonly Dispatcher _ui = Application.Current.Dispatcher;
    MMDeviceEnumerator? _enumerator;
    MMDevice? _device;

    public void Start()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(this);
            AttachDefault();
        }
        catch (Exception ex)
        {
            Log.Error("volume start", ex);
        }
    }

    void AttachDefault()
    {
        if (_device != null)
        {
            try { _device.AudioEndpointVolume.OnVolumeNotification -= OnVolume; } catch { }
            _device.Dispose();
            _device = null;
        }
        if (_enumerator == null) return;
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _device.AudioEndpointVolume.OnVolumeNotification += OnVolume;
        Level = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
        Muted = _device.AudioEndpointVolume.Mute;
    }

    void OnVolume(AudioVolumeNotificationData d) => _ui.BeginInvoke(() =>
    {
        Level = d.MasterVolume;
        Muted = d.Muted;
        Changed?.Invoke(Level, Muted);
    });

    /// <summary>Changes volume by a number of percent (Windows uses 2 per key press).</summary>
    public void Step(int percent)
    {
        try
        {
            if (_device == null) return;
            var v = _device.AudioEndpointVolume;
            float next = Math.Clamp((float)Math.Round(v.MasterVolumeLevelScalar * 100 + percent) / 100f, 0f, 1f);
            if (v.Mute && percent > 0) v.Mute = false;
            v.MasterVolumeLevelScalar = next;
        }
        catch (Exception ex)
        {
            Log.Error("volume step", ex);
        }
    }

    public void SetLevel(double level)
    {
        try
        {
            if (_device == null) return;
            var v = _device.AudioEndpointVolume;
            if (v.Mute && level > 0) v.Mute = false;
            v.MasterVolumeLevelScalar = (float)Math.Clamp(level, 0, 1);
        }
        catch (Exception ex)
        {
            Log.Error("volume set", ex);
        }
    }

    public void ToggleMute()
    {
        try
        {
            if (_device != null) _device.AudioEndpointVolume.Mute = !_device.AudioEndpointVolume.Mute;
        }
        catch (Exception ex)
        {
            Log.Error("volume mute", ex);
        }
    }

    public static string GlyphFor(float level, bool muted) =>
        muted || level <= 0.001f ? "\uE74F" : level < 0.34f ? "\uE993" : level < 0.67f ? "\uE994" : "\uE995";

    // Called by Windows when devices change (headphones plugged in, etc.).
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow != DataFlow.Render || role != Role.Multimedia) return;
        _ui.BeginInvoke(() =>
        {
            try { AttachDefault(); }
            catch (Exception ex) { Log.Error("volume device", ex); }
        });
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        try
        {
            _enumerator?.UnregisterEndpointNotificationCallback(this);
            _device?.Dispose();
            _enumerator?.Dispose();
        }
        catch { }
    }
}
