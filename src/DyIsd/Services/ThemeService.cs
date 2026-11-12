using System.Windows;
using System.Windows.Media;

namespace DyIsd.Services;

/// <summary>
/// The island is always black like Apple's, so colors are fixed. These are Apple's system colors
/// for dark backgrounds.
/// </summary>
public static class ThemeService
{
    public static void Init()
    {
        var r = Application.Current.Resources;
        Set(r, "IslandBg", Colors.Black);
        Set(r, "IslandBorder", Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        Set(r, "IslandFg", Colors.White);
        Set(r, "IslandMute", Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
        Set(r, "IslandTrack", Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        Set(r, "IslandHover", Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
        Set(r, "IslandBtn", Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        Set(r, "Panel", Color.FromRgb(0x0B, 0x0B, 0x0C));
        Set(r, "Card", Color.FromRgb(0x1C, 0x1C, 0x1E));
        Set(r, "CardHover", Color.FromRgb(0x24, 0x24, 0x26));
        Set(r, "Chip", Color.FromRgb(0x2C, 0x2C, 0x2E));
        Set(r, "ChipOn", Color.FromRgb(0x3A, 0x3A, 0x3C));
        Set(r, "Accent", Color.FromRgb(0x0A, 0x84, 0xFF));
        Set(r, "Good", Color.FromRgb(0x30, 0xD1, 0x58));
        Set(r, "Bad", Color.FromRgb(0xFF, 0x45, 0x3A));
        Set(r, "Orange", Color.FromRgb(0xFF, 0x9F, 0x0A));
        Set(r, "Yellow", Color.FromRgb(0xFF, 0xD6, 0x0A));
        Set(r, "Purple", Color.FromRgb(0xBF, 0x5A, 0xF2));
    }

    static void Set(ResourceDictionary r, string key, Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        r[key] = b;
    }

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    public static Brush Solid(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
