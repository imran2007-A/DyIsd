using System;
using System.Windows;
using System.Windows.Media;
using DyIsd.Settings;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace DyIsd.Services;

/// <summary>
/// Follows the Windows light/dark setting and accent color, and pushes matching colors into
/// the app's resources. Everything on the island uses these colors.
/// </summary>
public static class ThemeService
{
    public static bool IsDark { get; private set; }
    static UISettings? _ui;

    public static void Init()
    {
        try
        {
            _ui = new UISettings();
            _ui.ColorValuesChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(Apply);
        }
        catch (Exception ex)
        {
            Log.Error("theme", ex);
        }
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General) Application.Current.Dispatcher.BeginInvoke(Apply);
        };
        Apply();
    }

    static bool SystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply()
    {
        var t = SettingsStore.Current.Theme;
        IsDark = t == "dark" || (t != "light" && SystemIsDark());

        // Windows 11 guidance: lighter accent shade on dark backgrounds, darker shade on light ones.
        Color accent = IsDark ? Color.FromRgb(0x60, 0xCD, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8);
        try
        {
            if (_ui != null)
            {
                var c = _ui.GetColorValue(IsDark ? UIColorType.AccentLight2 : UIColorType.AccentDark1);
                accent = Color.FromRgb(c.R, c.G, c.B);
            }
        }
        catch { }

        var r = Application.Current.Resources;
        if (IsDark)
        {
            Set(r, "IslandBg", Color.FromArgb(0xF5, 0x2B, 0x2B, 0x2D));
            Set(r, "IslandBorder", Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            Set(r, "IslandFg", Colors.White);
            Set(r, "IslandMute", Color.FromRgb(0xB9, 0xB9, 0xB9));
            Set(r, "IslandTrack", Color.FromArgb(0x2B, 0xFF, 0xFF, 0xFF));
            Set(r, "IslandHover", Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
            Set(r, "IslandBtn", Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
            Set(r, "OnAccent", Colors.Black);
            Set(r, "Good", Color.FromRgb(0x6C, 0xCB, 0x5F));
            Set(r, "Bad", Color.FromRgb(0xFF, 0x99, 0xA4));
        }
        else
        {
            Set(r, "IslandBg", Color.FromArgb(0xF7, 0xF9, 0xF9, 0xFA));
            Set(r, "IslandBorder", Color.FromArgb(0x14, 0x00, 0x00, 0x00));
            Set(r, "IslandFg", Color.FromRgb(0x1B, 0x1B, 0x1B));
            Set(r, "IslandMute", Color.FromRgb(0x5F, 0x5F, 0x5F));
            Set(r, "IslandTrack", Color.FromArgb(0x1F, 0x00, 0x00, 0x00));
            Set(r, "IslandHover", Color.FromArgb(0x0F, 0x00, 0x00, 0x00));
            Set(r, "IslandBtn", Color.FromArgb(0x0E, 0x00, 0x00, 0x00));
            Set(r, "OnAccent", Colors.White);
            Set(r, "Good", Color.FromRgb(0x0F, 0x7B, 0x0F));
            Set(r, "Bad", Color.FromRgb(0xC4, 0x2B, 0x1C));
        }
        Set(r, "Accent", accent);
    }

    static void Set(ResourceDictionary r, string key, Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        r[key] = b;
    }

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
