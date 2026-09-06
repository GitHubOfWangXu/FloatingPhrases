using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace FloatingPhrases;

// Shrinks the actual HWND: the hidden panel must not intercept desktop input.
internal sealed class EdgeDockController : IDisposable
{
    private readonly Window _window;
    private readonly FrameworkElement _panel;
    private readonly FrameworkElement _handleView;
    private readonly Action _refreshBehavior;
    private readonly DispatcherTimer _timer;
    private readonly double _minWidth;
    private readonly double _minHeight;
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
        _minWidth = window.MinWidth;
        _minHeight = window.MinHeight;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += Tick;
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
    }

    public void Expand()
    {
        if (!IsCollapsed) return;
        // Resolve the monitor while the small handle still lies entirely on it.
        // Restoring minimum size first can temporarily extend into a neighboring monitor.
        _workArea = WorkArea();
        IsCollapsed = false;
        _handleView.Visibility = Visibility.Collapsed;
        _window.MinWidth = _minWidth;
        _window.MinHeight = _minHeight;
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
        _panel.Visibility = Visibility.Collapsed;
        _window.MinWidth = 0;
        _window.MinHeight = 0;
        _handleView.Visibility = Visibility.Visible;
        Move(EdgeDockLayout.Handle(_expanded, _workArea, _edge, Scale));
        ResetDelays();
        _refreshBehavior();
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_dragging || !_window.IsVisible || _window.WindowState != WindowState.Normal ||
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

        var pointer = Forms.Cursor.Position;
        var inside = bounds.Contains(pointer.X, pointer.Y);
        var now = Environment.TickCount64;
        if (IsCollapsed)
        {
            if (!inside) _hoverSince = 0;
            else if (_hoverSince == 0) _hoverSince = now;
            else if (now - _hoverSince >= 250) Expand();
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
        Expand();
        _window.Activate();
    }

    private void KeyDown(object sender, System.Windows.Input.KeyEventArgs e) => _lastInput = Environment.TickCount64;
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
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
        _timer.Stop();
        _timer.Tick -= Tick;
        _window.IsVisibleChanged -= VisibilityChanged;
        _window.PreviewKeyDown -= KeyDown;
        _handleView.MouseLeftButtonDown -= HandleClick;
    }

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
