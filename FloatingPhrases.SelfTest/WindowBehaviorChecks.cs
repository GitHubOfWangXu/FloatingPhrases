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
        type.GetMethod("ApplyAnimation", flags)!.Invoke(controller, [0.5]);
        Check(window.Width > 44 && window.Width < 430 && panel.Opacity > 0 && panel.Opacity < 1,
            "中间帧必须同时具有中间尺寸和过渡透明度");
        type.GetMethod("FinishAnimation", flags)!.Invoke(controller, null);
        Check(panel.Visibility == Visibility.Collapsed && handle.Visibility == Visibility.Visible,
            "收起结束后仅小块可见");
        type.GetMethod("Expand")!.Invoke(controller, [true]);
        type.GetMethod("ApplyAnimation", flags)!.Invoke(controller, [0.5]);
        Check(window.Width > 44 && window.Width < 430, "展开也必须经过中间尺寸");
        type.GetMethod("BeginDrag")!.Invoke(controller, null);
        Check(Math.Abs(window.Width - 430) < 0.01 && Math.Abs(window.Height - 650) < 0.01 && panel.IsHitTestVisible &&
            double.IsNaN(panel.Width) && panel.Opacity == 1,
            "动画被拖动打断时应恢复完整尺寸、自动布局和交互");
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
}
