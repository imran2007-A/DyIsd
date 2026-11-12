using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DyIsd.Settings;

namespace DyIsd.Voice;

/// <summary>
/// Sharper hearing with your Groq key: what you said to Jarvis goes to Groq's Whisper
/// large-v3-turbo, which is far more accurate than anything that fits on a laptop (the same
/// trick Wispr Flow uses). Only commands are sent: always-listening audio is checked for
/// "Jarvis" on the laptop first, and nothing else leaves it.
/// </summary>
public static class CloudEars
{
    const string Url = "https://api.groq.com/openai/v1/audio/transcriptions";
    const string Model = "whisper-large-v3";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static bool Enabled => SettingsStore.Current.CloudSpeech && Brain.Key().Length > 0;

    /// <summary>Text, or null if Groq couldn't be reached (the caller falls back to the laptop's own).</summary>
    public static async Task<string?> TranscribeAsync(float[] samples, string prompt, CancellationToken ct = default)
    {
        var key = Brain.Key();
        if (key.Length == 0) return null;
        var started = DateTime.Now;
        try
        {
            // The last command's audio, to check the microphone if hearing goes wrong.
            try { File.WriteAllBytes(Path.Combine(Log.Dir, "last-command.wav"), Wav(samples)); } catch { }
            using var form = new MultipartFormDataContent();
            var wav = new ByteArrayContent(Wav(samples));
            wav.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(wav, "file", "jarvis.wav");
            form.Add(new StringContent(Model), "model");
            form.Add(new StringContent("en"), "language");
            form.Add(new StringContent("0"), "temperature");
            form.Add(new StringContent("json"), "response_format");
            form.Add(new StringContent(prompt), "prompt");
            using var req = new HttpRequestMessage(HttpMethod.Post, Url) { Content = form };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var res = await Http.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                Log.Write($"cloud ears: HTTP {(int)res.StatusCode} {(json.Length > 200 ? json[..200] : json)}");
                return null;
            }
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.GetProperty("text").GetString() ?? "";
            Log.Write($"cloud ears ({(DateTime.Now - started).TotalMilliseconds:0} ms): {text.Trim()}");
            return text;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Write("cloud ears: " + ex.Message);
            return null;
        }
    }

    /// <summary>16 kHz mono floats → a 16-bit WAV file in memory.</summary>
    internal static byte[] Wav(float[] samples)
    {
        using var ms = new MemoryStream(44 + samples.Length * 2);
        using var w = new BinaryWriter(ms, Encoding.ASCII);
        int bytes = samples.Length * 2;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(AudioInput.Rate); w.Write(AudioInput.Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
        foreach (var s in samples) w.Write((short)Math.Clamp(s * 32767f, -32768f, 32767f));
        w.Flush();
        return ms.ToArray();
    }
}
