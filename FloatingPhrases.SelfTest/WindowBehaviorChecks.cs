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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
}
