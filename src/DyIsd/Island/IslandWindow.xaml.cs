using System;
using System.Linq;
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
    /// <summary>Two-finger swipe sideways on the touchpad (or tilt-wheel): switch to the other activity.</summary>
    public event Action<int>? Swiped;
    /// <summary>Files dragged from Explorer and dropped on the island.</summary>
    public event Action<string[]>? FilesDropped;

    // Roomy enough for the Home panel and for the album light to fade out before the window's edge
    // (a smaller window cut the glow off in straight lines, which made it look like a box).
    // Only the island itself takes clicks; the rest of the window lets them through (ClickThrough).
    const double WinWidth = 700, WinHeight = 380, Gap = 14;
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
        SourceInitialized += (_, _) =>
        {
            SetExStyle(Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE, true);
            Place();
            HwndSource.FromHwnd(Handle)?.AddHook(WndProc);
            _clickThrough.Tick += (_, _) => ClickThrough();
            _clickThrough.Start();
            StartDrawCheck();
        };
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(() => ApplyPosition(SettingsStore.Current.Position));

        // Files dropped on the island go to Home's tray.
        Pill.AllowDrop = true;
        Pill.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("FileGroupDescriptorW") ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Pill.Drop += (_, e) =>
        {
            // Real files, or (WhatsApp, Outlook) files handed over as contents with no real path.
            var files = (e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>())
                .Where(f => System.IO.File.Exists(f) || System.IO.Directory.Exists(f)).ToArray();
            if (files.Length == 0) files = VirtualFiles.Save(e.Data, HomeState.TrayDir).ToArray();
            if (files.Length == 0) Log.Write("tray: nothing usable in the drop (" + string.Join(", ", e.Data.GetFormats()) + ")");
            FilesDropped?.Invoke(files); // empty: the island says the app didn't hand the file over
            e.Handled = true;
        };

        // The island never takes the keyboard (so it can't steal focus from your app), except
        // while you type in Home's notes.
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindParent<System.Windows.Controls.TextBox>(d) is { } box) TakeKeyboard(box);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { ActionClicked?.Invoke("home-close"); e.Handled = true; }
        };
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

    // ---------- clicks go through everything but the island ----------

    readonly DispatcherTimer _clickThrough = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
    bool _passThrough;

    /// <summary>
    /// A see-through window still catches clicks on any pixel that isn't fully clear, like the
    /// glow. So the window lets clicks through (WS_EX_TRANSPARENT) except while the mouse is
    /// over the island or its circle.
    /// </summary>
    void ClickThrough()
    {
        bool onPill = Win32.GetCursorPos(out var c) && Over(Pill, c);
        bool over = _down != null || Pill.IsMouseCaptured || onPill || (_bubbleShown && Over(Bubble, c));
        // Hover comes from the cursor, not WPF's MouseLeave: once the window lets clicks through,
        // WPF never hears that the mouse left, and Home / an open view would never close.
        if (_down == null && !_dragOut && !Pill.IsMouseCaptured && onPill != _hover)
        {
            _hover = onPill;
            HoverChanged?.Invoke(onPill);
        }
        if (over == !_passThrough) return;
        _passThrough = !over;
        SetExStyle(Win32.WS_EX_TRANSPARENT, _passThrough);
    }

    bool Over(FrameworkElement e, Win32.POINT c)
    {
        if (!_shown && e == Pill || e.Opacity < 0.05) return false;
        try
        {
            var a = e.PointToScreen(new Point(0, 0));
            var b = e.PointToScreen(new Point(e.ActualWidth, e.ActualHeight));
            return c.X >= Math.Min(a.X, b.X) - 2 && c.X <= Math.Max(a.X, b.X) + 2 && c.Y >= Math.Min(a.Y, b.Y) - 2 && c.Y <= Math.Max(a.Y, b.Y) + 2;
        }
        catch { return false; }
    }

    void EnsureTopmost()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h != IntPtr.Zero)
            Win32.SetWindowPos(h, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    bool _keyboard;

    /// <summary>You're typing in Home's notes (WPF's IsActive can't tell: the island never really activates).</summary>
    public bool Typing => _keyboard;

    void TakeKeyboard(System.Windows.Controls.TextBox box)
    {
        if (!_keyboard)
        {
            _keyboard = true;
            SetExStyle(Win32.WS_EX_NOACTIVATE, false);
        }
        Activate();
        box.Focus();
    }

    /// <summary>Gives the keyboard back once Home closes.</summary>
    void ReleaseKeyboard()
    {
        if (!_keyboard) return;
        _keyboard = false;
        Keyboard.ClearFocus();
        SetExStyle(Win32.WS_EX_NOACTIVATE, true);
    }

    static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        for (var x = d; x != null; x = x is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
            if (x is T t) return t;
        return null;
    }

    // ---------- the tray: open a file, or drag it out anywhere ----------

    Point? _trayDown;
    bool _dragOut; // dragging a file out of the tray: Home stays open

    void TrayItem_Down(object sender, MouseButtonEventArgs e)
    {
        _trayDown = e.GetPosition(this);
        e.Handled = true; // not a click on the island itself
    }

    void TrayItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (_trayDown == null || e.LeftButton != MouseButtonState.Pressed) return;
        if ((e.GetPosition(this) - _trayDown.Value).Length < 6) return;
        _trayDown = null;
        if (sender is not FrameworkElement { DataContext: TrayItem t } el) return;
        if (!t.Exists) { Log.Write("tray: file is gone: " + t.Path); return; }
        _dragOut = true;
        try { var r = DragDrop.DoDragDrop(el, new DataObject(DataFormats.FileDrop, new[] { t.Path }), DragDropEffects.Copy); }
        finally { _dragOut = false; }
    }

    void TrayItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (_trayDown == null) return; // dragged out, or the × was pressed
        _trayDown = null;
        if (sender is FrameworkElement { DataContext: TrayItem t }) ActionClicked?.Invoke("tray-open:" + t.Path);
        e.Handled = true;
    }

    // ---------- position ----------

    IntPtr _monitor; // the screen the island is on (zero = the main one)

    /// <summary>Moves the island to the monitor you're working on.</summary>
    public void FollowMonitor(IntPtr monitor)
    {
        if (monitor == _monitor || !Monitors.IsValid(monitor)) return;
        _monitor = monitor;
        Place();
    }

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Puts the window at the top of its monitor, in real pixels (WPF only knows the main screen).</summary>
    void Place()
    {
        var h = Handle;
        if (h == IntPtr.Zero)
        {
            var wa = SystemParameters.WorkArea;
            Top = wa.Top;
            Left = _pos switch { "left" => wa.Left, "right" => wa.Right - WinWidth, _ => wa.Left + (wa.Width - WinWidth) / 2 };
            return;
        }
        var mon = Monitors.IsValid(_monitor) ? _monitor : Monitors.Primary;
        var w = Monitors.Work(mon);
        int width = (int)Math.Round(WinWidth * Monitors.Scale(mon));
        int x = _pos switch { "left" => w.Left, "right" => w.Right - width, _ => w.Left + (w.Width - width) / 2 };
        Monitors.Place(this, h, x, w.Top);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(Place); // the new size is known only after the change
    }

    public void ApplyPosition(string pos)
    {
        _pos = pos;
        Place();
        var align = pos switch { "left" => HorizontalAlignment.Left, "right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        Pill.HorizontalAlignment = align;
        Aura.HorizontalAlignment = align;
        Bubble.HorizontalAlignment = align;
        Presenter.HorizontalAlignment = align;
        PrivacyDot.HorizontalAlignment = align;
        Pill.RenderTransformOrigin = new Point(pos == "left" ? 0 : pos == "right" ? 1 : 0.5, 0);
        // Compact views mirror on the right so the album art hugs the screen edge.
        Resources["CompactFlow"] = pos == "right" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }


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

    // ---------- album-colour glow ----------

    readonly SolidColorBrush _auraBrush = new(Colors.Transparent);
    MediaState? _glowSource;
    bool _auraOn;

    /// <summary>Light in the album's colour spilling out from under the island while music plays.</summary>
    void UpdateGlow()
    {
        var m = Presenter.Content as MediaState;
        if (m != _glowSource)
        {
            if (_glowSource != null) _glowSource.PropertyChanged -= OnGlowSource;
            _glowSource = m;
            if (m != null) m.PropertyChanged += OnGlowSource;
        }
        bool on = _shown && m is { IsPlaying: true, ArtColor: not null } && SettingsStore.Current.AlbumGlow;
        if (on)
        {
            AuraCore.Background ??= _auraBrush;
            AuraBloom.Background ??= _auraBrush;
            var to = m!.ArtColor!.Value;
            if (SystemParameters.ClientAreaAnimation)
                _auraBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(700)));
            else _auraBrush.Color = to;
        }
        if (on == _auraOn) return;
        _auraOn = on;
        if (on && SystemParameters.ClientAreaAnimation)
        {
            // Fade up, then breathe slowly (20 fps is plenty for something this soft).
            var breathe = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(500) };
            breathe.KeyFrames.Add(new EasingDoubleKeyFrame(0.22, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.2)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            breathe.KeyFrames.Add(new EasingDoubleKeyFrame(0.32, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.4)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            Timeline.SetDesiredFrameRate(breathe, 20);
            var fadeIn = new DoubleAnimation(0.32, TimeSpan.FromMilliseconds(500)) { EasingFunction = _smooth };
            fadeIn.Completed += (_, _) => { if (_auraOn) Aura.BeginAnimation(OpacityProperty, breathe); };
            Aura.BeginAnimation(OpacityProperty, fadeIn);
        }
        else Animate(Aura, OpacityProperty, on ? 0.32 : 0, 400, _smooth);
    }

    void OnGlowSource(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaState.IsPlaying) or nameof(MediaState.ArtColor)) Dispatcher.BeginInvoke(UpdateGlow);
    }

    public void ShowView(string templateKey, string logicalKind, object data, double width, double height)
    {
        if (templateKey != _templateKey || logicalKind != _logicalKind)
        {
            bool morph = _shown && _templateKey != null;
            if (_templateKey == "home") ReleaseKeyboard();
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
        UpdateGlow();

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
            UpdateGlow(); // now that it's shown
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
        if (_templateKey == "home") ReleaseKeyboard();
        _templateKey = _logicalKind = null;
        Pill.IsHitTestVisible = false;
        UpdateGlow();
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
        if (sender is not FrameworkElement { Tag: string tag } el) return;
        // List rows say what to do; which item comes from the row itself.
        if (tag == "tray-remove" && el.DataContext is TrayItem t) tag += ":" + t.Path;
        else if (tag == "clip" && el.DataContext is ClipItem c) tag += ":" + c.Text;
        else if (tag is "rem-tick" or "rem-del" && el.DataContext is Services.Reminder r) tag += ":" + r.Id;
        ActionClicked?.Invoke(tag);
        if (e != null) e.Handled = true;
    }

    /// <summary>Enter in Home's reminder box adds it.</summary>
    void RemBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not System.Windows.Controls.TextBox box) return;
        var text = box.Text.Trim();
        box.Clear();
        if (text.Length > 0) ActionClicked?.Invoke("rem-add:" + text);
        e.Handled = true;
    }

    // ---------- sideways swipe ----------

    const int WM_MOUSEHWHEEL = 0x020E;
    int _hAccum;
    DateTime _lastSwipe;
    readonly TranslateTransform _slide = new();

    /// <summary>WPF has no event for sideways scrolling, so it's read from the raw window message.</summary>
    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_MOUSEHWHEEL || !Pill.IsMouseOver) return IntPtr.Zero;
        _hAccum += (short)((long)wParam >> 16);
        handled = true;
        // One swipe = one switch, however many messages the touchpad sends.
        if (Math.Abs(_hAccum) >= 120 && DateTime.Now - _lastSwipe > TimeSpan.FromMilliseconds(450))
        {
            int dir = _hAccum > 0 ? 1 : -1;
            _hAccum = 0;
            _lastSwipe = DateTime.Now;
            Swiped?.Invoke(dir);
        }
        else if (DateTime.Now - _lastSwipe <= TimeSpan.FromMilliseconds(450)) _hAccum = 0;
        return IntPtr.Zero;
    }

    /// <summary>The new activity slides in from the side you swiped toward.</summary>
    public void SlideIn(int dir)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        Presenter.RenderTransform = _slide;
        _slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dir * 36, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = _spring });
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
            UpdateGlow();
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

        // Snap to whichever third of the screen the island was dropped in (on the screen it was dropped on).
        string zone = Monitors.ZoneAtCursor();
        _monitor = Monitors.AtCursor();
        ApplyPosition(zone);
        Dropped?.Invoke(zone);
    }
}
