using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DyIsd.Settings;

namespace DyIsd.Voice;

/// <summary>
/// The optional AI brain (Groq's free tier). Only used when the rules don't understand you:
/// it either rewrites what you said as plain Jarvis commands ("make it less bright" → "brightness
/// down"), which then go through the normal rules (so "are you sure?" still applies), or gives
/// a short answer to a question. Nothing is sent unless you've added your own key.
/// </summary>
public static class Brain
{
    const string Url = "https://api.groq.com/openai/v1/chat/completions";
    public const string DefaultModel = "openai/gpt-oss-20b";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static AppSettings S => SettingsStore.Current;

    public static bool Enabled => S.BrainEnabled && Key().Length > 0;

    /// <summary>What the brain decided: commands to run, and/or something to say.</summary>
    public sealed record Answer(List<string> Do, string Say);

    // ---------- the key, locked to your Windows account ----------

    public static void SaveKey(string key)
    {
        key = key.Trim();
        S.GroqKey = key.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
        SettingsStore.Save();
    }

    public static string Key()
    {
        if (string.IsNullOrEmpty(S.GroqKey)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(S.GroqKey), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }

    /// <summary>"gsk_…abcd" for showing in settings.</summary>
    public static string Masked()
    {
        var k = Key();
        return k.Length < 10 ? "" : k[..4] + "…" + k[^4..];
    }

    // ---------- asking ----------

    static string SystemPrompt(string context) => $$"""
        You are Jarvis, the voice assistant inside DyIsd on a Windows 11 laptop. The user spoke a
        request that the built-in rules did not understand. Reply with JSON only:
        {"do": ["command", ...], "say": "text"}

        "do": zero to four commands, each written EXACTLY in one of these forms (fill in the parts):
          open APP | close APP | switch to APP | open SITE (youtube, gmail, github...) | search for TEXT | google TEXT
          play SONG on youtube | play SONG on apple music | play my NAME playlist | play | pause | next song | previous song
          volume to N | volume up | volume down | mute | unmute | brightness to N | brightness up | brightness down
          turn wifi on/off | turn bluetooth on/off | dark mode | light mode | play on headphones | play on speakers
          take a screenshot | lock the pc | minimize | maximize | new tab | close tab | scroll up | scroll down
          set a timer for N minutes | remind me to TEXT at TIME | remind me to TEXT in N minutes
          type TEXT | press KEYS | message NAME saying TEXT | call NAME | ask claude TEXT | start NAME mode
          open PAGE settings (wifi, bluetooth, display, sound...) | open FOLDER (downloads, documents, desktop...)
          empty the recycle bin | shut down the pc | restart the pc | put the pc to sleep
        Use only apps, modes and contacts from the context. Never invent a command form.
        "say": if the user asked a question or for something you can't do with commands, answer in at
        most 2 short sentences (under 160 characters), plain text, no markdown. If you return commands,
        "say" may be "" or a 2-4 word confirmation.
        If the request is unclear, return {"do": [], "say": "a short question asking what they meant"}.

        Context:
        {{context}}
        """;

    public static async Task<Answer?> AskAsync(string heard, string context, CancellationToken ct = default)
    {
        var key = Key();
        if (key.Length == 0) return null;
        var body = new Dictionary<string, object>
        {
            ["model"] = string.IsNullOrWhiteSpace(S.GroqModel) ? DefaultModel : S.GroqModel,
            ["messages"] = new object[]
            {
                new { role = "system", content = SystemPrompt(context) },
                new { role = "user", content = heard },
            },
            ["temperature"] = 0.2,
            ["max_completion_tokens"] = 400,
            ["response_format"] = new { type = "json_object" },
        };
        if ((body["model"] as string ?? "").StartsWith("openai/gpt-oss")) body["reasoning_effort"] = "low";

        using var req = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var started = DateTime.Now;
        try
        {
            using var res = await Http.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                Log.Write($"brain: HTTP {(int)res.StatusCode} {Trim(json, 200)}");
                throw new BrainException((int)res.StatusCode switch
                {
                    401 => "Groq key isn't valid · check Control Center",
                    429 => "Groq's free limit reached · try again later",
                    _ => "Groq didn't answer",
                });
            }
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            var answer = Parse(content);
            Log.Write($"brain ({(DateTime.Now - started).TotalMilliseconds:0} ms): \"{heard}\" → do [{string.Join(" | ", answer.Do)}] say \"{answer.Say}\"");
            return answer;
        }
        catch (BrainException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Write("brain: " + ex.Message);
            throw new BrainException("Couldn't reach Groq · are you online?");
        }
    }

    public sealed class BrainException(string message) : Exception(message);

    /// <summary>Reads the model's JSON, forgiving stray text around it.</summary>
    internal static Answer Parse(string content)
    {
        int a = content.IndexOf('{'), b = content.LastIndexOf('}');
        if (a < 0 || b <= a) return new Answer(new(), Clean(content));
        try
        {
            using var doc = JsonDocument.Parse(content[a..(b + 1)]);
            var root = doc.RootElement;
            var cmds = new List<string>();
            if (root.TryGetProperty("do", out var d) && d.ValueKind == JsonValueKind.Array)
                cmds = d.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length > 0).Take(4).ToList();
            string say = root.TryGetProperty("say", out var s) && s.ValueKind == JsonValueKind.String ? Clean(s.GetString()!) : "";
            return new Answer(cmds, say);
        }
        catch
        {
            return new Answer(new(), "");
        }
    }

    static string Clean(string s) => Trim(s.Replace("**", "").Replace("\n", " ").Trim(), 220);
    static string Trim(string s, int n) => s.Length > n ? s[..(n - 1)] + "…" : s;

    /// <summary>Checks a key with a tiny request. Null = works, otherwise what's wrong.</summary>
    public static async Task<string?> TestAsync()
    {
        try
        {
            var r = await AskAsync("say hi", "(test)");
            return r == null ? "No key" : null;
        }
        catch (BrainException ex) { return ex.Message; }
    }
}
