using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using FloatingPhrases;

internal static class ComputerChecks
{
    public static ComputerSnapshot SampleSnapshot => new(32, true,
        new MemoryUsage(16UL * 1073741824, 6UL * 1073741824),
        [new(@"C:\", 512L * 1073741824, 180L * 1073741824),
         new(@"D:\", 1024L * 1073741824, 640L * 1073741824)], "",
        [new("Wi-Fi", 2.4 * 1048576, 128 * 1024),
         new("Ethernet · 很长的通用网卡名称用于检查自动省略", 0, 0)], "");

    public static void Run()
    {
        var previous = new CpuTimes(100, 200, 100);
        Check(new CpuTimes(120, 260, 140).UsageSince(previous) == 80,
            "CPU 应按系统计时增量计算，内核时间已包含空闲时间");
        Check(previous.UsageSince(previous) is null, "零间隔不能显示伪造的 CPU 使用率");
        Check(new CpuTimes(200, 280, 120).UsageSince(previous) == 0, "完全空闲应为 0%");
        Check(new CpuTimes(100, 280, 120).UsageSince(previous) == 100, "无空闲增量应为 100%");
        Check(new CpuTimes(90, 260, 140).UsageSince(previous) is null, "计数器回退应重新采样");
        Check(new CpuTimes(300, 260, 140).UsageSince(previous) is null, "异常空闲增量不能生成负百分比");
        Check(NetworkStatus.RateSince(100, 500, 2) == 200, "网速应使用字节差与实际采样间隔");
        Check(NetworkStatus.RateSince(500, 100, 2) is null, "网卡重置不能生成负网速");
        Check(NetworkStatus.RateSince(100, 500, 0) is null, "零间隔应等待下次采样");
        Check(NetworkStatus.RateSince(100, 100, 2) == 0, "空闲网卡应显示 0 而非读取失败");
        Check(NetworkStatus.FormatRate(1048576).Contains("MiB/s")
            && NetworkStatus.FormatRate(null) == "采样中…", "网速应区分单位和初次采样");
        Check(new DiskStatus("Sample", 100, 25).UsedPercent == 75
            && new DiskStatus("Sample", 0, 0).UsedPercent == 0, "磁盘应显示已用比例并兼容零容量");

        var reader = new SystemStatus();
        var first = reader.Read();
        Thread.Sleep(60);
        var next = reader.Read();
        Check(first.CpuAvailable && first.CpuPercent is null && next.CpuPercent is >= 0 and <= 100,
            "Windows CPU API 应在第二次采样返回有效百分比");
        Check(next.Memory is { TotalBytes: > 0 }, "电脑页应复用系统物理内存读取");
        Check(next.Disks.All(disk => disk.UsedPercent is >= 0 and <= 100), "磁盘使用率应在有效范围内");
        Check(next.Networks.All(network => network.ReceiveBytesPerSecond is null or >= 0
            && network.SendBytesPerSecond is null or >= 0), "真实网卡速率不能为负值");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { CheckView(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("电脑页界面自检失败", failure);
        Console.WriteLine("Computer checks passed: sampling, counter resets, unavailable states and system-tool button events.");
    }

    private static void CheckView()
    {
        var launched = new List<ProcessStartInfo>();
        var memoryOpened = 0;
        var view = new ComputerView(launched.Add, () => memoryOpened++);
        T Named<T>(string name) => (T)view.FindName(name);
        Check(launched.Count == 0, "加载电脑页不得打开系统工具");
        Named<Button>("MemoryDetailsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(memoryOpened == 1 && launched.Count == 0, "内存详情按钮应只打开详情窗口，不直接控制进程");
        view.ShowStatus(SampleSnapshot);
        view.Measure(new Size(338, 455));
        view.Arrange(new Rect(0, 0, 338, 455));
        view.UpdateLayout();
        Check(Named<TextBlock>("CpuText").Text == "32%"
            && Named<ProgressBar>("MemoryBar").Value == 62.5, "状态卡片应显示快照中的真实数值");
        Check(Named<ItemsControl>("DiskList").Items.Count == 2
            && Named<ItemsControl>("NetworkList").Items.Count == 2, "各磁盘和网卡应独立呈现");
        var expected = new[] { "Taskmgr.exe", "resmon.exe", "ms-settings:sound", "ms-settings:display",
            "ms-settings:network-status", "ms-settings:powersleep", "ms-settings:storagesense",
            "ms-settings:appsfeatures", "ms-settings:startupapps", "ms-settings:windowsupdate", "mmc.exe" };
        var buttons = Named<System.Windows.Controls.Primitives.UniformGrid>("ToolButtons").Children.OfType<Button>().ToArray();
        Check(buttons.Length == expected.Length, "系统管理入口应完整显示");
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(launched.Count == i + 1 && launched[i].UseShellExecute && launched[i].Arguments.Length == 0,
                "每次点击只应请求打开一个系统工具，不运行终端命令");
            var target = i < 2 || i == 10 ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), expected[i]) : expected[i];
            Check(launched[i].FileName == target && string.IsNullOrEmpty(launched[i].Verb),
                "入口应指向固定的系统目标，且不自动请求提权");
            Check(i == 10
                ? launched[i].ArgumentList.SequenceEqual([Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "services.msc")])
                : launched[i].ArgumentList.Count == 0, "服务入口只能打开系统服务控制台");
        }
        view.ShowStatus(new(null, false, null, [], "磁盘读取失败", [], "没有已连接的网卡"));
        Check(Named<TextBlock>("CpuText").Text == "暂不可用" && Named<TextBlock>("MemoryText").Text == "--"
            && Named<ItemsControl>("DiskList").Items.Count == 0
            && Named<TextBlock>("NetworkMessage").Text == "没有已连接的网卡", "读取失败或断网不能残留旧的正常状态");
        view.ShowStatus(new(null, true, null, [], "", [new("Sample", null, null)], ""));
        Check(Named<TextBlock>("CpuText").Text == "采样中…", "初次采样不应误报 0% 使用率");
        var failed = new ComputerView(_ => throw new Win32Exception("模拟系统入口不可用"));
        ((Button)failed.FindName("TaskManagerButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((TextBlock)failed.FindName("FeedbackText")).Text.StartsWith("无法打开"), "入口启动失败必须反馈错误");
        try { SystemTools.CreateStartInfo((SystemTool)int.MaxValue); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new InvalidOperationException("未知系统入口必须拒绝");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
