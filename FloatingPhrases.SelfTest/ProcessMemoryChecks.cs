using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FloatingPhrases;

internal static class ProcessMemoryChecks
{
    public static ProcessMemorySnapshot Sample
    {
        get
        {
            using var self = Process.GetCurrentProcess();
            return new(new MemoryUsage(16UL * 1073741824, 3UL * 1073741824),
                [new(210001, "Browser · 很长的通用程序名称用于检查省略", 1400L * 1048576, 1800L * 1048576, 123, self.SessionId),
                 new(210002, "Editor", 500L * 1048576, 800L * 1048576, 124, self.SessionId),
                 new(210003, "SampleService", 100L * 1048576, 180L * 1048576, 125, 0)], 2);
        }
    }

    public static void Run()
    {
        var live = ProcessMemory.Read();
        Check(live.Processes.Any(item => item.Id == Environment.ProcessId && item.WorkingSetBytes > 0 && !item.CanEnd),
            "应能读取自身内存，但不能结束自身");
        Check(live.Processes.Zip(live.Processes.Skip(1)).All(pair => pair.First.WorkingSetBytes >= pair.Second.WorkingSetBytes),
            "进程列表应按驻留内存降序排列");
        var sample = Sample.Processes[0];
        Check(!(sample with { Id = 4 }).CanEnd && !(sample with { StartedFileTime = null }).CanEnd
            && !(sample with { SessionId = 0 }).CanEnd && !(sample with { SessionId = -1 }).CanEnd,
            "系统进程、服务、其他会话和身份不明的进程应禁止结束");
        CheckOwnedChild();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await CheckView(); }
                catch (Exception ex) { failure = ex; }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal); }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("内存详情控件自检失败", failure);
        Console.WriteLine("Memory checks passed: sorting, search, confirmation, protected targets, PID reuse and termination of an isolated test child.");
    }

    private static void CheckOwnedChild()
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        info.ArgumentList.Add("--memory-test-child");
        using var child = Process.Start(info) ?? throw new InvalidOperationException("临时测试子进程未启动");
        try
        {
            var target = new ProcessMemoryInfo(child.Id, child.ProcessName, 0, 0,
                child.StartTime.ToUniversalTime().ToFileTimeUtc(), child.SessionId);
            var refused = false;
            try { ProcessMemory.End(target with { StartedFileTime = target.StartedFileTime + 1 }); }
            catch (InvalidOperationException) { refused = true; }
            Check(refused && !child.HasExited, "启动时间变化时应拒绝，不能只凭 PID 结束进程");
            Check(ProcessMemory.End(target).StartsWith("已结束") && child.WaitForExit(1000),
                "只应成功结束本次创建的临时子进程，并核实退出结果");
        }
        finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(3000); } }
    }

    private static async Task CheckView()
    {
        var snapshot = Sample;
        var confirmed = false;
        var ends = 0;
        string? confirmation = null;
        var window = new ProcessMemoryWindow(() => snapshot, text => { confirmation = text; return confirmed; }, target =>
        {
            ends++;
            snapshot = snapshot with { Processes = snapshot.Processes.Where(item => item.Id != target.Id).ToArray() };
            return "已结束测试目标";
        });
        T Named<T>(string name) => (T)window.FindName(name);
        try
        {
            window.ShowSnapshot(snapshot);
            Check(ends == 0 && Named<DataGrid>("ProcessList").Items.Count == 3, "查看内存不能自动结束进程");
            Named<TextBox>("SearchBox").Text = "editor";
            Check(Named<DataGrid>("ProcessList").Items.Count == 1, "应按名称忽略大小写搜索");
            Named<TextBox>("SearchBox").Text = "210001";
            var list = Named<DataGrid>("ProcessList");
            Check(list.Items.Count == 1 && list.IsReadOnly, "应按 PID 搜索且禁止编辑进程状态");
            list.SelectedIndex = 0;
            var button = Named<Button>("EndButton");
            Check(button.IsEnabled, "当前会话的可核验进程应提供结束选项");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(ends == 0 && confirmation is not null && confirmation.Contains("210001") && confirmation.Contains("未保存"),
                "确认应展示目标和数据丢失风险，取消不能执行");
            confirmed = true;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => Named<TextBlock>("FeedbackText").Text == "已结束测试目标" && Named<Button>("RefreshButton").IsEnabled);
            Check(ends == 1 && list.Items.Count == 0, "确认后只结束一个目标并读取最新列表");
            Named<TextBox>("SearchBox").Text = "";
            list.SelectedItem = list.Items.Cast<ProcessMemoryInfo>().First(item => item.SessionId == 0);
            Check(!button.IsEnabled, "系统服务进程不得通过普通结束按钮关闭");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(ends == 1, "即使直接触发禁用按钮事件，也必须拒绝受保护目标");
        }
        finally { window.Close(); }
        var failed = new ProcessMemoryWindow(() => Sample, _ => true, _ => throw new Win32Exception("模拟拒绝访问"));
        try
        {
            failed.ShowSnapshot(Sample);
            ((DataGrid)failed.FindName("ProcessList")).SelectedIndex = 0;
            ((Button)failed.FindName("EndButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => ((TextBlock)failed.FindName("FeedbackText")).Text.StartsWith("未能结束"));
            Check(((TextBlock)failed.FindName("FeedbackText")).Text.Contains("模拟拒绝访问"), "失败必须保留原因，不得显示成功");
        }
        finally { failed.Close(); }
        var readFails = true;
        var recovery = new ProcessMemoryWindow(() => readFails ? throw new Win32Exception("模拟读取失败") : Sample,
            _ => false, _ => throw new InvalidOperationException("读取检查不得结束进程"));
        try
        {
            recovery.ShowSnapshot(Sample);
            var refresh = (Button)recovery.FindName("RefreshButton");
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => ((TextBlock)recovery.FindName("MemorySummary")).Text.StartsWith("读取失败") && refresh.IsEnabled);
            Check(((DataGrid)recovery.FindName("ProcessList")).Items.Count == 0
                && !((Button)recovery.FindName("EndButton")).IsEnabled, "读取失败应清除旧列表并禁用结束操作");
            readFails = false;
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => ((DataGrid)recovery.FindName("ProcessList")).Items.Count == 3 && refresh.IsEnabled);
            Check(((TextBlock)recovery.FindName("FeedbackText")).Text == "选择一个进程查看控制选项。", "读取恢复后应清除过期错误提示");
        }
        finally { recovery.Close(); }
    }

    private static async Task Until(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(5)) throw new InvalidOperationException("内存详情异步控件检查超时");
            await Task.Delay(10);
        }
        // The result text is set before the subsequent background refresh completes.
        await Task.Yield();
    }

    public static void Export(string output)
    {
        foreach (var width in new[] { 560, 700 })
        foreach (var dpi in new[] { 96, 144, 192 })
        {
            var window = new ProcessMemoryWindow(() => Sample, _ => false, _ => throw new InvalidOperationException("布局检查不得结束进程"));
            try
            {
                window.ShowSnapshot(Sample);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(width, 520));
                content.Arrange(new Rect(0, 0, width, 520));
                content.UpdateLayout();
                foreach (var name in new[] { "MemorySummary", "SearchBox", "RefreshButton", "ProcessList", "EndButton", "CountText" })
                {
                    var element = (FrameworkElement)window.FindName(name);
                    var bounds = element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
                    Check(bounds.Left >= 0 && bounds.Right <= width && bounds.Top >= 0 && bounds.Bottom <= 520 && bounds.Height > 0,
                        $"内存详情 {name} 在 {width} 宽度下必须可见");
                }
                var bitmap = new RenderTargetBitmap(width * dpi / 96, 520 * dpi / 96, dpi, dpi, PixelFormats.Pbgra32);
                var background = new DrawingVisual();
                using (var drawing = background.RenderOpen())
                    drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, 520));
                bitmap.Render(background);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, $"memory-{width}-{dpi}.png"));
                encoder.Save(file);
            }
            finally { window.Close(); }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
