using System;
using System.Windows;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace DyIsd.Services;

/// <summary>
/// Your default microphone: mute it for the whole PC, and read how loud you are right now
/// (drives the green voice waveform during a call).
/// </summary>
public sealed class MicService : IDisposable
{
    public event Action<bool>? MuteChanged;
    public bool IsReady => _device != null;
    public bool Muted { get; private set; }

    readonly Dispatcher _ui = Application.Current.Dispatcher;
    MMDeviceEnumerator? _enumerator;
    MMDevice? _device;

    public void Start()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            Muted = _device.AudioEndpointVolume.Mute;
            _device.AudioEndpointVolume.OnVolumeNotification += d => _ui.BeginInvoke(() =>
            {
                if (d.Muted == Muted) return;
                Muted = d.Muted;
                MuteChanged?.Invoke(Muted);
            });
        }
        catch (Exception ex)
        {
            Log.Write("no microphone: " + ex.Message);
        }
    }

    public void SetMuted(bool muted)
    {
        try
        {
            if (_device != null) _device.AudioEndpointVolume.Mute = muted;
        }
        catch (Exception ex)
        {
            Log.Error("mic mute", ex);
        }
    }

    /// <summary>How loud the mic is right now, 0..1.</summary>
    public float Level
    {
        get
        {
            try { return _device?.AudioMeterInformation.MasterPeakValue ?? 0; }
            catch { return 0; }
        }
    }

    public void Dispose()
    {
        try { _device?.Dispose(); _enumerator?.Dispose(); } catch { }
    }
}
