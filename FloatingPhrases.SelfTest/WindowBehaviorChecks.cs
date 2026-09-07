using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FloatingPhrases;

internal static class WindowBehaviorChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                // Create an HWND without showing it, touching user data or injecting input.
                window = new Window
                {
                    Width = 430, Height = 650, WindowStyle = WindowStyle.None,
                    AllowsTransparency = true, ShowInTaskbar = false, ShowActivated = false
                };
                var behavior = new WindowBehavior(window);
                window.WindowState = WindowState.Maximized;
                Check(window.WindowState == WindowState.Normal, "创建句柄前也应阻止最大化状态");
                var hwnd = new WindowInteropHelper(window).EnsureHandle();
                behavior.Attach(hwnd);
                const int resizeFrame = 0x00040000;
                const int maximizeBox = 0x00010000;
                const int minimizeBox = 0x00020000;
                var style = GetWindowLong(hwnd, -16);
                Check((style & (resizeFrame | maximizeBox)) == 0,
                    "工具窗不能保留触发系统贴边最大化的窗口样式");
                Check((style & minimizeBox) != 0, "应保留最小化能力");

                foreach (var mode in Enum.GetValues<WindowMode>())
                {
                    behavior.Apply(mode, 0.35, false);
                    Check((GetWindowLong(hwnd, -16) & (resizeFrame | maximizeBox)) == 0,
                        "切换窗口模式不能重新开启系统最大化");
                }
                window.WindowState = WindowState.Maximized;
                Check(window.WindowState == WindowState.Normal, "异常最大化请求应恢复到普通窗口");
                Check(window.Width == 430 && window.Height == 650, "恢复后必须保留面板尺寸");
                CheckDockTransition(window);
            }
            catch (Exception ex) { failure = ex; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("窗口行为回归检查失败", failure);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void CheckDockTransition(Window window)
    {
        var panel = new System.Windows.Controls.Border();
        var handle = new System.Windows.Controls.Border();
        var root = new System.Windows.Controls.Grid();
        root.Children.Add(panel);
        root.Children.Add(handle);
        window.Content = root;
        var type = typeof(EdgeDockLayout).Assembly.GetType("FloatingPhrases.EdgeDockController")!;
        using var controller = (IDisposable)Activator.CreateInstance(type, window, panel, handle, (Action)(() => { }))!;
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        type.GetField("_workArea", flags)!.SetValue(controller, new DockBounds(0, 0, 1920, 1080));
        type.GetField("_edge", flags)!.SetValue(controller, DockEdge.Right);
        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX;
        type.GetMethod("Collapse", flags)!.Invoke(controller, [new DockBounds(1920 - 430 * scale, 100, 430 * scale, 650 * scale)]);
        Check(Math.Abs(window.Width - 430) < 0.01 && panel.Visibility == Visibility.Visible,
            "收起开始不能立即跳到小块，必须保留面板并逐步过渡");
        CheckStableFrames();
        type.GetMethod("ApplyAnimation", flags)!.Invoke(controller, [0.5]);
        Check(Math.Abs(window.Width - 430) < 0.01 && panel.Opacity > 0 && panel.Opacity < 1,
            "收起中间帧必须保持原生窗口尺寸稳定，仅过渡内容，避免窗口抖动");
        type.GetMethod("FinishAnimation", flags)!.Invoke(controller, null);
        Check(Math.Abs(window.Width - 430) < 0.01 && Math.Abs(window.Height - 650) < 0.01,
            "收起结束也不能切换原生窗口尺寸，否则最终帧仍会闪烁");
        Check(panel.Visibility == Visibility.Collapsed && handle.Visibility == Visibility.Visible,
            "收起结束后仅小块可见");
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            Check(GetWindowRgn(new WindowInteropHelper(window).Handle, region) == 2,
                "收起后必须有原生矩形裁剪，不能让透明面板挡住桌面点击");
            Check(PtInRegion(region, (int)(handle.Margin.Left * scale + 3), (int)(handle.Margin.Top * scale + 3)) &&
                !PtInRegion(region, 10, 10), "仅边签区域应保留原生窗口命中范围");
        }
        finally { DeleteObject(region); }
        type.GetMethod("Expand")!.Invoke(controller, [true]);
        region = CreateRectRgn(0, 0, 0, 0);
        try { Check(GetWindowRgn(new WindowInteropHelper(window).Handle, region) == 0, "展开后必须解除原生裁剪"); }
        finally { DeleteObject(region); }
        CheckStableFrames();
        type.GetMethod("ApplyAnimation", flags)!.Invoke(controller, [0.5]);
        Check(Math.Abs(window.Width - 430) < 0.01, "展开中间帧必须保持完整窗口尺寸稳定");
        type.GetMethod("BeginDrag")!.Invoke(controller, null);
        Check(Math.Abs(window.Width - 430) < 0.01 && Math.Abs(window.Height - 650) < 0.01 && panel.IsHitTestVisible &&
            double.IsNaN(panel.Width) && panel.Opacity == 1,
            "动画被拖动打断时应恢复完整尺寸、自动布局和交互");

        void CheckStableFrames()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            Check(GetWindowRect(hwnd, out var initial), "应能读取动画窗口矩形");
            foreach (var progress in new[] { 0.1, 0.3, 0.6, 0.9 })
            {
                type.GetMethod("ApplyAnimation", flags)!.Invoke(controller, [progress]);
                Check(GetWindowRect(hwnd, out var current) && current.Equals(initial),
                    "动画中间帧不得改写原生窗口位置或尺寸");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr region);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
}
