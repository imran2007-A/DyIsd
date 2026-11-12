using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DyIsd.Controls;

/// <summary>How the island's eyes look.</summary>
public enum Mood
{
    /// <summary>White dots, blinking now and then.</summary>
    Idle,
    /// <summary>White dots drifting up and to the side, like thinking.</summary>
    Think,
    /// <summary>Orange dots glancing left and right: working.</summary>
    Busy,
    /// <summary>Green ^ ^: done, all good.</summary>
    Happy,
    /// <summary>Pink hearts: focus session.</summary>
    Love,
    /// <summary>Yellow, wide open: needs you.</summary>
    Alert,
    /// <summary>Cyan, a little bigger: Jarvis is listening.</summary>
    Listen,
    /// <summary>Flat lines: paused / do not disturb.</summary>
    Sleep,
}

/// <summary>
/// Two little eyes that give the island a face. Drawn by hand (no images). They only redraw
/// while blinking or glancing, a few times a minute, so an always-on face costs next to nothing.
/// </summary>
public sealed class Eyes : FrameworkElement
{
    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(nameof(Mood), typeof(Mood), typeof(Eyes),
        new FrameworkPropertyMetadata(Mood.Idle, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Eyes)d).Restart()));
    public static readonly DependencyProperty EyeSizeProperty = DependencyProperty.Register(nameof(EyeSize), typeof(double), typeof(Eyes),
        new FrameworkPropertyMetadata(6.5, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public Mood Mood { get => (Mood)GetValue(MoodProperty); set => SetValue(MoodProperty, value); }
    public double EyeSize { get => (double)GetValue(EyeSizeProperty); set => SetValue(EyeSizeProperty, value); }

    readonly DispatcherTimer _timer = new(DispatcherPriority.Render);
    // Follows the mouse ~12 times a second while the eyes are on screen; redraws only when it moves.
    readonly DispatcherTimer _follow = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(80) };
    double _lookY, _hop;
    DateTime _born = DateTime.Now;
    readonly Random _rnd = new();
    DateTime _animStart;
    double _blink = 1;     // 1 open, 0 shut
    double _look, _lookFrom, _lookTo; // -1 left … 1 right
    bool _blinking, _glancing;

    public Eyes()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) => Step();
        _follow.Tick += (_, _) => Follow();
        IsVisibleChanged += (_, _) => Restart();
        Unloaded += (_, _) => { _timer.Stop(); _follow.Stop(); };
    }

    double Gap => EyeSize * 1.5;

    protected override Size MeasureOverride(Size available) => new(EyeSize * 2 + Gap + EyeSize, EyeSize * 1.6);

    void Restart()
    {
        _timer.Stop();
        _follow.Stop();
        _blink = 1;
        _blinking = _glancing = false;
        _born = DateTime.Now;
        if (IsVisible && Mood is not (Mood.Sleep or Mood.Busy)) _follow.Start();
        if (!IsVisible || Mood is Mood.Happy or Mood.Sleep or Mood.Love) { InvalidateVisual(); return; }
        Schedule();
        InvalidateVisual();
    }

    /// <summary>Waits until the next blink or glance (no redraws in between).</summary>
    void Schedule()
    {
        double wait = Mood switch
        {
            Mood.Busy => 0.9 + _rnd.NextDouble() * 0.6,
            Mood.Think => 1.6 + _rnd.NextDouble(),
            _ => 2.8 + _rnd.NextDouble() * 3.2,
        };
        _timer.Interval = TimeSpan.FromSeconds(wait);
        _timer.Start();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    struct POINT { public int X, Y; }

    /// <summary>Looks toward the mouse; while Jarvis listens, also hops a little with excitement.</summary>
    void Follow()
    {
        if (!IsVisible || PresentationSource.FromVisual(this) == null) return;
        double lx = _look, ly = _lookY, hop = _hop;
        if (GetCursorPos(out var c))
        {
            var mid = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
            if (!_glancing) lx = Math.Clamp((c.X - mid.X) / 260.0, -1, 1);
            ly = Math.Clamp((c.Y - mid.Y) / 220.0, -1, 1);
        }
        if (Mood == Mood.Listen) hop = Math.Abs(Math.Sin((DateTime.Now - _born).TotalSeconds * 7)); // excited bounce
        else hop = 0;
        if (Math.Abs(lx - _look) + Math.Abs(ly - _lookY) + Math.Abs(hop - _hop) < 0.03) return;
        _look = lx; _lookY = ly; _hop = hop;
        InvalidateVisual();
    }

    void Step()
    {
        if (!_blinking && !_glancing)
        {
            // Busy eyes mostly glance; the others mostly blink.
            bool glance = Mood is Mood.Busy or Mood.Think ? _rnd.NextDouble() < 0.75 : _rnd.NextDouble() < 0.2;
            _animStart = DateTime.Now;
            if (glance)
            {
                _glancing = true;
                _lookFrom = _look;
                _lookTo = Mood == Mood.Think ? (_look > 0 ? -0.4 : 0.8) : _look > 0.2 ? -1 : _look < -0.2 ? (_rnd.NextDouble() < 0.5 ? 0 : 1) : (_rnd.NextDouble() < 0.5 ? -1 : 1);
            }
            else _blinking = true;
            _timer.Interval = TimeSpan.FromMilliseconds(33);
            return;
        }

        double t = (DateTime.Now - _animStart).TotalMilliseconds;
        if (_blinking)
        {
            const double dur = 170;
            _blink = t >= dur ? 1 : Math.Abs(1 - 2 * t / dur) * 0.9 + 0.1;
            if (t >= dur) _blinking = false;
        }
        if (_glancing)
        {
            const double dur = 220;
            double p = Math.Min(1, t / dur);
            p = 1 - Math.Pow(1 - p, 3);
            _look = _lookFrom + (_lookTo - _lookFrom) * p;
            if (t >= dur) _glancing = false;
        }
        InvalidateVisual();
        if (!_blinking && !_glancing) Schedule();
    }

    static readonly Color White = Color.FromRgb(0xF2, 0xF2, 0xF2);
    static readonly Color Orange = Color.FromRgb(0xFF, 0x9F, 0x0A);
    static readonly Color Green = Color.FromRgb(0x30, 0xD1, 0x58);
    static readonly Color Pink = Color.FromRgb(0xFF, 0x37, 0x6E);
    static readonly Color Yellow = Color.FromRgb(0xFF, 0xD6, 0x0A);
    static readonly Color Cyan = Color.FromRgb(0x64, 0xD2, 0xFF);

    protected override void OnRender(DrawingContext dc)
    {
        double s = EyeSize, w = ActualWidth, h = ActualHeight;
        double cy = h / 2, cx = w / 2, half = (Gap + s) / 2;
        var color = Mood switch
        {
            Mood.Busy => Orange, Mood.Happy => Green, Mood.Love => Pink, Mood.Alert => Yellow, Mood.Listen => Cyan, _ => White,
        };
        var fill = new SolidColorBrush(color);
        // A soft halo that fades to nothing, like a small light, not a ring.
        var glow = new RadialGradientBrush(Color.FromArgb(0x55, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B));
        double dx = _look * s * 0.35, dy = (Mood == Mood.Think ? -s * 0.15 : _lookY * s * 0.25) - _hop * s * 0.35;

        foreach (int side in new[] { -1, 1 })
        {
            var c = new Point(cx + side * half + dx, cy + dy);
            switch (Mood)
            {
                case Mood.Happy:
                {
                    // ^ ^
                    var pen = new Pen(fill, s * 0.42) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(c.X - s * 0.6, c.Y + s * 0.3), false, false);
                        ctx.QuadraticBezierTo(new Point(c.X, c.Y - s * 0.75), new Point(c.X + s * 0.6, c.Y + s * 0.3), true, true);
                    }
                    dc.DrawEllipse(glow, null, c, s * 1.4, s * 1.2);
                    dc.DrawGeometry(null, pen, g);
                    break;
                }
                case Mood.Love:
                {
                    double r = s * 0.32;
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(c.X, c.Y + s * 0.62), true, true);
                        ctx.BezierTo(new Point(c.X - s * 1.05, c.Y - s * 0.05), new Point(c.X - r * 1.4, c.Y - s * 0.85), new Point(c.X, c.Y - s * 0.3), true, true);
                        ctx.BezierTo(new Point(c.X + r * 1.4, c.Y - s * 0.85), new Point(c.X + s * 1.05, c.Y - s * 0.05), new Point(c.X, c.Y + s * 0.62), true, true);
                    }
                    dc.DrawEllipse(glow, null, c, s * 1.4, s * 1.4);
                    dc.DrawGeometry(fill, null, g);
                    break;
                }
                case Mood.Sleep:
                    dc.DrawRoundedRectangle(fill, null, new Rect(c.X - s * 0.55, c.Y - s * 0.12, s * 1.1, s * 0.24), s * 0.12, s * 0.12);
                    break;
                default:
                {
                    double rx = s / 2 * (Mood == Mood.Listen ? 1.15 : 1), ry = s / 2 * 1.25 * (Mood == Mood.Alert ? 1.15 : 1) * _blink;
                    dc.DrawEllipse(glow, null, c, rx * 2.4, Math.Max(ry * 2.1, rx * 1.6));
                    dc.DrawEllipse(fill, null, c, rx, Math.Max(ry, 0.6));
                    break;
                }
            }
        }
    }
}
