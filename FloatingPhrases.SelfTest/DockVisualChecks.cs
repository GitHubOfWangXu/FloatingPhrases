using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FloatingPhrases;

// Explicit opt-in desktop check. Only generic fixture content is displayed/captured.
internal static class DockVisualChecks
{
    public static void RunApp(string? selectedTab = null)
    {
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application();
            var window = new MainWindow { ShowInTaskbar = true };
            if (selectedTab is not null) ((TabItem)window.FindName(selectedTab)).IsSelected = true;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                typeof(MainWindow).GetMethod("ExitApplication", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, null);
            };
            timer.Start();
            app.Run(window);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    public static void Run(string outputDirectory, DockEdge edge)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RunWindow(Path.GetFullPath(outputDirectory), edge); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("可视化验收失败", failure);
    }

    private static void RunWindow(string outputDirectory, DockEdge edge)
    {
        Directory.CreateDirectory(outputDirectory);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var backdrop = new Window
        {
            Title = "Floating Phrases 动画验收", Width = 520, Height = 740,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(32, 42, 56)), Topmost = true,
            ShowInTaskbar = true
        };
        backdrop.Show();
        var scale = VisualTreeHelper.GetDpi(backdrop).DpiScaleX;
        backdrop.Left = edge == DockEdge.Right ? area.Right / scale - backdrop.Width : area.Left / scale;
        backdrop.Top = area.Top / scale;
        var root = new Grid();
        var handle = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(68, 98, 232)), CornerRadius = new CornerRadius(5),
            Visibility = Visibility.Collapsed,
            Child = new TextBlock { Text = "内存\n45%", Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center }
        };
        var content = new StackPanel { Margin = new Thickness(18) };
        content.Children.Add(new TextBlock { Text = "Floating Phrases", FontSize = 22, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "动画验收 · 通用示例数据", Margin = new Thickness(0, 12, 0, 20) });
        for (var i = 1; i <= 5; i++)
            content.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12),
                Child = new TextBlock { Text = $"示例短语 {i}\n检查文字、阴影及边签是否连续", FontSize = 15 } });
        var panel = new Border { Background = new SolidColorBrush(Color.FromRgb(248, 249, 253)),
            CornerRadius = new CornerRadius(18), Child = content,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Opacity = 0.22 } };
        root.Children.Add(handle);
        root.Children.Add(panel);
        var window = new Window { Title = "Floating Phrases 过渡样例", Width = 430, Height = 650,
            MinWidth = 370, MinHeight = 450, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanMinimize, AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = true, Topmost = true, Content = root, Owner = backdrop };
        window.Show();
        window.Left = edge == DockEdge.Right ? area.Right / scale - window.Width : area.Left / scale;
        window.Top = area.Top / scale + (edge == DockEdge.Top ? 0 : 40);
        var type = typeof(EdgeDockLayout).Assembly.GetType("FloatingPhrases.EdgeDockController")!;
        using var controller = (IDisposable)Activator.CreateInstance(type, window, panel, handle, (Action)(() => { }))!;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var hwnd = new WindowInteropHelper(window).Handle;
        GetWindowRect(hwnd, out var initial);
        type.GetField("_workArea", flags)!.SetValue(controller, new DockBounds(area.X, area.Y, area.Width, area.Height));
        type.GetField("_edge", flags)!.SetValue(controller, edge);
        var bounds = new DockBounds(initial.Left, initial.Top, initial.Right - initial.Left, initial.Bottom - initial.Top);
        var frames = new List<(int Time, System.Drawing.Bitmap Image)>();
        var brightness = new List<(int Time, double Value)>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var stage = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var ms = (int)watch.ElapsedMilliseconds;
            if (stage == 0 && ms >= 1200)
            {
                stage++;
                type.GetMethod("Collapse", flags)!.Invoke(controller, [bounds]);
            }
            if (stage == 1 && ms >= 2200)
            {
                stage++;
                type.GetMethod("Expand")!.Invoke(controller, [true]);
            }
            if (ms >= 1000 && ms < 2900)
            {
                var bitmap = new System.Drawing.Bitmap(initial.Right - initial.Left, initial.Bottom - initial.Top);
                using var graphics = System.Drawing.Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(initial.Left, initial.Top, 0, 0, bitmap.Size);
                frames.Add((ms, bitmap));
                var pixel = bitmap.GetPixel(bitmap.Width / 2, (int)(bitmap.Height * 0.92));
                brightness.Add((ms, (pixel.R + pixel.G + pixel.B) / 3.0));
            }
            if (ms < 3200) return;
            timer.Stop();
            window.Close();
            backdrop.Close();
            foreach (var (time, bitmap) in frames)
            {
                bitmap.Save(Path.Combine(outputDirectory, $"frame-{time:D5}.png"));
                bitmap.Dispose();
            }
            File.WriteAllText(Path.Combine(outputDirectory, "timing.txt"),
                "Collapse at 1200ms; expand at 2200ms; timestamps are actual elapsed capture times.\n" +
                string.Join("\n", frames.Select(frame => frame.Time)));
            app.Shutdown();
        };
        timer.Start();
        app.Run();
        // The empty lower panel area must fade monotonically in each direction.
        // This catches a bright/blank frame even if native size checks still pass.
        double Reversal(int start, int end, bool expanding)
        {
            var values = brightness.Where(frame => frame.Time >= start && frame.Time <= end).ToArray();
            if (values.Length < 5) throw new InvalidOperationException("动画采样不足，不能认定视觉验收通过");
            return values.Zip(values.Skip(1), (a, b) => expanding ? a.Value - b.Value : b.Value - a.Value)
                .Max();
        }
        var collapseReversal = Math.Max(0, Reversal(1100, 2100, false));
        var expandReversal = Math.Max(0, Reversal(2150, 2850, true));
        File.WriteAllText(Path.Combine(outputDirectory, "audit.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            Edge = edge.ToString(), FrameCount = frames.Count, CollapseReversal = collapseReversal,
            ExpandReversal = expandReversal, Passed = collapseReversal <= 12 && expandReversal <= 12
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (collapseReversal > 12 || expandReversal > 12)
            throw new InvalidOperationException("连续截图出现反向亮度跳变，请检查输出画面");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
}
