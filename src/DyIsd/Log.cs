using System;
using System.IO;

namespace DyIsd;

/// <summary>Tiny file logger. Logs live in %LOCALAPPDATA%\DyIsd\log.txt.</summary>
internal static class Log
{
    static readonly object Gate = new();

    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DyIsd");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                var file = Path.Combine(Dir, "log.txt");
                if (File.Exists(file) && new FileInfo(file).Length > 512 * 1024) File.Delete(file);
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    public static void Error(string where, Exception ex) => Write($"[{where}] {ex.GetType().Name}: {ex.Message}");
}
