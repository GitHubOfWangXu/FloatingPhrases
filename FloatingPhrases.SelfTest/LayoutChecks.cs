using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using FloatingPhrases;

// Render the shipped XAML with generic data, without opening a window, registering
// hotkeys, reading user stores, or changing the clipboard. This is a layout check,
// not an end-to-end test of the application's code-behind.
internal static class LayoutChecks
{
    public static void Run(string sourceRoot, string output)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Export(Path.GetFullPath(sourceRoot), Path.GetFullPath(output)); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("UI layout export failed", failure);
    }

    private static void Export(string sourceRoot, string output)
    {
        Directory.CreateDirectory(output);
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var doc = XDocument.Load(Path.Combine(sourceRoot, "MainWindow.xaml"));
        var root = doc.Root!;
        root.Name = ui + "UserControl";
        root.Element(ui + "Window.Resources")!.Name = ui + "UserControl.Resources";
        foreach (var name in new[] { "Title", "Icon", "WindowStyle", "ResizeMode", "AllowsTransparency", "Topmost", "ShowInTaskbar" })
            root.Attribute(name)?.Remove();
        root.Attribute(x + "Class")!.Remove();
        var eventNames = new HashSet<string> { "Click", "MouseEnter", "MouseLeave", "MouseLeftButtonDown", "PreviewMouseLeftButtonDown", "PreviewMouseMove", "Drop", "DragOver", "TextChanged", "SelectionChanged", "ValueChanged" };
        foreach (var attr in root.DescendantsAndSelf().Attributes().Where(a => eventNames.Contains(a.Name.LocalName)).ToList()) attr.Remove();
        var todo = root.Descendants().Single(e => e.Name.LocalName == "TodoListView");
        todo.Name = ui + "ContentControl";
        root.Descendants().Single(e => e.Name.LocalName == "CommandListView").Name = ui + "ContentControl";
        root.Descendants().Single(e => e.Name.LocalName == "DocumentListView").Name = ui + "ContentControl";
        var view = (UserControl)XamlReader.Parse(doc.ToString());
        T Find<T>(string name) => (T)view.FindName(name);
        Find<ItemsControl>("PhraseList").ItemsSource = new[] {
            new Phrase { Title = "进度同步", Group = "工作", Text = "已收到，我会整理进度后回复你。", IsPinned = true },
            new Phrase { Title = "会议确认", Group = "协作", Text = "时间已确认，我会提前准备好相关材料。" },
            new Phrase { Title = "交付说明", Group = "工作", Text = "文件已整理，请查看附件中的最新版本。" },
            new Phrase { Title = "周报模板", Group = "常用", Text = "本周完成 / 下周计划 / 需要协助" }
        };
        Find<ItemsControl>("FolderList").ItemsSource = new[] {
            new FolderShortcut { Name = "进行中的项目", Path = @"D:\Projects", IsPinned = true },
            new FolderShortcut { Name = "视频素材", Path = @"D:\Media\素材" },
            new FolderShortcut { Name = "交付文件", Path = @"D:\Work\交付" }
        };
        Find<ItemsControl>("AppList").ItemsSource = new[] {
            new AppShortcut { Name = "代码编辑器", Category = AppCategories.Development, Path = @"D:\Apps\Editor.exe", IsPinned = true },
            new AppShortcut { Name = "笔记工具", Category = AppCategories.Other, Path = @"D:\Apps\Notes.exe" },
            new AppShortcut { Name = "图片查看器", Category = AppCategories.Other, Path = @"D:\Apps\Viewer.exe" }
        };
        Find<ComboBox>("AppCategoryFilter").ItemsSource = new[] { "全部分类" };
        Find<ComboBox>("AppCategoryFilter").SelectedIndex = 0;
        Find<ComboBox>("ModeBox").SelectedIndex = 1;
        Find<Slider>("OpacitySlider").Value = 70;
        Find<TextBlock>("OpacityValueText").Text = "70%";
        Find<TextBlock>("MemoryText").Text = "内存 --";
        Find<TextBlock>("StatusText").Text = "Ctrl+Alt+Space 显隐 · Ctrl+Alt+T 穿透";
        var todoPath = Path.Combine(output, "sample-todos.json");
        var store = new TodoStore(todoPath);
        store.Save(new[] {
            new TodoItem { Text = "整理今天的项目进度", Progress = 35 },
            new TodoItem { Text = "准备明天的会议材料", Progress = 70 },
            new TodoItem { Text = "发送最新交付文件", IsCompleted = true }
        });
        Find<ContentControl>("TodoView").Content = new TodoListView(store);
        var documentStore = new DocumentStore(Path.Combine(output, "sample-documents.json"));
        documentStore.Save([
            new DocumentShortcut { Name = "项目使用说明", Path = @"D:\Projects\Sample\README.md", IsPinned = true },
            new DocumentShortcut { Name = "学习笔记", Path = @"D:\Knowledge\学习笔记.md" },
            new DocumentShortcut { Name = "设计方案", Path = @"D:\Projects\Sample\docs\设计方案.md" }
        ]);
        Find<ContentControl>("DocumentView").Content = new DocumentListView(documentStore, new DocumentLauncher(null, null));
        var tabs = Find<TabControl>("MainTabs");
        var fileTabs = Find<TabControl>("FileTabs");
        var commandStore = new CommandStore(Path.Combine(output, "sample-commands.json"));
        commandStore.Save([
            new CommandShortcut { Name = "查看目录", Text = "Get-ChildItem", WorkingDirectory = @"D:\Projects\Sample" },
            new CommandShortcut { Name = "启动开发服务", Text = "npm run dev", WorkingDirectory = @"D:\Projects\Sample" },
            new CommandShortcut { Name = "检查 Git 状态", Text = "git status", WorkingDirectory = @"D:\Projects\Sample", Shell = CommandShell.Cmd }
        ]);
        Find<ContentControl>("CommandView").Content = new CommandListView(commandStore);
        if (tabs.Items.Count != 5) throw new InvalidOperationException("Expected exactly five visible tabs");
        if (fileTabs.Items.Count != 2 || fileTabs.SelectedIndex != 0)
            throw new InvalidOperationException("Expected two file tabs with folders selected by default");
        foreach (var width in new[] { 370, 430 })
        foreach (var dpi in new[] { 96, 144, 192 })
        for (var tab = 0; tab < 6; tab++)
        {
            view.Width = width;
            view.Height = 650;
            tabs.SelectedIndex = tab == 5 ? 1 : tab;
            fileTabs.SelectedIndex = tab == 5 ? 0 : 1;
            view.Measure(new Size(width, 650));
            view.Arrange(new Rect(0, 0, width, 650));
            view.UpdateLayout();
            if (tab is 1 or 5)
            {
                foreach (TabItem header in fileTabs.Items)
                {
                    var bounds = header.TransformToAncestor(view).TransformBounds(new Rect(header.RenderSize));
                    if (bounds.Left < 0 || bounds.Right > width || bounds.Height <= 0)
                        throw new InvalidOperationException($"File tab {header.Header} is clipped at width {width}");
                }
                var selectedIndex = tab == 5 ? 1 : 0;
                var target = (TabItem)fileTabs.Items[selectedIndex];
                var peer = new TabItemAutomationPeer(target, new TabControlAutomationPeer(fileTabs));
                ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
                view.UpdateLayout();
                if (fileTabs.SelectedIndex != selectedIndex || !ReferenceEquals(fileTabs.SelectedContent, target.Content)
                    || view.IsAncestorOf(Find<Grid>("FolderPanel")) != (selectedIndex == 0)
                    || view.IsAncestorOf(Find<ContentControl>("DocumentView")) != (selectedIndex == 1))
                    throw new InvalidOperationException("One tab selection must switch to the matching file list");
                tabs.SelectedIndex = 2;
                tabs.SelectedIndex = 1;
                view.UpdateLayout();
                if (fileTabs.SelectedIndex != selectedIndex)
                    throw new InvalidOperationException("Returning to files must retain the selected file tab");
            }
            foreach (var name in new[] { "ModeBox", "OpacitySlider", "OpacityValueText" })
            {
                var element = Find<FrameworkElement>(name);
                var bounds = element.TransformToAncestor(view).TransformBounds(new Rect(element.RenderSize));
                if (bounds.Left < 0 || bounds.Right > width || bounds.Height <= 0)
                    throw new InvalidOperationException($"{name} is clipped at width {width}");
            }
            if (tab == 5)
            {
                var documents = (DocumentListView)Find<ContentControl>("DocumentView").Content;
                foreach (var name in new[] { "SearchBox", "AddButton", "DocumentList" })
                {
                    var element = (FrameworkElement)documents.FindName(name);
                    var bounds = element.TransformToAncestor(view).TransformBounds(new Rect(element.RenderSize));
                    if (bounds.Left < 0 || bounds.Right > width || bounds.Height <= 0)
                        throw new InvalidOperationException($"Markdown {name} is clipped at width {width}");
                }
            }
            var bitmap = new RenderTargetBitmap(width * dpi / 96, 650 * dpi / 96, dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"ui-{width}-{dpi}-{tab}.png"));
            encoder.Save(file);
        }
        File.WriteAllText(Path.Combine(output, "layout-check.txt"), "PASS: five main tabs and two file tabs; automation selection in both directions and selection retained on return; 370/430 logical pixels; 96/144/192 DPI rendering; toolbar, file tabs and Markdown bounds. Generic sample data. No desktop input. Physical mouse clicks and other code-behind actions not checked here.");
        Console.WriteLine("UI layout checks passed; 36 images exported.");
    }
}
