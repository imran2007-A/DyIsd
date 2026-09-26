using System;
using System.Windows;
using System.Windows.Media;

namespace DyIsd.Controls;

/// <summary>Circular progress ring, drawn by hand. Value goes from 0 to 1, starting at 12 o'clock.</summary>
public sealed class RingProgress : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = Reg(nameof(Value), typeof(double), 0.0);
    public static readonly DependencyProperty ThicknessProperty = Reg(nameof(Thickness), typeof(double), 2.5);
    public static readonly DependencyProperty TrackProperty = Reg(nameof(Track), typeof(Brush), null);
    public static readonly DependencyProperty FillProperty = Reg(nameof(Fill), typeof(Brush), null);

    static DependencyProperty Reg(string name, Type t, object? def) =>
        DependencyProperty.Register(name, t, typeof(RingProgress), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        double t = Thickness, r = (size - t) / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);

        if (Track != null) dc.DrawEllipse(null, new Pen(Track, t), c, r, r);

        double v = Math.Clamp(Value, 0, 1);
        if (v <= 0 || Fill == null) return;
        var pen = new Pen(Fill, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (v >= 0.9999)
        {
            dc.DrawEllipse(null, pen, c, r, r);
            return;
        }

        double a = v * 2 * Math.PI;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(c.X, c.Y - r), false, false);
            ctx.ArcTo(new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a)), new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}
