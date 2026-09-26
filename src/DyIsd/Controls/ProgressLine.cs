using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DyIsd.Controls;

/// <summary>Thin rounded progress bar. Set IsIndeterminate for a sliding "working" animation.</summary>
public sealed class ProgressLine : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = Reg(nameof(Value), typeof(double), 0.0);
    public static readonly DependencyProperty TrackProperty = Reg(nameof(Track), typeof(Brush), null);
    public static readonly DependencyProperty FillProperty = Reg(nameof(Fill), typeof(Brush), null);
    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate), typeof(bool), typeof(ProgressLine),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((ProgressLine)d).UpdateTimer()));

    static DependencyProperty Reg(string name, Type t, object? def) =>
        DependencyProperty.Register(name, t, typeof(ProgressLine), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public bool IsIndeterminate { get => (bool)GetValue(IsIndeterminateProperty); set => SetValue(IsIndeterminateProperty, value); }

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    double _phase;

    public ProgressLine()
    {
        _timer.Tick += (_, _) => { _phase = (_phase + 0.018) % 1; InvalidateVisual(); };
        Loaded += (_, _) => UpdateTimer();
        Unloaded += (_, _) => _timer.Stop();
    }

    void UpdateTimer()
    {
        if (IsIndeterminate && IsLoaded) _timer.Start();
        else _timer.Stop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double rad = h / 2;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), rad, rad);

        if (IsIndeterminate)
        {
            double seg = w * 0.3;
            double x = _phase * (w + seg) - seg;
            double left = Math.Max(0, x), right = Math.Min(w, x + seg);
            if (right > left) dc.DrawRoundedRectangle(Fill, null, new Rect(left, 0, right - left, h), rad, rad);
            return;
        }

        double fw = w * Math.Clamp(Value, 0, 1);
        if (fw > 0) dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, Math.Max(fw, h), h), rad, rad);
    }
}
