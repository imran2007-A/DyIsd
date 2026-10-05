using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DyIsd.Controls;
using DyIsd.Native;
using DyIsd.Services;
using DyIsd.Settings;
using Microsoft.Win32;

namespace DyIsd.Island;

/// <summary>
/// The always-on-top window at the top of the screen. It only draws, animates and reports input;
/// IslandController decides what to show.
///
/// Mouse: click opens (expands), right-click jumps to the app, drag moves it to the left, center
/// or right, scroll changes volume. Clicks never go to the app underneath.
/// </summary>
public partial class IslandWindow : Window
{
    public event Action? Clicked;
    public event Action? RightClicked;
    public event Action<string>? ActionClicked;
    public event Action<int, bool>? Wheel;
    /// <summary>Seek bar let go at 0..1.</summary>
    public event Action<double>? Seek;
    /// <summary>Volume slider dragged to 0..1.</summary>
    public event Action<double>? VolumeSet;
    public event Action<string>? Dropped;
    /// <summary>True when the mouse enters the island, false when it leaves.</summary>
    public event Action<bool>? HoverChanged;
    /// <summary>Dragged upward and let go: hide what's showing.</summary>
    public event Action? FlickedUp;
    /// <summary>The small circle beside the island (second activity) was clicked.</summary>
    public event Action? BubbleClicked;

    const double WinWidth = 420, WinHeight = 250, Gap = 14;
    public const double BubbleSize = 34, BubbleGap = 6;

    readonly IEasingFunction _spring = new SpringEase();
    readonly IEasingFunction _smooth = new CubicEase { EasingMode = EasingMode.EaseOut };
    string? _templateKey, _logicalKind;
    bool _shown, _hover;
    string _pos = "center";

    public IslandWindow()
    {
        InitializeComponent();
        Width = WinWidth;
        Height = WinHeight;
        SourceInitialized += (_, _) => SetExStyle(Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE, true);
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(() => ApplyPosition(SettingsStore.Current.Position));
        ApplyPosition(SettingsStore.Current.Position);
    }

    // ---------- window styles ----------

