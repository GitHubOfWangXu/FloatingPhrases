using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FloatingPhrases;

internal static class TodoChecks
{
    // Opt-in: exercises the real clipboard and leaves generic text for Win+V verification.
    public static void RunClipboardCopy(string directory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var text = "FloatingPhrases clipboard check app " + Guid.NewGuid().ToString("N");
                var store = new TodoStore(Path.Combine(directory, "clipboard-todos.json"));
                store.Save([new TodoItem { Text = text }]);
                var view = new TodoListView(store);
                ((Button)view.FindName("CopyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(((TextBlock)view.FindName("FeedbackText")).Text.StartsWith("已复制 1 条"),
                    "真实剪贴板写入必须完整成功，不能仅在写入后抛错");
                Check(System.Windows.Clipboard.GetText() == text + " 0%", "真实剪贴板应读回进度文本");
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("真实剪贴板回归失败", failure);
        Console.WriteLine("Clipboard copy smoke passed; generic checklist left for Win+V verification.");
    }

    public static void Run(string directory, string? screenshot = null)
    {
        var path = Path.Combine(directory, "todos.json");
        var store = new TodoStore(path);
        Check(store.Load().Count == 0, "新待办列表应为空");
        var compatibilityPath = Path.Combine(directory, "legacy-todos.json");
        var legacyJson = "[{\"text\":\"旧完成待办\",\"isCompleted\":true},{\"text\":\"旧未完成待办\"}]";
        File.WriteAllText(compatibilityPath, legacyJson);
        var compatibilityStore = new TodoStore(compatibilityPath);
        var legacyItems = compatibilityStore.Load();
        Check(legacyItems.Count == 2 && legacyItems[0].Progress == 100 && legacyItems[0].IsCompleted &&
            legacyItems[1].Progress == 0 && !legacyItems[1].IsCompleted,
            "旧 JSON 缺少进度字段时应兼容，并把旧完成项归一化为 100%");
        Check(File.ReadAllText(compatibilityPath) == legacyJson, "读取旧 JSON 不能改写原文件");
        compatibilityStore.Save(legacyItems);
        Check(File.ReadAllText(compatibilityPath).Contains("\"progress\": 100") &&
            File.ReadAllText(compatibilityPath).Contains("\"progress\": 0") &&
            File.ReadAllText(compatibilityPath + ".bak") == legacyJson,
            "旧数据首次保存应写入进度字段，并在备份中保留原 JSON");

        var first = new TodoItem { Text = "  整理今日计划  ", Progress = 25 };
        var done = new TodoItem { Text = "检查构建结果", IsCompleted = true };
        store.Save([first, done]);
        Check(store.Load()[0].Text == "整理今日计划", "待办保存应移除首尾空白");
        Check(store.Load()[0].Progress == 25 && store.Load()[1].Progress == 100 && store.Load()[1].IsCompleted,
            "部分进度和完成状态应往返持久化，完成项应为 100%");
        var normalizationStore = new TodoStore(Path.Combine(directory, "normalized-todos.json"));
        normalizationStore.Save([
            new TodoItem { Text = "过低进度", Progress = -1 },
            new TodoItem { Text = "过高进度", Progress = 101 }
        ]);
        Check(normalizationStore.Load().Select(item => item.Progress).SequenceEqual([0, 100]) &&
            normalizationStore.Load()[1].IsCompleted,
            "存储应将进度归一化到 0 至 100，并同步完成状态");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                string? copiedText = null;
                var clipboardBusy = false;
                var view = new TodoListView(store, text =>
                {
                    if (clipboardBusy) throw new System.Runtime.InteropServices.ExternalException();
                    copiedText = text;
                }) { Width = 390, Height = 490 };
                void Layout()
                {
                    view.Measure(new System.Windows.Size(390, 490));
                    view.Arrange(new Rect(0, 0, 390, 490));
                    view.UpdateLayout();
                }
                T Named<T>(string name) => (T)view.FindName(name);
                void Click(string name) => Named<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FrameworkElement RowControl(Guid id, string? label)
                {
                    Layout();
                    return Descendants(Named<ItemsControl>("TodoList")).OfType<FrameworkElement>()
                        .First(element => element.Tag is TodoItem item && item.Id == id &&
                            (label is null ? element is CheckBox : element is Button button && Equals(button.Content, label)));
                }
                void RowClick(Guid id, string? label) => RowControl(id, label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var savedBeforeCopy = File.ReadAllText(path);
                Check(Named<Button>("CopyButton").IsEnabled, "有待办时应允许复制");
                Click("CopyButton");
                Check(copiedText == "整理今日计划 25%" + Environment.NewLine + "检查构建结果 100%",
                    "复制应保留显示顺序和进度文本，不添加清单前缀");
                Check(File.ReadAllText(path) == savedBeforeCopy, "复制不能修改待办存储");
                clipboardBusy = true;
                Click("CopyButton");
                Check(Named<TextBlock>("FeedbackText").Text.Contains("请重试"), "剪贴板占用时应提示重试");
                clipboardBusy = false;

                Named<TextBox>("InputBox").Text = "   ";
                Click("SaveButton");
                Check(store.Load().Count == 2, "空白输入不能添加待办");
                Named<TextBox>("InputBox").Text = "非法进度待办";
                Named<TextBox>("ProgressBox").Text = "101";
                Click("SaveButton");
                Check(store.Load().Count == 2 && Named<TextBox>("InputBox").Text == "非法进度待办" &&
                    Named<TextBox>("ProgressBox").Text == "101",
                    "超出范围的进度应阻止保存并保留输入");
                Named<TextBox>("ProgressBox").Text = "abc";
                Click("SaveButton");
                Check(store.Load().Count == 2 && Named<TextBox>("ProgressBox").Text == "abc",
                    "非整数进度应阻止保存并保留输入");
                Named<TextBox>("InputBox").Text = "准备会议资料";
                Named<TextBox>("ProgressBox").Text = "40";
                Click("SaveButton");
                var added = store.Load().First();
                Check(added.Text == "准备会议资料" && added.Progress == 40 && !added.IsCompleted,
                    "添加按钮应持久化部分进度");
                Check(Named<TextBox>("ProgressBox").Text == "0", "新增保存后进度输入应重置为 0");
                RowClick(added.Id, "改");
                Check(Named<TextBox>("ProgressBox").Text == "40", "编辑应读回已有进度");
                Named<TextBox>("InputBox").Text = "准备明天的会议资料";
                Named<TextBox>("ProgressBox").Text = "60";
                Click("SaveButton");
                Check(store.Load().First().Id == added.Id && store.Load().First().Text == "准备明天的会议资料" &&
                    store.Load().First().Progress == 60,
                    "编辑应保留原 ID 并保存内容和进度");
                Layout();
                Check(Descendants(Named<ItemsControl>("TodoList")).OfType<TextBlock>()
                    .Any(block => block.Text == "60%"), "待办行内应显示进度百分比");
                RowClick(added.Id, "改");
                Named<TextBox>("InputBox").Text = "取消的修改";
                Named<TextBox>("ProgressBox").Text = "50";
                Click("CancelButton");
                Check(store.Load().First().Text == "准备明天的会议资料" && store.Load().First().Progress == 60 &&
                    Named<TextBox>("ProgressBox").Text == "0", "取消编辑不能写入修改，并应重置进度输入");
                RowClick(added.Id, "改");
                RowClick(added.Id, null);
                Check(store.Load().First().IsCompleted && store.Load().First().Progress == 100,
                    "勾选应保存完成状态和 100% 进度");
                Check(Named<TextBox>("ProgressBox").Text == "100", "编辑中勾选完成后进度输入应同步为 100");
                RowClick(added.Id, null);
                Check(!store.Load().First().IsCompleted && store.Load().First().Progress == 0 &&
                    Named<TextBox>("ProgressBox").Text == "0", "编辑中取消勾选后进度输入应同步为 0");
                Named<TextBox>("InputBox").Text = "编辑保存失败的输入应保留";
                Named<TextBox>("ProgressBox").Text = "55";
                Directory.CreateDirectory(path + ".tmp");
                Click("SaveButton");
                Check(store.Load().First().Text == "准备明天的会议资料" && store.Load().First().Progress == 0 &&
                    Named<TextBox>("InputBox").Text == "编辑保存失败的输入应保留" &&
                    Named<TextBox>("ProgressBox").Text == "55" && Named<TextBlock>("FeedbackText").Text.StartsWith("保存失败"),
                    "编辑保存失败时不能覆盖已存待办或清空编辑输入");
                Directory.Delete(path + ".tmp");
                Click("CancelButton");
                RowClick(added.Id, "改");
                Named<TextBox>("ProgressBox").Text = "100";
                Click("SaveButton");
                Check(store.Load().First().IsCompleted && store.Load().First().Progress == 100,
                    "输入 100% 保存应自动标记完成");
                RowClick(added.Id, "改");
                Named<TextBox>("ProgressBox").Text = "75";
                Click("SaveButton");
                Check(!store.Load().First().IsCompleted && store.Load().First().Progress == 75,
                    "已完成待办调低进度应恢复未完成，不能被旧完成状态覆盖");
                RowClick(added.Id, null);
                Named<ComboBox>("FilterBox").SelectedIndex = 1;
                Check(Named<ItemsControl>("TodoList").Items.Count == 1, "未完成筛选应排除已完成项");
                Click("CopyButton");
                Check(copiedText == "整理今日计划 25%", "复制应遵循未完成筛选并含进度");
                Named<TextBox>("SearchBox").Text = "不存在的内容";
                Check(Named<ItemsControl>("TodoList").Items.Count == 0, "搜索应更新列表");
                Check(!Named<Button>("CopyButton").IsEnabled, "无匹配待办时应禁用复制");
                Click("CopyButton");
                Check(copiedText == "整理今日计划 25%", "空清单不能覆盖剪贴板");
                Named<TextBox>("SearchBox").Clear();
                Named<ComboBox>("FilterBox").SelectedIndex = 2;
                Named<TextBox>("SearchBox").Text = "构建";
                Click("CopyButton");
                Check(copiedText == "检查构建结果 100%", "复制应同时遵循搜索、已完成筛选和进度");
                Named<TextBox>("SearchBox").Clear();
                Named<ComboBox>("FilterBox").SelectedIndex = 0;
                RowClick(added.Id, null);
                Check(!store.Load().First().IsCompleted && store.Load().First().Progress == 0,
                    "再次勾选应恢复未完成和 0% 进度");
                RowClick(added.Id, "删");
                Check(store.Load().Count == 2, "删除按钮应保存移除结果");
                Click("UndoButton");
                Check(store.Load().First().Id == added.Id, "撤销删除应恢复 ID、状态和原顺序");

                Directory.CreateDirectory(path + ".tmp");
                Named<TextBox>("InputBox").Text = "保存失败的输入应保留";
                Named<TextBox>("ProgressBox").Text = "55";
                Click("SaveButton");
                Check(store.Load().Count == 3 && Named<ItemsControl>("TodoList").Items.Count == 3 &&
                    Named<TextBox>("InputBox").Text.Length > 0 && Named<TextBox>("ProgressBox").Text == "55" &&
                    Named<TextBlock>("FeedbackText").Text.StartsWith("保存失败"),
                    "保存失败时不能修改列表或清空内容、进度输入");
                Directory.Delete(path + ".tmp");

                var reloaded = new TodoListView(store);
                Check(((ItemsControl)reloaded.FindName("TodoList")).Items.Count == 3, "重建页面后应读回待办");
                var emptyView = new TodoListView(new TodoStore(Path.Combine(directory, "empty-todos.json")));
                Check(!((Button)emptyView.FindName("CopyButton")).IsEnabled, "新清单应禁用复制");
                var multilineStore = new TodoStore(Path.Combine(directory, "multiline-todos.json"));
                multilineStore.Save([new TodoItem { Text = "整理记录\r\n补充结论\n发送摘要", Progress = 35 }]);
                var multilineView = new TodoListView(multilineStore, text => copiedText = text);
                ((Button)multilineView.FindName("CopyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(copiedText == "整理记录" + Environment.NewLine + "补充结论" + Environment.NewLine + "发送摘要 35%",
                    "多行待办复制应保持原换行，不添加前缀或续行缩进，并在末尾添加进度");
                if (screenshot is not null)
                {
                    Named<TextBox>("InputBox").Clear();
                    Named<TextBlock>("FeedbackText").Text = "所有改动已保存到本机";
                    Layout();
                    var bitmap = new RenderTargetBitmap(390, 490, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(view);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(screenshot);
                    encoder.Save(stream);
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("待办界面回归失败", failure);

        File.WriteAllText(path, "[null,{\"text\":\"有效待办\"},{\"text\":\" \"}]");
        Check(store.Load().Count == 1, "应忽略空待办项");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new[] { first, first, first with { Id = Guid.Empty } }));
        Check(store.Load().Select(item => item.Id).Distinct().Count() == 3, "重复或空 ID 应修复，避免一次操作修改多条待办");
        File.WriteAllText(path, "{broken json");
        Check(store.Load().Count == 0 && Directory.EnumerateFiles(directory, "todos.corrupt-*.json").Any(),
            "损坏文件应保留副本并恢复为空列表");
        Check(File.Exists(path + ".bak"), "保存应保留上一版备份");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
