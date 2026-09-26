using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DyIsd.Controls;

/// <summary>Turns the island's height into its corner radius: a full pill when small, softer corners when big.</summary>
public sealed class RadiusConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        new CornerRadius(Math.Min((value is double h ? h : 36) / 2, 28));

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Hides an element when its bound value is null or an empty string.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is null || (value is string s && s.Length == 0) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
