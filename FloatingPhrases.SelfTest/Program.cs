using FloatingPhrases;

var testDirectory = Path.Combine(Path.GetTempPath(), $"FloatingPhrases-{Guid.NewGuid():N}");
var testFile = Path.Combine(testDirectory, "phrases.json");
var folderTestFile = Path.Combine(testDirectory, "folders.json");
var appTestFile = Path.Combine(testDirectory, "apps.json");
var settingsTestFile = Path.Combine(testDirectory, "settings.json");
var captureTestFile = Path.Combine(testDirectory, "feishu-captures.json");

try
{
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

    var captureStore = new PhraseStore(captureTestFile, seedDefaults: false);
    Require(captureStore.Load().Count == 0, "飞书复制历史首次加载应为空");
    captureStore.Save([new Phrase { Title = "飞书选区", Group = "飞书", Text = "复制内容" }]);
    Require(captureStore.Load().Single().Text == "复制内容", "飞书复制历史应独立保存并读回");
    File.WriteAllText(captureTestFile, "{broken json");
    Require(captureStore.Load().Count == 0, "损坏的飞书复制历史应恢复为空列表");
    Require(Directory.EnumerateFiles(testDirectory, "feishu-captures.corrupt-*.json").Any(), "损坏的飞书复制历史应使用独立文件名前缀保留");

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

    var combinedSelection = SelectedTextReader.CombineSelections(["第一段", "", null, "第二段  "]);
    Require(combinedSelection == $"第一段{Environment.NewLine}第二段  ", "多段文本选区应按行合并并保留末尾空白");

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
