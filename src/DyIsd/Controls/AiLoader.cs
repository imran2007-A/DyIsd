using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DyIsd.Controls;

/// <summary>
/// "Working" animation for AI apps, drawn in code (no image files, no licenses):
///  - Kind "claude": an orange starburst whose rays breathe in and out while it slowly turns.
///  - Kind "openai": the six-loop knot, spinning with a gentle pulse (ChatGPT, Codex).
/// </summary>
public sealed class AiLoader : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(AiLoader), new FrameworkPropertyMetadata("claude", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(AiLoader), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    // Ray angle (degrees) and length (fraction of the radius). Uneven on purpose, like the real mark.
    static readonly (double Angle, double Length)[] Rays =
    {
        (0, .95), (26, .8), (53, 1), (80, .84), (106, .96), (133, .78), (160, .98),
        (187, .82), (213, .94), (240, .8), (266, 1), (293, .86), (320, .92), (346, .8),
    };

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly DateTime _start = DateTime.Now;

    public AiLoader()
    {
        _timer.Tick += (_, _) => InvalidateVisual();
        Loaded += (_, _) => { if (SystemParameters.ClientAreaAnimation) _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double r = Math.Min(ActualWidth, ActualHeight) / 2;
        if (r <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double t = SystemParameters.ClientAreaAnimation ? (DateTime.Now - _start).TotalSeconds : 0.3;

        if (Kind == "openai") DrawKnot(dc, c, r, t);
        else DrawStarburst(dc, c, r, t);
    }

    void DrawStarburst(DrawingContext dc, Point c, double r, double t)
    {
        var pen = new Pen(Fill, r * 0.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        double spin = t * 0.6;
        for (int i = 0; i < Rays.Length; i++)
        {
            var (deg, len) = Rays[i];
            double breathe = 0.72 + 0.28 * (0.5 + 0.5 * Math.Sin(t * 4.2 + i * 0.9));
            double a = deg * Math.PI / 180 + spin;
            var dir = new Vector(Math.Cos(a), Math.Sin(a));
            dc.DrawLine(pen, c + dir * (r * 0.12), c + dir * (r * len * breathe));
        }
    }

    void DrawKnot(DrawingContext dc, Point c, double r, double t)
    {
        var pen = new Pen(Fill, r * 0.12) { LineJoin = PenLineJoin.Round };
        double pulse = 1 + 0.06 * Math.Sin(t * 3);
        double w = r * 0.46, h = r * 0.98;
        var loop = new Rect(-w / 2 + r * 0.2, -h + r * 0.1, w, h);

        dc.PushTransform(new TranslateTransform(c.X, c.Y));
        dc.PushTransform(new ScaleTransform(pulse, pulse));
        dc.PushTransform(new RotateTransform(t * 2.4 * 180 / Math.PI));
        for (int k = 0; k < 6; k++)
        {
            dc.PushTransform(new RotateTransform(k * 60));
            dc.DrawRoundedRectangle(null, pen, loop, w / 2, w / 2);
            dc.Pop();
        }
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }
}