    void SetExStyle(long flags, bool on)
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        long ex = (long)Win32.GetWindowLongPtr(h, Win32.GWL_EXSTYLE);
        ex = on ? ex | flags : ex & ~flags;
        if (flags == (Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE)) ex &= ~Win32.WS_EX_APPWINDOW;
        Win32.SetWindowLongPtr(h, Win32.GWL_EXSTYLE, (IntPtr)ex);
    }

    void EnsureTopmost()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero)
            Win32.SetWindowPos(h, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    // ---------- position ----------

    public void ApplyPosition(string pos)
    {
        _pos = pos;
        var wa = SystemParameters.WorkArea;
        Top = wa.Top;
        Left = pos switch
        {
            "left" => wa.Left,
            "right" => wa.Right - WinWidth,
            _ => wa.Left + (wa.Width - WinWidth) / 2,
        };
        var align = pos switch { "left" => HorizontalAlignment.Left, "right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        Pill.HorizontalAlignment = align;
        Bubble.HorizontalAlignment = align;
        Presenter.HorizontalAlignment = align;
        PrivacyDot.HorizontalAlignment = align;
        Pill.RenderTransformOrigin = new Point(pos == "left" ? 0 : pos == "right" ? 1 : 0.5, 0);
        // Compact views mirror on the right so the album art hugs the screen edge.
        Resources["CompactFlow"] = pos == "right" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    /// <summary>Where the Control Center should appear: under the island, same side.</summary>
    public Rect IslandArea => new(Left, Top, WinWidth, WinHeight);

    // ---------- animation ----------

    int Ms(int ms) => SystemParameters.ClientAreaAnimation ? ms : 0; // respects "Animation effects: off"

    void Animate(IAnimatable target, DependencyProperty dp, double to, int ms = 560, IEasingFunction? ease = null) =>
        target.BeginAnimation(dp, new DoubleAnimation(to, TimeSpan.FromMilliseconds(Ms(ms))) { EasingFunction = ease ?? _spring });

    /// <summary>Content fades in from slightly blurred, like the iPhone island.</summary>
    void BlurIn()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var blur = new BlurEffect { Radius = 8, RenderingBias = RenderingBias.Performance };
        Presenter.Effect = blur;
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)) { EasingFunction = _smooth };
        a.Completed += (_, _) => { if (Presenter.Effect == blur) Presenter.Effect = null; }; // crisp text once settled
        blur.BeginAnimation(BlurEffect.RadiusProperty, a);
        Presenter.Opacity = 0;
        Animate(Presenter, OpacityProperty, 1, 260, _smooth);
    }

    public void ShowView(string templateKey, string logicalKind, object data, double width, double height)
    {
        if (templateKey != _templateKey || logicalKind != _logicalKind)
        {
            bool morph = _shown && _templateKey != null;
            Presenter.ContentTemplate = (DataTemplate)Resources[templateKey];
            Presenter.Content = data;
            BlurIn();
            _templateKey = templateKey;
            _logicalKind = logicalKind;
            if (morph) Wobble();
        }
        else if (!ReferenceEquals(Presenter.Content, data))
        {
            Presenter.Content = data;
        }

        // Lay content out at its final size; the pill clips it while growing.
        Presenter.Width = width - 2;
        Presenter.Height = height - 2;

        if (!_shown)
        {
            _shown = true;
            Pill.BeginAnimation(WidthProperty, null);
            Pill.BeginAnimation(HeightProperty, null);
            Pill.Width = width;
            Pill.Height = height;
            EnsureTopmost();
            Pill.IsHitTestVisible = true;
            Animate(Pill, OpacityProperty, 1, 200, _smooth);
            Animate(PillScale, ScaleTransform.ScaleXProperty, 1);
            Animate(PillScale, ScaleTransform.ScaleYProperty, 1);
            return;
        }

        Animate(Pill, WidthProperty, width);
        Animate(Pill, HeightProperty, height);
    }

    /// <summary>
    /// Liquid morph: when the island turns into something else it squashes a little and
    /// springs back, like a drop of liquid, on top of the size change.
    /// </summary>
    void Wobble()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        MorphScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.05, 1, TimeSpan.FromMilliseconds(620)) { EasingFunction = _spring });
        MorphScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, TimeSpan.FromMilliseconds(620)) { EasingFunction = _spring });
    }

    // ---------- the second activity's circle ----------

    string? _bubbleTemplate;
    object? _bubbleData;
    bool _bubbleShown;

    /// <summary>Shows (template != null) or hides the circle beside an island this wide.</summary>
    public void SetBubble(string? template, object? data, double pillWidth)
    {
        if (template == null || !_shown)
        {
            if (!_bubbleShown) return;
            _bubbleShown = false;
            _bubbleTemplate = null;
            Bubble.IsHitTestVisible = false;
            Animate(Bubble, OpacityProperty, 0, 160, _smooth);
            Animate(BubbleScale, ScaleTransform.ScaleXProperty, 0.3, 220, _smooth);
            Animate(BubbleScale, ScaleTransform.ScaleYProperty, 0.3, 220, _smooth);
            Animate(BubbleShift, TranslateTransform.XProperty, BubbleX(pillWidth) - Dir * 22, 220, _smooth);
            return;
        }

        if (template != _bubbleTemplate || !ReferenceEquals(data, _bubbleData))
        {
            BubblePresenter.ContentTemplate = (DataTemplate)Resources[template];
            BubblePresenter.Content = data;
            _bubbleTemplate = template;
            _bubbleData = data;
        }
        double x = BubbleX(pillWidth);
        if (!_bubbleShown)
        {
            // Buds off the island's edge and springs out to its spot.
            _bubbleShown = true;
            Bubble.IsHitTestVisible = true;
            BubbleShift.BeginAnimation(TranslateTransform.XProperty, null);
            BubbleShift.X = x - Dir * 26;
            Animate(Bubble, OpacityProperty, 1, 220, _smooth);
            Animate(BubbleScale, ScaleTransform.ScaleXProperty, 1, 640);
            Animate(BubbleScale, ScaleTransform.ScaleYProperty, 1, 640);
        }
        Animate(BubbleShift, TranslateTransform.XProperty, x, 640);
    }

    /// <summary>+1 when the circle sits to the right of the island, -1 when to the left.</summary>
    double Dir => _pos == "right" ? -1 : 1;

    double BubbleX(double pillWidth) => _pos switch
    {
        "left" => pillWidth + BubbleGap,
        "right" => -(pillWidth + BubbleGap),
        _ => pillWidth / 2 + BubbleGap + BubbleSize / 2,
    };

    void Bubble_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Squish(BubblePress, true);
        e.Handled = true;
    }

    void Bubble_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Squish(BubblePress, false);
        e.Handled = true;
        BubbleClicked?.Invoke();
    }

    void Bubble_MouseLeave(object sender, MouseEventArgs e) => Squish(BubblePress, false);

    /// <summary>Press squish: shrinks a touch while held, springs back on release.</summary>
    void Squish(ScaleTransform t, bool down)
    {
        double to = down ? 0.94 : 1;
        Animate(t, ScaleTransform.ScaleXProperty, to, down ? 140 : 520, down ? _smooth : _spring);
        Animate(t, ScaleTransform.ScaleYProperty, to, down ? 140 : 520, down ? _smooth : _spring);
    }

    public void HideIsland()
    {
        if (!_shown) return;
        _shown = false;
        _templateKey = _logicalKind = null;
        Pill.IsHitTestVisible = false;
        SetBubble(null, null, Pill.ActualWidth);
        Animate(Pill, OpacityProperty, 0, 200, _smooth);
        Animate(PillScale, ScaleTransform.ScaleXProperty, 0.5, 260, _smooth);
        Animate(PillScale, ScaleTransform.ScaleYProperty, 0.5, 260, _smooth);
        if (_hover)
        {
            _hover = false;
            HoverChanged?.Invoke(false);
        }
    }

    /// <summary>
    /// The iPhone-style dot. brush = orange for mic, green for camera, null to hide.
    /// pillWidth = width of the island next to it, or null when the island is hidden.
    /// </summary>
    public void SetPrivacyDot(Brush? brush, double? pillWidth)
    {
        if (brush == null)
        {
            Animate(PrivacyDot, OpacityProperty, 0, 200, _smooth);
            return;
        }
        PrivacyDot.Fill = brush;
        Animate(PrivacyDot, OpacityProperty, 1, 250, _smooth);
        PrivacyDot.Margin = new Thickness(Gap, pillWidth == null ? 12 : 20.5, Gap, 0);
        double shift = pillWidth == null ? 0 : _pos switch
        {
            "left" => pillWidth.Value + 8,
            "right" => -(pillWidth.Value + 8),
            _ => pillWidth.Value / 2 + 12,
        };
        Animate(DotShift, TranslateTransform.XProperty, shift);
    }

    // ---------- input ----------

    void OnAction(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag }) ActionClicked?.Invoke(tag);
    }

    void Pill_MouseWheel(object sender, MouseWheelEventArgs e) =>
        Wheel?.Invoke(e.Delta > 0 ? 1 : -1, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));

    void OnSeek(object sender, RoutedEventArgs e)
    {
        if (sender is ProgressLine p) Seek?.Invoke(p.ChosenValue);
    }

    void OnVolume(object sender, RoutedEventArgs e)
    {
        if (sender is ProgressLine p) VolumeSet?.Invoke(p.ChosenValue);
    }

    void Pill_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_hover) return;
        _hover = true;
        HoverChanged?.Invoke(true);
    }

    void Pill_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_hover || _down != null) return;
        _hover = false;
        HoverChanged?.Invoke(false);
    }

    void Pill_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        RightClicked?.Invoke();
        e.Handled = true;
    }

    Point? _down;
    double _downLeft;
    bool _dragging, _flicking;

    Point ScreenDip(MouseEventArgs e)
    {
        var p = PointToScreen(e.GetPosition(this));
        var src = PresentationSource.FromVisual(this);
        return src?.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice.Transform(p) : p;
    }

    void Pill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _down = ScreenDip(e);
        _downLeft = Left;
        _dragging = _flicking = false;
        Pill.CaptureMouse();
        Squish(PressScale, true);
        e.Handled = true;
    }

    void Pill_MouseMove(object sender, MouseEventArgs e)
    {
        if (_down == null || e.LeftButton != MouseButtonState.Pressed || _flicking) return;
        var now = ScreenDip(e);
        double dx = now.X - _down.Value.X, dy = now.Y - _down.Value.Y;
        // Upward and more up than sideways: a flick, like swiping the iPhone island away.
        if (!_dragging && dy < -12 && -dy > Math.Abs(dx))
        {
            Flick();
            return;
        }
        if (!_dragging && Math.Abs(dx) < 6) return;
        _dragging = true;
        Left = _downLeft + dx;
    }

    void Flick()
    {
        _flicking = true;
        _down = null;
        Pill.ReleaseMouseCapture();
        Squish(PressScale, false);
        Pill.IsHitTestVisible = false;
        SetBubble(null, null, Pill.ActualWidth);
        var up = new DoubleAnimation(-44, TimeSpan.FromMilliseconds(Ms(200))) { EasingFunction = _smooth };
        up.Completed += (_, _) =>
        {
            // Reset off-screen, then let the controller show whatever is next (it pops in fresh).
            FlickMove.BeginAnimation(TranslateTransform.YProperty, null);
            FlickMove.Y = 0;
            Pill.BeginAnimation(OpacityProperty, null);
            Pill.Opacity = 0;
            PillScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            PillScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            PillScale.ScaleX = PillScale.ScaleY = 0.5;
            _shown = false;
            _templateKey = _logicalKind = null;
            _flicking = false;
            if (_hover)
            {
                _hover = false;
                HoverChanged?.Invoke(false);
            }
            FlickedUp?.Invoke();
        };
        FlickMove.BeginAnimation(TranslateTransform.YProperty, up);
        Animate(Pill, OpacityProperty, 0, 180, _smooth);
    }

    void Pill_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_down == null) return;
        Pill.ReleaseMouseCapture();
        Squish(PressScale, false);
        bool wasDrag = _dragging;
        _down = null;
        _dragging = false;
        if (!wasDrag)
        {
            Clicked?.Invoke();
            return;
        }

        // Snap to whichever third of the screen the island was dropped in.
        var mid = Pill.TranslatePoint(new Point(Pill.ActualWidth / 2, 0), this);
        double cx = Left + mid.X;
        var wa = SystemParameters.WorkArea;
        string zone = cx < wa.Left + wa.Width / 3 ? "left" : cx > wa.Left + wa.Width * 2 / 3 ? "right" : "center";
        ApplyPosition(zone);
        Dropped?.Invoke(zone);
    }
}
