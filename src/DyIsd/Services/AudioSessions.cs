using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace DyIsd.Services;

/// <summary>Which apps are making sound right now (e.g. a ringtone). Safe to call from a background thread.</summary>
public static class AudioSessions
{
    public static HashSet<int> SoundingProcessIds(float threshold = 0.01f)
    {
        var pids = new HashSet<int>();
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var role in new[] { Role.Multimedia, Role.Communications })
            {
                try
                {
                    using var dev = en.GetDefaultAudioEndpoint(DataFlow.Render, role);
                    var mgr = dev.AudioSessionManager;
                    mgr.RefreshSessions();
                    var sessions = mgr.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            var s = sessions[i];
                            if (s.AudioMeterInformation.MasterPeakValue > threshold) pids.Add((int)s.GetProcessID);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Write("audio sessions: " + ex.Message);
        }
        return pids;
    }
}
