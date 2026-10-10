using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FloatingPhrases;

internal static class DocumentViewChecks
{
    public static void Run(string directory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { CheckView(directory); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Markdown 界面回归失败", failure);
    }

    private static void CheckView(string directory)
    {
        var vault = Path.Combine(directory, "view-vault");
        Directory.CreateDirectory(vault);
        var inside = Path.Combine(vault, "笔记 & 100%.md");
        var outside = Path.Combine(directory, "README.md");
        File.WriteAllText(inside, "# 笔记\n");
        File.WriteAllText(outside, "# README\n");
        var code = Path.Combine(directory, "view-Code.exe");
        File.WriteAllText(code, "不会执行");
        var obsidian = Path.Combine(directory, "view-Obsidian.exe");
        File.WriteAllText(obsidian, "不会执行");
        var data = Path.Combine(directory, "view-documents.json");
        var store = new DocumentStore(data);
        ProcessStartInfo? launched = null;
        string? copied = null;
        var view = new DocumentListView(store, new DocumentLauncher(obsidian, code),
            info => launched = info, text => copied = text, _ => true) { Width = 338, Height = 455 };
        T Named<T>(string name) => (T)view.FindName(name);
        void Layout()
        {
            view.Measure(new Size(338, 455));
            view.Arrange(new Rect(0, 0, 338, 455));
            view.UpdateLayout();
        }
        Button ButtonFor(string path, object? label)
        {
            Layout();
            return Descendants(Named<ItemsControl>("DocumentList")).OfType<Button>()
                .First(button => button.Tag is DocumentShortcut item && item.Path == path
                    && (label is null ? button.Content is StackPanel : Equals(button.Content, label)));
        }
        void RowClick(string path, object? label) => ButtonFor(path, label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Click(string name) => Named<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Named<ItemsControl>("DocumentList").Items.Count == 0, "初始列表应为空");
        var files = new DataObject(DataFormats.FileDrop, new[] { inside, outside, inside });
        var dropped = DragArgs(files, view);
        dropped.RoutedEvent = System.Windows.DragDrop.PreviewDropEvent;
        view.RaiseEvent(dropped);
        Check(store.Load().Count == 2, "添加应去重并保存入口");
        Check(store.Load().All(item => item.OpenMode == DocumentOpenMode.Obsidian) && Named<ComboBox>("OpenModeBox").Items.Count == 2,
            "新增文档应默认 Obsidian，打开方式只提供两个明确选项");
        view.AddPaths([inside]);
        Check(store.Load().Count == 2, "重复添加不能产生新入口");
        view.AddPaths([Path.Combine(directory, "not-markdown.txt")]);
        Check(store.Load().Count == 2, "非法文件不能添加");
        var targetCard = ButtonFor(outside, null);
        var sourceItem = (DocumentShortcut)ButtonFor(inside, null).Tag;
        var reordered = DragArgs(new DataObject(typeof(DocumentShortcut), sourceItem), targetCard);
        reordered.RoutedEvent = System.Windows.DragDrop.DropEvent;
        targetCard.RaiseEvent(reordered);
        Check(store.Load()[0].Path == outside && store.Load()[1].Path == inside, "真实拖放事件应保存文档顺序");
        // Restore the known order for the edit/save-failure checks below.
        var restored = DragArgs(new DataObject(typeof(DocumentShortcut), sourceItem), ButtonFor(outside, null));
        restored.RoutedEvent = System.Windows.DragDrop.DropEvent;
        ButtonFor(outside, null).RaiseEvent(restored);
        Named<TextBox>("SearchBox").Text = "README";
        Check(Named<ItemsControl>("DocumentList").Items.Count == 1, "应按名称搜索");
        Named<TextBox>("SearchBox").Text = "view-vault";
        Check(Named<ItemsControl>("DocumentList").Items.Count == 1, "应按路径搜索");
        Named<TextBox>("SearchBox").Text = "";
        RowClick(inside, null);
        Check(launched?.FileName == obsidian && launched.ArgumentList.Single() == inside, "真实卡片事件应选择 Obsidian");
        RowClick(outside, null);
        Check(launched?.FileName == obsidian && launched.ArgumentList.Single() == outside, "库外真实卡片事件也应默认 Obsidian");
        var card = ButtonFor(inside, null);
        var row = (Grid)card.Parent;
        var menu = row.ContextMenu;
        menu.PlacementTarget = row;
        ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(launched?.FileName == code && store.Load()[0].OpenMode == DocumentOpenMode.Obsidian,
            "右键临时覆盖软件不能修改默认模式");
        ((MenuItem)menu.Items[4]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(copied == inside, "复制路径应使用同一原文件");
        ((MenuItem)menu.Items[3]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(launched?.FileName == vault && launched.UseShellExecute, "所在目录应使用系统打开");
        RowClick(inside, "改");
        Named<TextBox>("NameBox").Text = "置顶笔记";
        Named<CheckBox>("PinnedBox").IsChecked = true;
        Named<ComboBox>("OpenModeBox").SelectedValue = DocumentOpenMode.VSCode;
        Click("SaveButton");
        Check(store.Load().Single(item => item.Path == inside).Name == "置顶笔记" && store.Load().Single(item => item.Path == inside).IsPinned && store.Load().Single(item => item.Path == inside).OpenMode == DocumentOpenMode.VSCode,
            "编辑名称、置顶和默认打开方式应持久化");
        RowClick(outside, "改");
        Named<ComboBox>("OpenModeBox").SelectedValue = DocumentOpenMode.Obsidian;
        Click("SaveButton");
        Check(Named<Border>("EditorPanel").Visibility == Visibility.Collapsed && store.Load().Single(item => item.Path == outside).OpenMode == DocumentOpenMode.Obsidian,
            "库外文件应允许保存 Obsidian 作为默认方式");
        RowClick(outside, "改");
        Named<ComboBox>("OpenModeBox").SelectedValue = DocumentOpenMode.VSCode;
        Click("SaveButton");
        RowClick(outside, null);
        Check(launched?.FileName == code && launched.ArgumentList.Last() == outside, "文件改选 VS Code 后应按保存设置打开");
        var outsideRow = (Grid)ButtonFor(outside, null).Parent;
        outsideRow.ContextMenu.PlacementTarget = outsideRow;
        Check(((MenuItem)outsideRow.ContextMenu.Items[1]).IsEnabled, "库外 Obsidian 右键选项不能禁用");
        ((MenuItem)outsideRow.ContextMenu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(launched?.FileName == obsidian && store.Load().Single(item => item.Path == outside).OpenMode == DocumentOpenMode.VSCode,
            "库外文件可临时用 Obsidian 打开，且不覆盖已保存的 VS Code 选择");
        RowClick(inside, "改");
        Named<TextBox>("NameBox").Text = "不应保存";
        Click("CancelButton");
        Check(store.Load().Single(item => item.Path == inside).Name == "置顶笔记", "取消编辑不能修改入口");
        File.Move(data, data + ".original");
        Directory.CreateDirectory(data);
        RowClick(inside, "改");
        Named<TextBox>("NameBox").Text = "保存失败";
        Click("SaveButton");
        Check(Named<Border>("EditorPanel").Visibility == Visibility.Visible &&
            Named<TextBlock>("FeedbackText").Text.StartsWith("保存失败") && ((DocumentShortcut)ButtonFor(inside, null).Tag).Name == "置顶笔记",
            "保存失败应保留原列表和编辑输入");
        Click("CancelButton");
        RowClick(inside, "删");
        Check(Named<ItemsControl>("DocumentList").Items.Count == 2, "删除保存失败不能移除列表入口");
        Directory.Delete(data);
        File.Move(data + ".original", data);
        RowClick(inside, "删");
        Check(store.Load().Count == 1 && File.Exists(inside), "删除入口应保存，原文档保留");
        File.Delete(outside);
        launched = null;
        RowClick(outside, null);
        Check(launched is null && Named<TextBlock>("FeedbackText").Text.Contains("不存在"), "丢失文件应提示而不启动软件");
        Check(File.ReadAllText(inside) == "# 笔记\n", "编辑入口与菜单不得改写笔记正文");
        var reloaded = new DocumentListView(store, new DocumentLauncher(obsidian, code));
        Check(((ItemsControl)reloaded.FindName("DocumentList")).Items.Count == 1, "重启控件应读回已保存的入口");
    }

    private static DragEventArgs DragArgs(IDataObject data, DependencyObject target) =>
        (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, [data, DragDropKeyStates.None, DragDropEffects.Copy | DragDropEffects.Move, target, new Point(0, 10000)], null)!;

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
