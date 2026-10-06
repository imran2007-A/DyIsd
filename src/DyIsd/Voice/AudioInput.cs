using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace DyIsd.Voice;

/// <summary>
/// The microphone, 16 kHz mono (what Whisper wants). Two ways to use it:
///  - Push to talk: BeginTake() while you hold Ctrl+Space, EndTake() returns what you said.
///  - Always listening (for "Jarvis"): cuts the sound into phrases on its own. A phrase starts
///    when it gets louder than the room for a moment and ends after 0.7 s of quiet.
/// Events fire on a background thread.
/// </summary>
public sealed class AudioInput : IDisposable
{
    public const int Rate = 16000;
    const int FrameMs = 30, Frame = Rate * FrameMs / 1000;
    const int PreRollFrames = 10;   // keep 300 ms from before the phrase so the first word isn't cut
    const int StartFrames = 3;      // ~90 ms of sound starts a phrase
    const int EndFrames = 18;       // ~540 ms of quiet ends it
    const int MaxSamples = Rate * 10;

    /// <summary>A phrase heard while always listening.</summary>
    public event Action<float[]>? Phrase;
    /// <summary>How loud you are right now, 0..1 (drives the island's wave).</summary>
    public event Action<double>? Level;

    WaveInEvent? _wave;
    readonly object _gate = new();
    readonly List<float> _pending = new();       // unprocessed samples, less than one frame
    readonly Queue<float[]> _preRoll = new();
    readonly List<float> _phrase = new();
    readonly List<float> _take = new();
    double _floor = 0.01;
    int _loud, _quiet;
    bool _inPhrase, _taking;

    public bool Listening { get; set; }
    public bool IsOpen => _wave != null;

    public void Open()
    {
        if (_wave != null) return;
        _wave = new WaveInEvent { WaveFormat = new WaveFormat(Rate, 16, 1), BufferMilliseconds = FrameMs * 2 };
        _wave.DataAvailable += OnData;
        _wave.RecordingStopped += (_, e) => { if (e.Exception != null) Log.Error("mic", e.Exception); };
        _wave.StartRecording();
        Log.Write("jarvis: microphone open");
    }

    public void Close()
    {
        if (_wave == null) return;
        try { _wave.StopRecording(); } catch { }
        _wave.Dispose();
        _wave = null;
        lock (_gate) { ResetPhrase(); _pending.Clear(); }
        Log.Write("jarvis: microphone closed");
    }

    public void BeginTake()
    {
        lock (_gate)
        {
            _take.Clear();
            // Include the moment just before the key press: people start talking as they press.
            foreach (var f in _preRoll) _take.AddRange(f);
            _taking = true;
        }
    }

    public float[] EndTake()
    {
        lock (_gate)
        {
            _taking = false;
            var a = _take.ToArray();
            _take.Clear();
            return a;
        }
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        lock (_gate)
        {
            for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
                _pending.Add(BitConverter.ToInt16(e.Buffer, i) / 32768f);
            while (_pending.Count >= Frame)
            {
                var frame = _pending.GetRange(0, Frame).ToArray();
                _pending.RemoveRange(0, Frame);
                OnFrame(frame);
            }
        }
    }

    void OnFrame(float[] frame)
    {
        double sum = 0;
        foreach (var s in frame) sum += s * s;
        double rms = Math.Sqrt(sum / frame.Length);
        Level?.Invoke(Math.Min(1, rms * 9));

        if (_taking)
        {
            if (_take.Count < MaxSamples * 2) _take.AddRange(frame);
        }

        _preRoll.Enqueue(frame);
        while (_preRoll.Count > PreRollFrames) _preRoll.Dequeue();

        if (!Listening || _taking)
        {
            ResetPhrase();
            return;
        }

        double threshold = Math.Max(0.015, _floor * 3);
        bool loud = rms > threshold;
        if (!_inPhrase)
        {
            // The room's normal noise level, learned slowly while nobody is talking.
            if (!loud) _floor = _floor * 0.97 + rms * 0.03;
            _loud = loud ? _loud + 1 : 0;
            if (_loud >= StartFrames)
            {
                _inPhrase = true;
                _quiet = 0;
                _phrase.Clear();
                foreach (var f in _preRoll) _phrase.AddRange(f);
            }
            return;
        }

        _phrase.AddRange(frame);
        _quiet = loud ? 0 : _quiet + 1;
        if (_quiet >= EndFrames || _phrase.Count >= MaxSamples)
        {
            var phrase = _phrase.ToArray();
            ResetPhrase();
            // Under ~0.45 s is a cough, a click or our own chime, not a command.
            if (phrase.Length > Rate * 45 / 100) Phrase?.Invoke(phrase);
        }
    }

    void ResetPhrase()
    {
        _inPhrase = false;
        _loud = _quiet = 0;
        _phrase.Clear();
    }

    public void Dispose() => Close();
}
