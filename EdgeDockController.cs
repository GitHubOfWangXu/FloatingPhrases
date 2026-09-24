using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace FloatingPhrases;

// Keeps the layered HWND stable. A native region limits both drawing and input
// to the handle when collapsed, without reallocating WPF's window surface.
internal sealed class EdgeDockController : IDisposable
{
    private readonly Window _window;
    private readonly FrameworkElement _panel;
    private readonly FrameworkElement _handleView;
    private readonly Action _refreshBehavior;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _animationTimer;
    private readonly TranslateTransform _panelOffset = new();
    private long _animationStarted;
    public bool IsAnimating => _animationTimer.IsEnabled;
    public bool IsDocked => _edge != DockEdge.None;
    private DockBounds _expanded;
    private DockBounds _workArea;
    private DockEdge _edge;
    private bool _enabled;
    private bool _dragging;
    private long _outsideSince;
    private long _hoverSince;
    private long _lastInput;
    public bool IsCollapsed { get; private set; }

    public EdgeDockController(Window window, FrameworkElement panel, FrameworkElement handleView, Action refreshBehavior)
    {
        _window = window;
        _panel = panel;
        _handleView = handleView;
        _refreshBehavior = refreshBehavior;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += Tick;
        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _animationTimer.Tick += AnimationTick;
        window.IsVisibleChanged += VisibilityChanged;
        window.PreviewKeyDown += KeyDown;
        handleView.MouseLeftButtonDown += HandleClick;
    }

