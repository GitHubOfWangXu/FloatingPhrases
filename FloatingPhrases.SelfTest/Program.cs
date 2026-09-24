using FloatingPhrases;

if (args.Length == 3 && args[0] == "--ui-export")
{
    LayoutChecks.Run(args[1], args[2]);
    return 0;
}

if (args.Length == 1 && args[0] is "--app-visual" or "--todo-app-visual")
{
    DockVisualChecks.RunApp(args[0] == "--todo-app-visual" ? "TodoTab" : null);
    return 0;
}

if (args.Length is 2 or 3 && args[0] == "--dock-visual")
{
    DockVisualChecks.Run(args[1], args.Length == 3 ? Enum.Parse<DockEdge>(args[2], true) : DockEdge.Left);
    return 0;
}

var testDirectory = Path.Combine(Path.GetTempPath(), $"FloatingPhrases-{Guid.NewGuid():N}");
var testFile = Path.Combine(testDirectory, "phrases.json");
var folderTestFile = Path.Combine(testDirectory, "folders.json");
var appTestFile = Path.Combine(testDirectory, "apps.json");
var settingsTestFile = Path.Combine(testDirectory, "settings.json");

try
{
    Directory.CreateDirectory(testDirectory);
    if (args.Length == 1 && args[0] == "--clipboard-copy-smoke")
    {
        TodoChecks.RunClipboardCopy(testDirectory);
        return 0;
    }
    TodoChecks.Run(testDirectory, args.Length == 2 && args[0] == "--todo-screenshot" ? Path.GetFullPath(args[1]) : null);
    var memory = new MemoryUsage(16UL * 1073741824, 4UL * 1073741824);
    Require(memory.UsedBytes == 12UL * 1073741824 && memory.UsedPercent == 75,
        "系统内存应按总量减去可用量计算，不能误用本进程内存");
    Require(new MemoryUsage(0, 0).UsedPercent == 0, "零总量不能产生无效百分比");
    Require(new MemoryUsage(1, 2).UsedBytes == 0, "异常可用量不能导致无符号下溢");
    var liveMemory = SystemMemory.Read();
    Require(liveMemory is { TotalBytes: > 0 } && liveMemory.Value.AvailableBytes <= liveMemory.Value.TotalBytes,
        "Windows 内存 API 应返回有效的物理内存快照");
    var store = new PhraseStore(testFile);
    var expected = new[]
    {
        new Phrase { Title = "测试", Group = "自检", Text = "  第一行\n第二行  ", IsPinned = true },
        new Phrase { Title = "空分组归一化", Group = " ", Text = "内容" }
    };

    store.Save(expected);
    var loaded = store.Load();

    Require(loaded.Count == 2, "保存后应读回两条短语");
    Require(loaded[0].Text == "  第一行\n第二行  ", "多行内容和首尾空白必须保持不变");
    Require(loaded[0].IsPinned, "置顶状态必须保持不变");
    Require(loaded[1].Group == "常用", "空分组应归一化为常用");

    File.WriteAllText(testFile, "[null,{\"title\":\"有效\",\"group\":\"常用\",\"text\":\"内容\"}]");
    Require(store.Load().Count == 1, "无效的空条目不应阻止其他短语加载");

    File.WriteAllText(testFile, "{broken json");
    var recovered = store.Load();
    Require(recovered.Count > 0, "损坏数据应恢复为默认短语");
    Require(Directory.EnumerateFiles(testDirectory, "phrases.corrupt-*.json").Any(), "损坏文件应被保留");

    var folderStore = new FolderStore(folderTestFile);
    folderStore.Save(
    [
        new FolderShortcut { Name = "自检目录", Path = testDirectory, IsPinned = true },
        new FolderShortcut { Name = "第二个目录", Path = testDirectory }
    ]);
    var loadedFolders = folderStore.Load();
    Require(loadedFolders.Count == 2, "保存后应读回两个文件夹快捷入口");
    Require(loadedFolders[0].Path == testDirectory, "文件夹路径必须保持不变");
    Require(loadedFolders[0].IsPinned, "文件夹置顶状态必须保持不变");

    var appStore = new AppStore(appTestFile);
    appStore.Save(
    [
        new AppShortcut { Name = "自检程序", Path = testFile, Category = AppCategories.Development, IsPinned = true },
        new AppShortcut { Name = "未知分类", Path = folderTestFile, Category = "不存在的分类" }
    ]);
    var loadedApps = appStore.Load();
    Require(loadedApps.Count == 2, "保存后应读回两个软件快捷入口");
    Require(loadedApps[0].Category == AppCategories.Development, "软件分类必须保持不变");
    Require(loadedApps[0].IsPinned, "软件置顶状态必须保持不变");
    Require(loadedApps[1].Category == AppCategories.Other, "未知软件分类应归一化为其他");
    Require(AppCategories.Infer("Visual Studio Code", "Code.exe") == AppCategories.Development, "VS Code 应识别为开发工具");
    Require(AppCategories.Infer("微信", "Weixin.exe") == AppCategories.Office, "微信应识别为办公沟通");

    var desktopApps = DesktopAppImporter.FindApps();
    Require(desktopApps.All(app => File.Exists(app.Path)), "桌面导入结果必须指向现有程序或快捷方式");
    Require(desktopApps.All(app => AppCategories.All.Contains(app.Category)), "桌面导入结果必须使用有效分类");

    var order = new List<string> { "A", "B", "C" };
    Require(ItemOrder.Move(order, "C", "A", false), "应能把项目移动到目标之前");
    Require(order.SequenceEqual(["C", "A", "B"]), "向前拖动后的顺序不正确");
    Require(ItemOrder.Move(order, "C", "B", true), "应能把项目移动到目标之后");
    Require(order.SequenceEqual(["A", "B", "C"]), "向后拖动后的顺序不正确");

    var settingsStore = new SettingsStore(settingsTestFile);
    settingsStore.Save(new WindowSettings { Mode = WindowMode.ClickThrough, IdleOpacity = 0.42 });
    var loadedSettings = settingsStore.Load();
    Require(loadedSettings.Mode == WindowMode.ClickThrough, "窗口模式必须保持不变");
    Require(Math.Abs(loadedSettings.IdleOpacity - 0.42) < 0.001, "透明度必须保持不变");

    settingsStore.Save(new WindowSettings { Mode = WindowMode.Floating, IdleOpacity = 0.05 });
    Require(Math.Abs(settingsStore.Load().IdleOpacity - 0.2) < 0.001, "透明度应限制在安全范围内");

    var screenshotSelection = ScreenshotSelection.ToPixels(80, 60, 20, 10, 1.5, 2, 300, 200);
    Require(
        screenshotSelection == new ScreenshotRectangle(30, 20, 90, 100),
        "反向拖拽的截图选区应正确换算为像素区域");
    Require(
        ScreenshotSelection.ToPixels(-10, -5, 500, 300, 1, 1, 320, 180)
            == new ScreenshotRectangle(0, 0, 320, 180),
        "截图选区应限制在屏幕图像范围内");
    Require(
        ScreenshotSelection.ToPixels(10, 10, 10, 40, 1, 1, 320, 180) is null,
        "零宽度截图选区应被忽略");

    var area = new DockBounds(0, 0, 1920, 1040);
    Require(EdgeDockLayout.Detect(new(12, 200, 430, 650), area, 16) == DockEdge.Left,
        "靠近左侧工作区边缘应触发贴边");
    Require(EdgeDockLayout.Detect(new(1480, 200, 430, 650), area, 16) == DockEdge.Right,
        "靠近右侧工作区边缘应触发贴边");
    Require(EdgeDockLayout.Detect(new(600, -5, 430, 650), area, 16) == DockEdge.Top,
        "轻微越过上边缘也应触发贴边");
    Require(EdgeDockLayout.Detect(new(17, 200, 430, 650), area, 16) == DockEdge.None,
        "拖离吸附阈值应取消贴边");
    Require(EdgeDockLayout.Detect(new(600, 390, 430, 650), area, 16) == DockEdge.None,
        "底边不应触发收缩，以免干扰任务栏");
    Require(EdgeDockLayout.Detect(new(10, 2, 430, 650), area, 16) == DockEdge.Top,
        "角落应选择距离最近的边缘");
    var leftMonitor = new DockBounds(-1920, -200, 1920, 1040);
    var docked = EdgeDockLayout.Snap(new(-440, 500, 430, 650), leftMonitor, DockEdge.Right);
    Require(docked == new DockBounds(-430, 190, 430, 650),
        "负坐标副屏应贴合自身右边缘，并避开工作区底部");
    var handle = EdgeDockLayout.Handle(docked, leftMonitor, DockEdge.Right, 1.5);
    Require(handle.Width == 66 && handle.Height == 96 && handle.Right == 0,
        "150% 缩放下内存边签应为 66×96 像素，且不伸入相邻屏幕");
    Require(handle.Y >= leftMonitor.Y && handle.Bottom <= leftMonitor.Bottom,
        "边签必须保持在工作区内");
    Require(!handle.Contains(docked.X + 10, docked.Y + 10),
        "收缩后的矩形不能继续覆盖原面板内部");
    var topHandle = EdgeDockLayout.Handle(new(600, 0, 430, 650), area, DockEdge.Top, 2);
    Require(topHandle.Width == 128 && topHandle.Height == 80 && topHandle.Y == 0,
        "顶部边签应横向显示并适配 200% 缩放");
    Require(EdgeDockLayout.Snap(new(500, 500, 430, 650), new(0, 0, 320, 240), DockEdge.Left).Y == 0,
        "工作区小于窗口时应保留标题栏可达，不能抛出钳位异常");
    var taskbarArea = new DockBounds(48, 40, 1872, 1000);
    Require(EdgeDockLayout.Snap(new(50, 20, 430, 650), taskbarArea, DockEdge.Left).X == 48,
        "吸附应使用工作区，避开左侧任务栏");
    foreach (var scale in new[] { 1.0, 1.5, 2.0 })
    {
        foreach (var edge in new[] { DockEdge.Left, DockEdge.Right, DockEdge.Top })
        {
            var originalPanel = EdgeDockLayout.Snap(new(-1200, -100, 430 * scale, 650 * scale), leftMonitor, edge);
            var originalHandle = EdgeDockLayout.Handle(originalPanel, leftMonitor, edge, scale);
            var offsetHandle = originalHandle with
            {
                X = originalHandle.X + (edge == DockEdge.Top ? 70 : 0),
                Y = originalHandle.Y + (edge == DockEdge.Top ? 0 : 25)
            };
            var relocated = EdgeDockLayout.PanelFromHandle(offsetHandle, originalPanel, leftMonitor, edge);
            var relocatedHandle = EdgeDockLayout.Handle(relocated, leftMonitor, edge, scale);
            Require(relocatedHandle.X >= leftMonitor.X && relocatedHandle.Right <= leftMonitor.Right &&
                relocatedHandle.Y >= leftMonitor.Y && relocatedHandle.Bottom <= leftMonitor.Bottom,
                "负坐标屏幕不同缩放下拖动小块后必须仍在工作区内");
            Require(relocated.Width == originalPanel.Width && relocated.Height == originalPanel.Height,
                "拖动小块不得改变完整面板尺寸");
        }
    }
    var releasedHandle = new DockBounds(800, 400, 44, 64);
    Require(EdgeDockLayout.PanelFromHandle(releasedHandle, new(0, 0, 430, 650), area, DockEdge.None)
        == new DockBounds(607, 107, 430, 650), "拖离边缘后应在小块释放位置居中展开，不能返回原边缘");
    var switchedPanel = EdgeDockLayout.PanelFromHandle(new(1876, 400, 44, 64), new(0, 0, 430, 650), area, DockEdge.Right);
    Require(EdgeDockLayout.Handle(switchedPanel, area, DockEdge.Right, 1) == new DockBounds(1876, 400, 44, 64),
        "跨边缘拖动后小块应停在新的释放位置");

    File.WriteAllText(settingsTestFile, "{broken json");
    var recoveredSettings = settingsStore.Load();
    Require(recoveredSettings.Mode == WindowMode.Floating, "损坏设置应恢复到安全的悬浮模式");
    Require(Directory.EnumerateFiles(testDirectory, "settings.corrupt-*.json").Any(), "损坏的设置文件应被保留");

    File.WriteAllText(folderTestFile, "{broken json");
    Require(folderStore.Load().Count == 0, "损坏的文件夹数据应恢复为空列表");
    Require(Directory.EnumerateFiles(testDirectory, "folders.corrupt-*.json").Any(), "损坏的文件夹文件应被保留");

    File.WriteAllText(appTestFile, "{broken json");
    Require(appStore.Load().Count == 0, "损坏的软件数据应恢复为空列表");
    Require(Directory.EnumerateFiles(testDirectory, "apps.corrupt-*.json").Any(), "损坏的软件数据文件应被保留");

    WindowBehaviorChecks.Run();
    Console.WriteLine($"FloatingPhrases self-test passed. Desktop apps found: {desktopApps.Count}.");
    return 0;
}
finally
{
    if (Directory.Exists(testDirectory))
    {
        Directory.Delete(testDirectory, true);
    }
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
