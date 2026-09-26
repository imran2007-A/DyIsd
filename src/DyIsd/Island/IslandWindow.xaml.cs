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

    const double WinWidth = 420, WinHeight = 250, Gap = 14;

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
            Presenter.ContentTemplate = (DataTemplate)Resources[templateKey];
            Presenter.Content = data;
            BlurIn();
            _templateKey = templateKey;
            _logicalKind = logicalKind;
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

    public void HideIsland()
    {
        if (!_shown) return;
        _shown = false;
        _templateKey = _logicalKind = null;
        Pill.IsHitTestVisible = false;
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
    bool _dragging;

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
        _dragging = false;
        Pill.CaptureMouse();
        e.Handled = true;
    }

    void Pill_MouseMove(object sender, MouseEventArgs e)
    {
        if (_down == null || e.LeftButton != MouseButtonState.Pressed) return;
        double dx = ScreenDip(e).X - _down.Value.X;
        if (!_dragging && Math.Abs(dx) < 6) return;
        _dragging = true;
        Left = _downLeft + dx;
    }

    void Pill_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_down == null) return;
        Pill.ReleaseMouseCapture();
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
