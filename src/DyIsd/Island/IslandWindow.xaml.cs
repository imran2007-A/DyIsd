using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DyIsd.Native;
using DyIsd.Settings;
using Microsoft.Win32;

namespace DyIsd.Island;

/// <summary>
/// The see-through, always-on-top window at the top of the screen. It only draws and animates;
/// IslandController decides what to show.
/// </summary>
public partial class IslandWindow : Window
{
    public event Action<bool>? HoverChanged;
    public event Action? IslandClicked;
    public event Action<string>? ActionClicked;
    public event Action<int, bool>? Wheel;
    public event Action<string>? Dropped;
    public event Action? BubbleClicked;

    const double WinWidth = 480, WinHeight = 250;
    readonly IEasingFunction _spring = new BackEase { Amplitude = 0.28, EasingMode = EasingMode.EaseOut };
    readonly IEasingFunction _smooth = new CubicEase { EasingMode = EasingMode.EaseOut };
    string? _templateKey, _logicalKind;
    bool _shown, _bubbleShown;

    public IslandWindow()
    {
        InitializeComponent();
        Width = WinWidth;
        Height = WinHeight;
        SourceInitialized += (_, _) => MakeToolWindow();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(() => ApplyPosition(SettingsStore.Current.Position));
        ApplyPosition(SettingsStore.Current.Position);
    }

    /// <summary>No taskbar button, no Alt+Tab entry, and clicking it doesn't steal focus from your app.</summary>
    void MakeToolWindow()
    {
        var h = new WindowInteropHelper(this).Handle;
        long ex = (long)Win32.GetWindowLongPtr(h, Win32.GWL_EXSTYLE);
        ex |= Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE;
        ex &= ~Win32.WS_EX_APPWINDOW;
        Win32.SetWindowLongPtr(h, Win32.GWL_EXSTYLE, (IntPtr)ex);
    }

    void EnsureTopmost()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero)
            Win32.SetWindowPos(h, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    /// <summary>Moves the window to the top-left, top-center or top-right of the main screen.</summary>
    public void ApplyPosition(string pos)
    {
        var wa = SystemParameters.WorkArea;
        Top = wa.Top;
        Left = pos switch
        {
            "left" => wa.Left,
            "right" => wa.Right - WinWidth,
            _ => wa.Left + (wa.Width - WinWidth) / 2,
        };

        var align = pos switch { "left" => HorizontalAlignment.Left, "right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        Row.HorizontalAlignment = align;
        Presenter.HorizontalAlignment = align;
        // On the right side, the island grows leftward and the second bubble sits to its left.
        Row.FlowDirection = pos == "right" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Pill.RenderTransformOrigin = new Point(pos == "left" ? 0 : pos == "right" ? 1 : 0.5, 0);
        // Compact views mirror on the right so the album art hugs the screen edge.
        Resources["CompactFlow"] = pos == "right" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    int Ms(int ms) => SystemParameters.ClientAreaAnimation ? ms : 0; // respects "Animation effects: off"

    void Animate(IAnimatable target, DependencyProperty dp, double to, int ms = 480, IEasingFunction? ease = null) =>
        target.BeginAnimation(dp, new DoubleAnimation(to, TimeSpan.FromMilliseconds(Ms(ms))) { EasingFunction = ease ?? _spring });

    /// <summary>Shows a view. Same logical kind updates in place; a new kind fades in.</summary>
    public void ShowView(string templateKey, string logicalKind, object data, double width, double height)
    {
        if (templateKey != _templateKey || logicalKind != _logicalKind)
        {
            Presenter.ContentTemplate = (DataTemplate)Resources[templateKey];
            Presenter.Content = data;
            Presenter.Opacity = 0;
            Animate(Presenter, OpacityProperty, 1, 260, _smooth);
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
            Pill.IsHitTestVisible = true;
            EnsureTopmost();
            Animate(Pill, OpacityProperty, 1, 220, _smooth);
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
        Animate(Pill, OpacityProperty, 0, 220, _smooth);
        Animate(PillScale, ScaleTransform.ScaleXProperty, 0.6, 260, _smooth);
        Animate(PillScale, ScaleTransform.ScaleYProperty, 0.6, 260, _smooth);
        SetBubble(null, null);
    }

    /// <summary>The small circle next to the island when two things are running.</summary>
    public void SetBubble(string? kind, object? data)
    {
        if (kind == null)
        {
            if (!_bubbleShown) return;
            _bubbleShown = false;
            Bubble.IsHitTestVisible = false;
            Animate(Bubble, OpacityProperty, 0, 180, _smooth);
            return;
        }

        BubblePresenter.ContentTemplate = (DataTemplate)Resources["bubble-" + kind];
        BubblePresenter.Content = data;
        if (_bubbleShown) return;
        _bubbleShown = true;
        Bubble.IsHitTestVisible = true;
        Animate(Bubble, OpacityProperty, 1, 260, _smooth);
    }

    // ---------- input ----------

    void OnAction(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag }) ActionClicked?.Invoke(tag);
    }

    void Pill_MouseEnter(object sender, MouseEventArgs e) => HoverChanged?.Invoke(true);
    void Pill_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_dragging) HoverChanged?.Invoke(false);
    }

    void Pill_MouseWheel(object sender, MouseWheelEventArgs e) =>
        Wheel?.Invoke(e.Delta > 0 ? 1 : -1, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));

    void Bubble_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => BubbleClicked?.Invoke();

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
            IslandClicked?.Invoke();
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
