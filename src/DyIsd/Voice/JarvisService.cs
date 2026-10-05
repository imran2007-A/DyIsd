using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DyIsd.Island;
using DyIsd.Settings;

namespace DyIsd.Voice;

/// <summary>
/// Jarvis: hold Ctrl + Space (or say "Jarvis …") and talk. What you say is turned into text on
/// this laptop (Whisper), matched against a long list of commands (no AI), and done.
/// The island shows that it's listening, what it heard and what it did.
/// </summary>
public sealed class JarvisService : IDisposable
{
    public JarvisState State { get; } = new();
    /// <summary>Model downloaded / downloading / ready changed (for the Control Center).</summary>
    public event Action? StatusChanged;

    readonly App _app;
    readonly IslandController _island;
    readonly AudioInput _audio = new();
    readonly SpeechEngine _speech = new();
    readonly CommandRunner _runner;
    readonly Dispatcher _ui = Application.Current.Dispatcher;
    readonly DispatcherTimer _armTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    bool _ptt, _busy, _downloading;
    DateTime _armedUntil;
    Reply? _pending;
    DateTime _pendingUntil;
    DateTime _lastLevel;

    static AppSettings S => SettingsStore.Current;

    public JarvisService(App app, IslandController island)
    {
        _app = app;
        _island = island;
        _runner = new CommandRunner(app);
        _armTimer.Tick += (_, _) =>
        {
            _armTimer.Stop();
            if (_busy || _ptt) return;
            _armedUntil = default;
            _pending = null;
            _island.EndJarvis();
        };
    }

    public bool ModelReady => File.Exists(SpeechEngine.ModelPath);
    public bool Downloading => _downloading;
    public double DownloadProgress { get; private set; }
    public string Runtime => _speech.Runtime;

    public void Start()
    {
        AppCatalog.Load();
        _audio.Phrase += samples => _ui.BeginInvoke(() => _ = OnPhraseAsync(samples));
        _audio.Level += level =>
        {
            // ~20 updates a second is plenty for the wave.
            if (DateTime.Now - _lastLevel < TimeSpan.FromMilliseconds(45)) return;
            _lastLevel = DateTime.Now;
            _ui.BeginInvoke(() => State.Level = level);
        };
        _speech.Downloading += p => _ui.BeginInvoke(() =>
        {
            DownloadProgress = p;
            _island.ShowJarvisCard("\uE896", "Accent", "Downloading Jarvis's ears", "One time · stays on this laptop", $"{(int)(p * 100)}%", null, null, 320, 60000);
            StatusChanged?.Invoke();
        });
        Apply();
    }

    /// <summary>Settings changed: open or close the microphone to match.</summary>
    public void Apply()
    {
        bool wake = S.JarvisEnabled && S.JarvisWakeWord && ModelReady;
        try
        {
            if (wake)
            {
                _audio.Open();
                _audio.Listening = true;
            }
            else
            {
                _audio.Listening = false;
                if (!_ptt) _audio.Close();
            }
        }
        catch (Exception ex)
        {
            Log.Error("jarvis mic", ex);
        }
        // Load the model into memory now, so the first command isn't slow.
        if (S.JarvisEnabled && ModelReady) _ = WarmUpAsync();
    }

    async Task WarmUpAsync()
    {
        try
        {
            await _speech.EnsureStartedAsync();
            StatusChanged?.Invoke();
        }
        catch { }
    }

    // ---------------- push to talk ----------------

    public void PttDown()
    {
        if (!S.JarvisEnabled || _ptt) return;
        if (!ModelReady)
        {
            OfferDownload();
            return;
        }
        try
        {
            _audio.Open();
        }
        catch (Exception ex)
        {
            Log.Error("jarvis mic", ex);
            _island.ShowJarvisCard("\uE720", "Bad", "Can't use the microphone", "Settings › Privacy › Microphone › let desktop apps use it", null, null, null, 400, 6000);
            Chime.Error();
            return;
        }
        _ptt = true;
        _audio.BeginTake();
        _armTimer.Stop();
        ShowListening("Listening…");
        Chime.Listen();
    }

    public void PttUp()
    {
        if (!_ptt) return;
        _ptt = false;
        var samples = _audio.EndTake();
        if (!(S.JarvisWakeWord && S.JarvisEnabled)) _audio.Close();
        if (samples.Length < AudioInput.Rate * 35 / 100)
        {
            _island.EndJarvis(); // a tap, not a command
            return;
        }
        _ = HearAsync(samples, requireWake: false);
    }

    // ---------------- always listening ----------------

    async Task OnPhraseAsync(float[] samples)
    {
        if (_busy || _ptt || !S.JarvisEnabled) return;
        if (_app.Calls.State.IsActive) return; // never act on what you say to people on a call
        bool armed = DateTime.Now < _armedUntil;
        await HearAsync(samples, requireWake: !armed);
    }

    static readonly Regex Wake = new(@"^\W*(?:(?:hey|hi|ok|okay|yo|oh|uh|um|so)\W+){0,2}(?:jarvis|jervis|javis|jarvas|jarves|jarvus|jarviss|charvis|jarbis|jaris|travis)\b[\s,.!?:;-]*", RegexOptions.IgnoreCase);

