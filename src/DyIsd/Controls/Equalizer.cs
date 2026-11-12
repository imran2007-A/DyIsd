using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DyIsd.Controls;

/// <summary>Four bouncing bars shown next to the album art while music plays.</summary>
public sealed class Equalizer : FrameworkElement
{
    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying), typeof(bool), typeof(Equalizer),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Equalizer)d).UpdateTimer()));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Equalizer), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsPlaying { get => (bool)GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    static readonly double[] Speed = { 7.1, 9.3, 6.2, 8.4 };
    static readonly double[] Offset = { 0.0, 1.3, 2.1, 0.7 };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    double _time;

    public Equalizer()
    {
        _timer.Tick += (_, _) => { _time += 0.04; InvalidateVisual(); };
        Loaded += (_, _) => UpdateTimer();
        Unloaded += (_, _) => _timer.Stop();
    }

    void UpdateTimer()
    {
        if (IsPlaying && IsLoaded && SystemParameters.ClientAreaAnimation) _timer.Start();
        else _timer.Stop();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double h = ActualHeight, w = ActualWidth;
        if (h <= 0 || Fill == null) return;
        const double bar = 3, gap = 2.5;
        double x = (w - (4 * bar + 3 * gap)) / 2;
        for (int i = 0; i < 4; i++)
        {
            double k = IsPlaying ? 0.25 + 0.75 * Math.Abs(Math.Sin(_time * Speed[i] + Offset[i])) : 0.28;
            if (IsPlaying && !SystemParameters.ClientAreaAnimation) k = 0.7;
            double bh = Math.Max(bar, h * k);
            dc.DrawRoundedRectangle(Fill, null, new Rect(x, (h - bh) / 2, bar, bh), 1.5, 1.5);
            x += bar + gap;
        }
    }
}
