using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;

namespace DyIsd.Services;

/// <summary>A speaker or headphones you can send sound to.</summary>
public sealed record AudioOutput(string Id, string Name, bool IsDefault)
{
    public string Glyph => Regex.IsMatch(Name, "head|ear|bud|airpod|wh-|wf-", RegexOptions.IgnoreCase) ? "\uE7F6" : "\uE7F5";
    public string Tag => "output:" + Id;
    /// <summary>Not colour-only: the chosen one also gets a tick.</summary>
    public string Check => IsDefault ? "\uE73E" : "";
}

/// <summary>
/// Lists speakers and headphones and switches between them, like AirPlay on the iPhone.
/// Windows has no public call for "make this the default output"; every switcher app uses the
/// same undocumented interface (IPolicyConfig) that the Sound settings page uses.
/// </summary>
public static class AudioOutputs
{
    public static List<AudioOutput> List()
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            string? def = null;
            try { using var d = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); def = d.ID; } catch { }
            return en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(d => { using (d) return new AudioOutput(d.ID, Short(d.FriendlyName), d.ID == def); })
                .OrderByDescending(o => o.IsDefault).ThenBy(o => o.Name)
                .Take(5) // what fits in the island
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error("audio outputs", ex);
            return new List<AudioOutput>();
        }
    }

    /// <summary>"Speakers (Realtek(R) Audio)" → "Speakers · Realtek Audio".</summary>
    static string Short(string name)
    {
        name = Regex.Replace(name, @"\((R|TM)\)", "", RegexOptions.IgnoreCase);
        var m = Regex.Match(name, @"^(.*?)\s*\((.+)\)$");
        return m.Success ? $"{m.Groups[1].Value} · {m.Groups[2].Value}" : name;
    }

    /// <summary>"headphones", "speakers", "boat" → the closest output, by the words in its name.</summary>
    public static AudioOutput? Find(string spoken)
    {
        var all = List();
        spoken = spoken.ToLowerInvariant();
        bool wantsHead = Regex.IsMatch(spoken, "head|ear|bud|airpod");
        return all.FirstOrDefault(o => o.Name.ToLowerInvariant().Contains(spoken))
            ?? all.FirstOrDefault(o => spoken.Split(' ').Any(w => w.Length > 2 && o.Name.ToLowerInvariant().Contains(w)))
            ?? (Regex.IsMatch(spoken, "head|ear|bud|airpod|speaker|laptop")
                ? all.FirstOrDefault(o => (o.Glyph == "\uE7F6") == wantsHead) : null);
    }

    public static bool SetDefault(string id)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            // Music (multimedia), system sounds (console) and calls (communications) all move.
            for (int role = 0; role < 3; role++) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role));
            Log.Write("audio output → " + id);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("audio output switch", ex);
            return false;
        }
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class PolicyConfigClient { }

    // Only SetDefaultEndpoint is used; the others hold its place in the interface.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat(string id, bool def, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(string id);
        [PreserveSig] int SetDeviceFormat(string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string id, bool def, IntPtr defaultPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod(string id, IntPtr period);
        [PreserveSig] int GetShareMode(string id, IntPtr mode);
        [PreserveSig] int SetShareMode(string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility(string id, bool visible);
    }
}
