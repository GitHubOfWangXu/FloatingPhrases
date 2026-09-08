using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FloatingPhrases;

internal static class TodoChecks
{
    public static void Run(string directory, string? screenshot = null)
    {
        var path = Path.Combine(directory, "todos.json");
        var store = new TodoStore(path);
        Check(store.Load().Count == 0, "新待办列表应为空");
        var first = new TodoItem { Text = "  整理今日计划  " };
        var done = new TodoItem { Text = "检查构建结果", IsCompleted = true };
        store.Save([first, done]);
        Check(store.Load()[0].Text == "整理今日计划", "待办保存应移除首尾空白");
        Check(store.Load()[1].IsCompleted, "完成状态应持久化");

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
                Check(copiedText == "- [ ] 整理今日计划" + Environment.NewLine + "- [x] 检查构建结果",
                    "复制应保留显示顺序和完成状态，输出日志清单");
                Check(File.ReadAllText(path) == savedBeforeCopy, "复制不能修改待办存储");
                clipboardBusy = true;
                Click("CopyButton");
                Check(Named<TextBlock>("FeedbackText").Text.Contains("请重试"), "剪贴板占用时应提示重试");
                clipboardBusy = false;

                Named<TextBox>("InputBox").Text = "   ";
                Click("SaveButton");
                Check(store.Load().Count == 2, "空白输入不能添加待办");
                Named<TextBox>("InputBox").Text = "准备会议资料";
                Click("SaveButton");
                var added = store.Load().First();
                Check(added.Text == "准备会议资料", "添加按钮应持久化新待办");
                RowClick(added.Id, "改");
                Named<TextBox>("InputBox").Text = "准备明天的会议资料";
                Click("SaveButton");
                Check(store.Load().First().Id == added.Id && store.Load().First().Text == "准备明天的会议资料",
                    "编辑应保留原 ID 并保存内容");
                RowClick(added.Id, "改");
                Named<TextBox>("InputBox").Text = "取消的修改";
                Click("CancelButton");
                Check(store.Load().First().Text == "准备明天的会议资料", "取消编辑不能写入修改");
                RowClick(added.Id, null);
                Check(store.Load().First().IsCompleted, "勾选应保存完成状态");
                Named<ComboBox>("FilterBox").SelectedIndex = 1;
                Check(Named<ItemsControl>("TodoList").Items.Count == 1, "未完成筛选应排除已完成项");
                Click("CopyButton");
                Check(copiedText == "- [ ] 整理今日计划", "复制应遵循未完成筛选");
                Named<TextBox>("SearchBox").Text = "不存在的内容";
                Check(Named<ItemsControl>("TodoList").Items.Count == 0, "搜索应更新列表");
                Check(!Named<Button>("CopyButton").IsEnabled, "无匹配待办时应禁用复制");
                Click("CopyButton");
                Check(copiedText == "- [ ] 整理今日计划", "空清单不能覆盖剪贴板");
                Named<TextBox>("SearchBox").Clear();
                Named<ComboBox>("FilterBox").SelectedIndex = 2;
                Named<TextBox>("SearchBox").Text = "构建";
                Click("CopyButton");
                Check(copiedText == "- [x] 检查构建结果", "复制应同时遵循搜索和已完成筛选");
                Named<TextBox>("SearchBox").Clear();
                Named<ComboBox>("FilterBox").SelectedIndex = 0;
                RowClick(added.Id, null);
                Check(!store.Load().First().IsCompleted, "再次勾选应恢复未完成");
                RowClick(added.Id, "删");
                Check(store.Load().Count == 2, "删除按钮应保存移除结果");
                Click("UndoButton");
                Check(store.Load().First().Id == added.Id, "撤销删除应恢复 ID、状态和原顺序");

                Directory.CreateDirectory(path + ".tmp");
                Named<TextBox>("InputBox").Text = "保存失败的输入应保留";
                Click("SaveButton");
                Check(store.Load().Count == 3 && Named<ItemsControl>("TodoList").Items.Count == 3 &&
                    Named<TextBox>("InputBox").Text.Length > 0 && Named<TextBlock>("FeedbackText").Text.StartsWith("保存失败"),
                    "保存失败时不能修改列表或清空用户输入");
                Directory.Delete(path + ".tmp");

                var reloaded = new TodoListView(store);
                Check(((ItemsControl)reloaded.FindName("TodoList")).Items.Count == 3, "重建页面后应读回待办");
                var emptyView = new TodoListView(new TodoStore(Path.Combine(directory, "empty-todos.json")));
                Check(!((Button)emptyView.FindName("CopyButton")).IsEnabled, "新清单应禁用复制");
                var multilineStore = new TodoStore(Path.Combine(directory, "multiline-todos.json"));
                multilineStore.Save([new TodoItem { Text = "整理记录\r\n补充结论\n发送摘要" }]);
                var multilineView = new TodoListView(multilineStore, text => copiedText = text);
                ((Button)multilineView.FindName("CopyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(copiedText == "- [ ] 整理记录" + Environment.NewLine + "  补充结论" + Environment.NewLine + "  发送摘要",
                    "多行待办应保持换行并缩进续行");
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
