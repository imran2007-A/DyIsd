using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DyIsd.Controls;

/// <summary>Three dots circling: "an AI is working on something".</summary>
public sealed class Thinking : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Thinking), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    double _angle;

    public Thinking()
    {
        _timer.Tick += (_, _) => { _angle = (_angle + 9) % 360; InvalidateVisual(); };
        Loaded += (_, _) => { if (SystemParameters.ClientAreaAnimation) _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double orbit = size * 0.3, dot = size * 0.13;
        double[] opacity = { 1, 0.7, 0.45 };
        for (int i = 0; i < 3; i++)
        {
            double a = (_angle - i * 120) * Math.PI / 180;
            dc.PushOpacity(opacity[i]);
            dc.DrawEllipse(Fill, null, new Point(c.X + orbit * Math.Cos(a), c.Y + orbit * Math.Sin(a)), dot, dot);
            dc.Pop();
        }
    }
}
