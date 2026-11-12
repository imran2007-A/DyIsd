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
    // small.en (~470 MB) hears well enough to run commands on its own. With cloud ears on, the
    // laptop only has to spot "Jarvis", which base.en (~140 MB) does at a third of the memory.
    const string Small = "ggml-small.en.bin", Base = "ggml-base.en.bin";
    const string ModelBase = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    static string PathOf(string name) => Path.Combine(Log.Dir, "models", name);
    static string Wanted => CloudEars.Enabled ? Base : Small;

    /// <summary>The model to load: the one wanted if it's here, else whichever is here.</summary>
    public static string ModelPath
    {
        get
        {
            if (File.Exists(PathOf(Wanted))) return PathOf(Wanted);
            foreach (var n in new[] { Small, Base }) if (File.Exists(PathOf(n))) return PathOf(n);
            return PathOf(Wanted);
        }
    }

    /// <summary>The better-fitting model isn't here yet (fetched quietly in the background).</summary>
    public static bool WantsSwap => !File.Exists(PathOf(Wanted)) && File.Exists(ModelPath);

    public bool Ready => _processor != null;
    public string Runtime { get; private set; } = "";
    string _loaded = "";

    /// <summary>Download progress 0..1 while the model is being fetched.</summary>
    public event Action<double>? Downloading;

    WhisperFactory? _factory;
    WhisperProcessor? _processor;
    readonly SemaphoreSlim _lock = new(1, 1);
    Task? _starting;

    public Task EnsureStartedAsync()
    {
        // Settings changed which model fits: load the other one next time.
        if (_starting is { IsCompleted: true } && _loaded.Length > 0 && _loaded != ModelPath) Unload();
        return _starting ??= StartAsync();
    }

    /// <summary>Frees the model's memory; the next command loads it again.</summary>
    public void Unload()
    {
        _lock.Wait();
        try
        {
            _processor?.Dispose();
            _factory?.Dispose();
            _processor = null;
            _factory = null;
            _starting = null;
            _loaded = "";
        }
        finally { _lock.Release(); }
    }

    bool _swapping;

    /// <summary>Fetches the model that fits the settings without bothering you, then switches to it.</summary>
    public async Task SwapQuietlyAsync()
    {
        if (!WantsSwap || _swapping) return;
        _swapping = true;
        try
        {
            await DownloadAsync(Wanted, quiet: true);
            Unload();
            await EnsureStartedAsync();
        }
        catch (Exception ex) { Log.Error("speech model swap", ex); }
        finally { _swapping = false; }
    }

    async Task StartAsync()
    {
        try
        {
            if (!File.Exists(ModelPath)) await DownloadAsync(Wanted, quiet: false);
            string path = ModelPath;
            // Graphics card first, processor as the fallback.
            // Graphics card first: it hears "Jarvis" in ~0.25 s, the processor took ~2.3 s (it
            // saves ~150 MB, but every command felt laggy).
            RuntimeOptions.RuntimeLibraryOrder = new List<RuntimeLibrary> { RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx };
            await Task.Run(() =>
            {
                _factory = WhisperFactory.FromPath(path);
                _processor = _factory.CreateBuilder()
                    .WithLanguage("en")
                    .WithPrompt(Prompt())
                    .WithNoContext()
                    .WithSingleSegment()
                    .WithNoSpeechThreshold(0.6f)
                    .WithTemperature(0f)
                    .WithThreads(Math.Max(2, Environment.ProcessorCount / 2))
                    .Build();
            });
            Runtime = RuntimeOptions.LoadedLibrary?.ToString() ?? "unknown";
            _loaded = path;
            Log.Write($"jarvis: speech engine ready ({Runtime}, {Path.GetFileName(path)})");
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
    /// this as earlier conversation, which nudges its spelling. Only names: whole example
    /// sentences here used to come back as if you'd said them ("100 divided by 8") when the
    /// microphone only heard noise.
    /// </summary>
    public static string Prompt()
    {
        var names = new List<string>();
        int budget = 300; // characters of names
        foreach (var n in AppCatalog.SpokenNames(50))
        {
            if (budget - n.Length < 0) break;
            names.Add(n);
            budget -= n.Length + 2;
        }
        foreach (var c in Settings.SettingsStore.Current.WhatsAppContacts) names.Add(c.Name);
        return "Jarvis. " + string.Join(", ", names) + ".";
    }

    /// <summary>For Groq: the words Jarvis commands use, then your names (apps, contacts, Discord server).</summary>
    public static string CloudPrompt() =>
        "Jarvis, message, send, saying, Discord, WhatsApp, general, " + Settings.SettingsStore.Current.DiscordServer +
        ", open, close, play, pause, volume, brightness, timer, remind me, search, YouTube, Spotify, Apple Music, Claude. " + Prompt();

    async Task DownloadAsync(string name, bool quiet)
    {
        string target = PathOf(name);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string part = target + ".part";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var resp = await http.GetAsync(ModelBase + name, HttpCompletionOption.ResponseHeadersRead);
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
                if (!quiet && p - lastReported >= 0.01) { lastReported = p; Downloading?.Invoke(p); }
            }
        }
        File.Move(part, target, true);
        Log.Write($"jarvis: speech model downloaded ({name})");
    }

    /// <summary>16 kHz mono audio in, words out. One at a time.</summary>
    public async Task<string> TranscribeAsync(float[] samples, CancellationToken ct = default)
    {
        await EnsureStartedAsync();
        await _lock.WaitAsync(ct);
        try
        {
            var sb = new StringBuilder();
            // (A per-segment "probably noise" filter here threw away real speech on a real mic.)
            await foreach (var seg in _processor!.ProcessAsync(samples, ct)) sb.Append(seg.Text);
            return Clean(sb.ToString());
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// What Whisper invents when it hears only noise: it was trained on subtitled videos, so
    /// silence turns into subtitle credits and "thanks for watching".
    /// </summary>
    static readonly System.Text.RegularExpressions.Regex Invented = new(
        @"subtitles? by|subs by|captions? by|transcri(bed|ption) by|www\.|\.co\.uk|amara\.org|thanks? (you )?for watching|please subscribe|like and subscribe|see you (in the )?next (time|video)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Whisper's habits: bracketed sound tags, invented credits, stray punctuation.</summary>
    public static string Clean(string text)
    {
        if (Invented.IsMatch(text)) return "";
        if (System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), @"^\W*(aplausos|applause|music|musica|silence|risas|laughter)\W*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return "";
        var t = System.Text.RegularExpressions.Regex.Replace(text, @"\[[^\]]*\]|\([^)]*\)|[®™©]", " ");
        // Whisper sometimes loops on a word ("no no no no no…"): keep one.
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\b(\w+)(?:[\s,.!?]+\1\b){2,}[.!?]?", "$1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
    }
}