    private IntPtr Hwnd => new WindowInteropHelper(_window).Handle;
    private double Scale => VisualTreeHelper.GetDpi(_window).DpiScaleX;

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            Expand();
            _edge = DockEdge.None;
        }
        UpdateTimer();
    }

    public void BeginDrag()
    {
        Expand();
        _dragging = true;
        _edge = DockEdge.None;
        ResetDelays();
    }

    public void EndDrag()
    {
        _dragging = false;
        if (!_enabled || _window.WindowState != WindowState.Normal || !TryBounds(out var bounds)) return;
        _workArea = WorkArea();
        _edge = EdgeDockLayout.Detect(bounds, _workArea, 16 * Scale);
        if (_edge != DockEdge.None)
        {
            _expanded = EdgeDockLayout.Snap(bounds, _workArea, _edge);
            Move(_expanded);
        }
        ResetDelays();
        UpdateTimer();
        _refreshBehavior();
    }

    public void Expand(bool animate = false)
    {
        if (IsAnimating) FinishAnimation();
        if (!IsCollapsed) return;
        _workArea = WorkArea();
        IsCollapsed = false;
        _expanded = EdgeDockLayout.Snap(_expanded, _workArea, _edge);
        if (animate)
        {
            StartAnimation();
            return;
        }
        SetWindowRgn(Hwnd, IntPtr.Zero, true);
        _handleView.Visibility = Visibility.Collapsed;
        _expanded = EdgeDockLayout.Snap(_expanded, _workArea, _edge);
        Move(_expanded);
        _panel.Visibility = Visibility.Visible;
        ResetDelays();
        _lastInput = Environment.TickCount64;
        _refreshBehavior();
    }

    private void Collapse(DockBounds bounds)
    {
        _expanded = EdgeDockLayout.Snap(bounds, _workArea, _edge);
        IsCollapsed = true;
        StartAnimation();
    }

    private void StartAnimation()
    {
        _animationStarted = Environment.TickCount64;
        _panel.RenderTransform = _panelOffset;
        _panel.Visibility = Visibility.Visible;
        _handleView.Visibility = Visibility.Visible;
        var handle = EdgeDockLayout.Handle(_expanded, _workArea, _edge, Scale);
        _handleView.Width = handle.Width / Scale;
        _handleView.Height = handle.Height / Scale;
        _handleView.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        _handleView.VerticalAlignment = VerticalAlignment.Top;
        _handleView.Margin = new Thickness((handle.X - _expanded.X) / Scale,
            (handle.Y - _expanded.Y) / Scale, 0, 0);
        _panel.IsHitTestVisible = false;
        _handleView.IsHitTestVisible = false;
        _animationTimer.Start();
        ResetDelays();
        _refreshBehavior();
        ApplyAnimation(0);
        // Reveal the full region only after the expansion's initial transparent
        // state is prepared; keep the same HWND bounds even at the endpoints.
        if (!IsCollapsed) SetWindowRgn(Hwnd, IntPtr.Zero, true);
    }

    private void AnimationTick(object? sender, EventArgs e)
    {
        var progress = Math.Clamp((Environment.TickCount64 - _animationStarted) / 220.0, 0, 1);
        ApplyAnimation(progress);
        if (progress >= 1) FinishAnimation();
    }

    private void ApplyAnimation(double progress)
    {
        var eased = progress * progress * (3 - 2 * progress);
        _panel.Opacity = IsCollapsed ? 1 - eased : eased;
        var offset = 12 * (1 - _panel.Opacity);
        _panelOffset.X = _edge == DockEdge.Left ? -offset : _edge == DockEdge.Right ? offset : 0;
        _panelOffset.Y = _edge == DockEdge.Top ? -offset : 0;
        _handleView.Opacity = 1 - _panel.Opacity;
    }

    private void FinishAnimation()
    {
        _animationTimer.Stop();
        _panel.Visibility = IsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        _handleView.Visibility = IsCollapsed ? Visibility.Visible : Visibility.Collapsed;
        _panel.RenderTransform = Transform.Identity;
        _panel.Opacity = _handleView.Opacity = 1;
        _panel.IsHitTestVisible = _handleView.IsHitTestVisible = true;
        if (IsCollapsed && !ClipToHandle())
        {
            // If native clipping fails, retain an operable full panel.
            IsCollapsed = false;
            _edge = DockEdge.None;
            _panel.Visibility = Visibility.Visible;
            _handleView.Visibility = Visibility.Collapsed;
        }
        ResetDelays();
        _lastInput = Environment.TickCount64;
        _refreshBehavior();
    }

    private bool ClipToHandle()
    {
        var handle = EdgeDockLayout.Handle(_expanded, _workArea, _edge, Scale);
        var region = CreateRectRgn((int)Math.Round(handle.X - _expanded.X),
            (int)Math.Round(handle.Y - _expanded.Y), (int)Math.Round(handle.Right - _expanded.X),
            (int)Math.Round(handle.Bottom - _expanded.Y));
        if (region == IntPtr.Zero) return false;
        if (SetWindowRgn(Hwnd, region, true) != 0) return true; // Windows owns region on success.
        DeleteObject(region);
        return false;
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (IsAnimating || _dragging || !_window.IsVisible || _window.WindowState != WindowState.Normal ||
            _edge == DockEdge.None || !TryBounds(out var bounds)) return;
        // Native disabled state covers both WPF dialogs and native MessageBox loops.
        if (!IsWindowEnabled(Hwnd) || Mouse.Captured is not null ||
            Forms.Control.MouseButtons != Forms.MouseButtons.None ||
            _window.OwnedWindows.Cast<Window>().Any(child => child.IsVisible))
        {
            ResetDelays();
            return;
        }

        var currentArea = WorkArea();
        if (currentArea != _workArea)
        {
            _workArea = currentArea;
            if (IsCollapsed) Expand();
            else Move(EdgeDockLayout.Snap(bounds, currentArea, _edge));
            ResetDelays();
            return;
        }

        if (IsCollapsed) bounds = EdgeDockLayout.Handle(_expanded, _workArea, _edge, Scale);
        var pointer = Forms.Cursor.Position;
        var inside = bounds.Contains(pointer.X, pointer.Y);
        var now = Environment.TickCount64;
        if (IsCollapsed)
        {
            if (!inside) _hoverSince = 0;
            else if (_hoverSince == 0) _hoverSince = now;
            else if (now - _hoverSince >= 250) Expand(animate: true);
        }
        else
        {
            if (inside || now - _lastInput < 650) _outsideSince = 0;
            else if (_outsideSince == 0) _outsideSince = now;
            else if (now - _outsideSince >= 650) Collapse(bounds);
        }
    }

    private void HandleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!IsCollapsed || !TryBounds(out var start)) return;
        var moved = false;
        void TrackMovement(object? source, EventArgs args)
        {
            if (TryBounds(out var current))
                moved |= Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance * Scale ||
                    Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance * Scale;
        }

        // Keep the native handle region and collapsed content throughout the move.
        // DragMove owns mouse capture until release; Tick must not expand on hover.
        _dragging = true;
        ResetDelays();
        _window.LocationChanged += TrackMovement;
        try { _window.DragMove(); }
        finally
        {
            _window.LocationChanged -= TrackMovement;
            _dragging = false;
            CompleteHandleDrag(moved);
        }
        _window.Activate();
    }

    private void CompleteHandleDrag(bool moved)
    {
        if (!TryBounds(out var bounds)) return;
        if (!moved)
        {
            Expand(animate: true);
            return;
        }

        // The invisible full panel may overlap another monitor. Use the visible
        // handle to choose both the destination monitor and the docking edge.
        var handle = new DockBounds(bounds.X + _handleView.Margin.Left * Scale,
            bounds.Y + _handleView.Margin.Top * Scale, _handleView.Width * Scale, _handleView.Height * Scale);
        var area = Forms.Screen.FromPoint(new System.Drawing.Point(
            (int)Math.Round(handle.X + handle.Width / 2), (int)Math.Round(handle.Y + handle.Height / 2))).WorkingArea;
        _workArea = new DockBounds(area.X, area.Y, area.Width, area.Height);
        _edge = _enabled ? EdgeDockLayout.Detect(handle, _workArea, 16 * Scale) : DockEdge.None;
        _expanded = EdgeDockLayout.PanelFromHandle(handle, bounds, _workArea, _edge);
        Move(_expanded);
        if (_edge == DockEdge.None) Expand();
        else
        {
            // Reposition the small view if the drag changed edges or monitor DPI.
            var target = EdgeDockLayout.Handle(_expanded, _workArea, _edge, Scale);
            _handleView.Width = target.Width / Scale;
            _handleView.Height = target.Height / Scale;
            _handleView.Margin = new Thickness((target.X - _expanded.X) / Scale,
                (target.Y - _expanded.Y) / Scale, 0, 0);
            FinishAnimation();
        }
        ResetDelays();
        UpdateTimer();
        _refreshBehavior();
    }

    private void KeyDown(object sender, System.Windows.Input.KeyEventArgs e) => _lastInput = Environment.TickCount64;
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_window.IsVisible && IsAnimating) FinishAnimation();
        ResetDelays();
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        if (_enabled && _window.IsVisible && _edge != DockEdge.None) _timer.Start();
        else _timer.Stop();
    }

    private void ResetDelays() { _outsideSince = 0; _hoverSince = 0; }

    private DockBounds WorkArea()
    {
        var area = Forms.Screen.FromHandle(Hwnd).WorkingArea;
        return new DockBounds(area.X, area.Y, area.Width, area.Height);
    }

    private bool TryBounds(out DockBounds bounds)
    {
        var success = GetWindowRect(Hwnd, out var rect);
        bounds = new DockBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return success;
    }

    private void Move(DockBounds bounds)
    {
        // WPF dimensions are DIPs; monitor origins and native positioning stay in pixels.
        _window.Width = bounds.Width / Scale;
        _window.Height = bounds.Height / Scale;
        SetWindowPos(Hwnd, IntPtr.Zero, (int)Math.Round(bounds.X), (int)Math.Round(bounds.Y),
            (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), 0x0014); // NOZORDER | NOACTIVATE
    }

    public void Dispose()
    {
        _animationTimer.Stop();
        _animationTimer.Tick -= AnimationTick;
        _timer.Stop();
        _timer.Tick -= Tick;
        _window.IsVisibleChanged -= VisibilityChanged;
        _window.PreviewKeyDown -= KeyDown;
        _handleView.MouseLeftButtonDown -= HandleClick;
        if (Hwnd != IntPtr.Zero) SetWindowRgn(Hwnd, IntPtr.Zero, false);
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
