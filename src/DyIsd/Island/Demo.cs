using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DyIsd.Services;

namespace DyIsd.Island;

/// <summary>
/// "DyIsd.exe --demo VIEW out.png": shows one island view with sample data, saves a picture of
/// it and quits. For checking how views look without making them happen for real (and while
/// the screen is locked). Views: face, media, media-e, claude, claude-e, home, timer, home-tray.
/// </summary>
public static class Demo
{
    public static async Task RunAsync(string view, string outPng)
    {
        var win = new IslandWindow();
        win.Left = -4000; // off screen; it's rendered to a file
        win.Show();

        var media = new MediaState
        {
            Title = "Golden Hour", Artist = "JVKE", Source = "Spotify", IsPlaying = true, IsActive = true, HasSession = true,
            ArtColor = Color.FromRgb(0xF2, 0x4B, 0x6A), HasTimeline = true, Progress = 0.42, PositionText = "1:23", RemainingText = "-1:46",
            Volume = 0.6, CanSeek = true, CanShuffle = true, CanRepeat = true,
        };
        var art = new LinearGradientBrush(Color.FromRgb(0xFF, 0x8A, 0x5C), Color.FromRgb(0xC2, 0x18, 0x5B), 45);
        art.Freeze();
        media.ArtBrush = art;
        media.BarBrush = ThemeService.Solid(media.ArtColor.Value);

        var claude = new ClaudeState
        {
            Connected = true, IsActive = true, Project = "DyIsd", Task = "Ship the new landing page and fix the glow", Status = view == "claude-e" ? "Editing" : "Done",
            Detail = view == "claude-e" ? "IslandWindow.xaml" : "", IsDone = view != "claude-e", Elapsed = "4:12",
            Mood = view == "claude-e" ? Controls.Mood.Busy : Controls.Mood.Happy,
        };
        var focus = new FocusTimer();
        focus.Start(25);
        var home = new HomeState(claude, media, focus, new Services.ReminderStore()) { Tab = view == "home-tray" ? "tray" : view == "home-rem" ? "rem" : "home" };
        home.SetOpen(true);
        home.AddClip(new ClipInfo(ClipKind.Text, "dotnet build -c Release", 23));

        switch (view)
        {
            case "face": win.ShowView("face", "face", new FaceState(), 84, 30); break;
            case "media": win.ShowView("media-c", "media-c", media, 190, 34); break;
            case "media-e": win.ShowView("media-e", "media-e", media, 366, 208); break;
            case "claude": win.ShowView("claude-c", "claude-c", claude, 260, 34); break;
            case "claude-e": win.ShowView("claude-e", "claude-e", claude, 380, 124); break;
            case "timer": win.ShowView("timer-c", "timer-c", focus, 150, 34); break;
            case "home-rem": win.ShowView("home", "home", home, IslandController.HomeW, 220); break;
            default: win.ShowView("home", "home", home, IslandController.HomeW, IslandController.HomeH); break;
        }

        await Task.Delay(2600); // let the spring animations settle and the glow fade in
        var root = (FrameworkElement)win.Content;
        double w = root.ActualWidth, h = root.ActualHeight;
        var bmp = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // A Windows-blue desktop behind it, so the glow and the black island both show.
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x0B, 0x1A, 0x4A), Color.FromRgb(0x1B, 0x3F, 0xA8), 90), null, new Rect(0, 0, w, h));
        }
        bmp.Render(dv);
        bmp.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPng))!);
        using (var f = File.Create(outPng)) enc.Save(f);
        Log.Write($"demo: saved {view} to {outPng}");
        focus.End();
        win.Close();
    }
}
