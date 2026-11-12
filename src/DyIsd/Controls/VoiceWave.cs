using System;
using System.Windows;
using System.Windows.Media;

namespace DyIsd.Controls;

/// <summary>Live voice bars for calls: taller when you talk, flat when you're quiet.</summary>
public sealed class VoiceWave : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(VoiceWave), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(VoiceWave), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    // Middle bars move most, like a voice waveform.
    static readonly double[] Shape = { 0.45, 0.8, 1.0, 0.7, 0.4 };
    readonly double[] _shown = new double[5];
    readonly Random _rng = new();

    protected override void OnRender(DrawingContext dc)
    {
        double h = ActualHeight, w = ActualWidth;
        if (h <= 0) return;
        const double bar = 3, gap = 2.5;
        double x = (w - (5 * bar + 4 * gap)) / 2;
        for (int i = 0; i < 5; i++)
        {
            double target = 0.18 + 0.82 * Math.Clamp(Level, 0, 1) * Shape[i] * (0.75 + 0.5 * _rng.NextDouble());
            _shown[i] += (target - _shown[i]) * 0.5; // smooth
            double bh = Math.Max(bar, h * Math.Min(1, _shown[i]));
            dc.DrawRoundedRectangle(Fill, null, new Rect(x, (h - bh) / 2, bar, bh), 1.5, 1.5);
            x += bar + gap;
        }
    }
}