    async Task HearAsync(float[] samples, bool requireWake)
    {
        _busy = true;
        if (!requireWake) ShowThinking();
        try
        {
            string text = await _speech.TranscribeAsync(samples);
            if (requireWake)
            {
                var m = Wake.Match(text);
                if (!m.Success) return; // just people talking
                Log.Write("jarvis heard: " + text);
                text = text[m.Length..].Trim();
                if (IsNothing(text))
                {
                    Arm("Yes?");
                    return;
                }
                ShowThinking();
            }
            else
            {
                Log.Write("jarvis heard: " + text);
                var m = Wake.Match(text);
                if (m.Success) text = text[m.Length..].Trim();
            }

            if (IsNothing(text))
            {
                ShowReply("", new Reply("\uE720", "Orange", "Didn't hear anything", Failed: true));
                return;
            }
            await ActAsync(text);
        }
        catch (Exception ex)
        {
            Log.Error("jarvis", ex);
            ShowReply("", new Reply("\uE783", "Bad", "Jarvis hit a problem · see log", Failed: true));
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Whisper's "nothing was said" outputs.</summary>
    static bool IsNothing(string text)
    {
        var t = Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9 ]", "").Trim();
        return t.Length == 0 || t is "you" or "bye" or "thanks for watching" or "thank you for watching" or "so" or "uh" or "um" or "hmm";
    }

    void Arm(string prompt)
    {
        _armedUntil = DateTime.Now.AddSeconds(8);
        ShowListening(prompt);
        Chime.Listen();
        _armTimer.Stop();
        _armTimer.Start();
    }

    async Task ActAsync(string text)
    {
        _armedUntil = default;
        _armTimer.Stop();
        var cmds = CommandParser.Parse(text, DateTime.Now);

        // Answering an "are you sure?"
        if (_pending != null)
        {
            var pending = _pending;
            _pending = null;
            if (DateTime.Now < _pendingUntil)
            {
                if (cmds[0].Kind == "yes") { ShowReply(text, await pending.Confirm!()); return; }
                if (cmds[0].Kind == "cancel") { ShowReply(text, new Reply("\uE711", "Accent", "Cancelled")); return; }
            }
        }

        var reply = await _runner.RunAsync(cmds);
        if (reply.Confirm != null)
        {
            _pending = reply;
            _pendingUntil = DateTime.Now.AddSeconds(10);
            _armedUntil = _pendingUntil; // "yes" works without saying "Jarvis" again
            _island.ShowJarvisCard(reply.Glyph, reply.Brush, reply.Text, $"“{text}” · say yes or no", null, "Yes", "No", 380, 10000);
            Chime.Listen();
            _armTimer.Stop();
            _armTimer.Interval = TimeSpan.FromSeconds(10);
            _armTimer.Start();
            return;
        }
        ShowReply(text, reply);
    }

    /// <summary>The island's Yes / No buttons on a confirmation.</summary>
    public async void Confirm(bool yes)
    {
        var pending = _pending;
        _pending = null;
        _armedUntil = default;
        if (pending?.Confirm == null) { _island.EndJarvis(); return; }
        ShowReply("", yes ? await pending.Confirm() : new Reply("\uE711", "Accent", "Cancelled"));
    }

    // ---------------- island ----------------

    void ShowListening(string text)
    {
        State.Text = text;
        State.IsListening = true;
        _island.ShowJarvis(State);
    }

    void ShowThinking()
    {
        State.Text = "Got it…";
        State.IsListening = false;
        _island.ShowJarvis(State);
    }

    void ShowReply(string heard, Reply r)
    {
        _armTimer.Interval = TimeSpan.FromSeconds(8);
        if (r.Silent && !r.Failed)
        {
            // Volume, brightness and media show their own pop-ups, which replace Jarvis's.
            // If nothing replaces it (volume was already there), close it shortly after.
            _ = Task.Delay(600).ContinueWith(_ => _ui.BeginInvoke(_island.EndJarvis));
            Chime.Done();
            return;
        }
        string? sub = heard.Length > 0 ? $"“{heard}”" : null;
        _island.ShowJarvisCard(r.Glyph, r.Brush, r.Text, sub, null, null, null, 0, r.Failed ? 5000 : 3600);
        if (r.Failed) Chime.Error(); else Chime.Done();
    }

    // ---------------- first-time download ----------------

    void OfferDownload()
    {
        if (_downloading) return;
        _island.ShowJarvisCard("\uE720", "Accent", "Jarvis needs a one-time download", "Speech model · 470 MB · runs on this laptop", null, "Download", "Later", 400, 12000);
    }

    public async void StartDownload()
    {
        if (_downloading || ModelReady) return;
        _downloading = true;
        StatusChanged?.Invoke();
        try
        {
            await _speech.EnsureStartedAsync();
            _island.ShowJarvisCard("\uE73E", "Good", "Jarvis is ready", "Hold Ctrl + Space and talk, or say “Jarvis”", null, null, null, 380, 6000);
            Chime.Done();
        }
        catch (Exception ex)
        {
            _island.ShowJarvisCard("\uE783", "Bad", "Download failed", ex.Message, null, null, null, 380, 6000);
            Chime.Error();
        }
        finally
        {
            _downloading = false;
            StatusChanged?.Invoke();
            Apply();
        }
    }

    public void Dispose()
    {
        _audio.Dispose();
        _speech.Dispose();
    }
}
