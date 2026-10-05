using System;
using System.IO;
using System.Media;

namespace DyIsd.Voice;

/// <summary>
/// Jarvis's soft sounds, made in code (no sound files): a rising blip when it starts
/// listening, a gentle two-note chime when done, a low buzz when something went wrong.
/// </summary>
public static class Chime
{
    static readonly byte[] ListenWav = Make((880, 70), (1320, 90));
    static readonly byte[] DoneWav = Make((1046.5, 90), (1568, 160));
    static readonly byte[] ErrorWav = Make((330, 110), (247, 160));

    public static void Listen() => Play(ListenWav);
    public static void Done() => Play(DoneWav);
    public static void Error() => Play(ErrorWav);

    static void Play(byte[] wav)
    {
        if (!Settings.SettingsStore.Current.JarvisSounds) return;
        try
        {
            using var player = new SoundPlayer(new MemoryStream(wav));
            player.Play(); // doesn't wait
        }
        catch (Exception ex)
        {
            Log.Error("chime", ex);
        }
    }

    /// <summary>Notes (frequency Hz, length ms) as a 16-bit mono WAV, each softly faded in and out.</summary>
    static byte[] Make(params (double Hz, int Ms)[] notes)
    {
        const int rate = 22050;
        int total = 0;
        foreach (var n in notes) total += rate * n.Ms / 1000;
        var samples = new short[total];
        int at = 0;
        foreach (var (hz, ms) in notes)
        {
            int len = rate * ms / 1000;
            for (int i = 0; i < len; i++)
            {
                double t = (double)i / rate;
                double env = Math.Min(1, i / (rate * 0.008)) * Math.Pow(1 - (double)i / len, 1.6); // quick attack, smooth decay
                double v = Math.Sin(2 * Math.PI * hz * t) * 0.8 + Math.Sin(4 * Math.PI * hz * t) * 0.12; // a touch of overtone
                samples[at + i] = (short)(v * env * 0.22 * short.MaxValue);
            }
            at += len;
        }

        using var ms2 = new MemoryStream();
        using var w = new BinaryWriter(ms2);
        int bytes = samples.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + bytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(bytes);
        foreach (var s in samples) w.Write(s);
        return ms2.ToArray();
    }
}
