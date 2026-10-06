using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace DyIsd.Voice;

/// <summary>
/// Speech to text on your own laptop (Whisper, the same engine Alfred uses). Runs on the
/// graphics card through Vulkan when it can, otherwise on the processor. Nothing is sent
/// anywhere. The model (~470 MB) downloads once, the first time Jarvis is used.
/// </summary>
public sealed class SpeechEngine : IDisposable
{
    const string ModelName = "ggml-small.en.bin";
    const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + ModelName;

    public static string ModelPath => Path.Combine(Log.Dir, "models", ModelName);
    public bool Ready => _processor != null;
    public string Runtime { get; private set; } = "";

    /// <summary>Download progress 0..1 while the model is being fetched.</summary>
    public event Action<double>? Downloading;

    WhisperFactory? _factory;
    WhisperProcessor? _processor;
    readonly SemaphoreSlim _lock = new(1, 1);
    Task? _starting;

    public Task EnsureStartedAsync() => _starting ??= StartAsync();

    async Task StartAsync()
    {
        try
        {
            if (!File.Exists(ModelPath)) await DownloadAsync();
            // Graphics card first, processor as the fallback.
            RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary> { RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx };
            await Task.Run(() =>
            {
                _factory = WhisperFactory.FromPath(ModelPath);
                _processor = _factory.CreateBuilder()
                    .WithLanguage("en")
                    .WithPrompt(Prompt())
                    .WithNoContext()
                    .WithSingleSegment()
                    .WithThreads(Math.Max(2, Environment.ProcessorCount / 2))
                    .Build();
            });
            Runtime = RuntimeOptions.LoadedLibrary?.ToString() ?? "unknown";
            Log.Write($"jarvis: speech engine ready ({Runtime})");
        }
        catch (Exception ex)
        {
            _starting = null; // try again next time
            Log.Error("speech engine", ex);
            throw;
        }
    }

    /// <summary>
    /// Names it should expect, so "Spotify" doesn't come back as "spot if I". Whisper reads
    /// this as earlier conversation, which nudges its spelling.
    /// </summary>
    static string Prompt()
    {
        // Whisper keeps only the last ~220 words of this, so the app names go first (some may be
        // dropped) and the wake word and commands go last, where they're always kept.
        var names = new List<string>();
        int budget = 260; // characters of names
        foreach (var n in AppCatalog.SpokenNames(40))
        {
            if (budget - n.Length < 0) break;
            names.Add(n);
            budget -= n.Length + 2;
        }
        return string.Join(", ", names) + ". " +
               "Jarvis, open Spotify. Jarvis, search YouTube for lofi beats. Type hello and press enter. " +
               "Volume up. Max brightness. Dim the brightness. Close this tab. Scroll down. Play Believer on Apple Music. " +
               "100 divided by 8. Deafen. Undeafen. Bring it back. Mute me. Jarvis, launch Fortnite. Jarvis.";
    }

    async Task DownloadAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        string part = ModelPath + ".part";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var resp = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 488_000_000;
        await using (var src = await resp.Content.ReadAsStreamAsync())
        await using (var dst = File.Create(part))
        {
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            double lastReported = -1;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                done += n;
                double p = (double)done / total;
                if (p - lastReported >= 0.01) { lastReported = p; Downloading?.Invoke(p); }
            }
        }
        File.Move(part, ModelPath, true);
        Log.Write("jarvis: speech model downloaded");
    }

    /// <summary>16 kHz mono audio in, words out. One at a time.</summary>
    public async Task<string> TranscribeAsync(float[] samples, CancellationToken ct = default)
    {
        await EnsureStartedAsync();
        await _lock.WaitAsync(ct);
        try
        {
            var sb = new StringBuilder();
            await foreach (var seg in _processor!.ProcessAsync(samples, ct)) sb.Append(seg.Text);
            return Clean(sb.ToString());
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Whisper's habits: bracketed sound tags and stray punctuation.</summary>
    static string Clean(string text)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(text, @"\[[^\]]*\]|\([^)]*\)", " ");
        return System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
    }
}
