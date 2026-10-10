using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using DataFormats = System.Windows.DataFormats;

namespace FloatingPhrases;

public partial class DocumentListView : System.Windows.Controls.UserControl
{
    private readonly DocumentStore _store;
    private readonly DocumentLauncher _launcher;
    private readonly Action<ProcessStartInfo> _launch;
    private readonly Action<string> _copy;
    private readonly Func<string, bool> _confirm;
    private List<DocumentShortcut> _items = [];
    private DocumentShortcut? _editing;
    private DocumentShortcut? _dragged;
    private System.Windows.Point _dragStart;
    private bool _loaded;

    public DocumentListView() : this(new DocumentStore(), new DocumentLauncher()) { }

    public DocumentListView(DocumentStore store, DocumentLauncher launcher,
        Action<ProcessStartInfo>? launch = null, Action<string>? copy = null, Func<string, bool>? confirm = null)
    {
        _store = store;
        _launcher = launcher;
        _launch = launch ?? (info => Process.Start(info));
        _copy = copy ?? System.Windows.Forms.Clipboard.SetText;
        _confirm = confirm ?? (message => System.Windows.MessageBox.Show(Window.GetWindow(this), message,
            "Floating Phrases", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
        InitializeComponent();
        try
        {
            _items = store.Load().ToList();
            _loaded = true;
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddButton.IsEnabled = false;
            EmptyHint.Text = "文档入口读取失败，请检查权限后重新打开";
            FeedbackText.Text = ex.Message;
        }
    }

    public void FocusSearch() => SearchBox.Focus();

    public void BrowseFiles()
    {
        if (!_loaded) return;
        var dialog = new OpenFileDialog { Title = "添加 Markdown 快捷入口", Filter = "Markdown 文件|*.md;*.markdown", Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) AddPaths(dialog.FileNames);
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        if (!_loaded) return;
        try
        {
            var additions = paths.Select(ValidatePath).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !_items.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
                .Select(path => new DocumentShortcut { Name = Path.GetFileNameWithoutExtension(path), Path = path }).ToList();
            if (additions.Count == 0) { FeedbackText.Text = "这些文档已经添加"; return; }
            Persist(_items.Concat(additions).ToList(), $"已添加 {additions.Count} 个文档入口");
        }
        catch (Exception ex) when (IsFileError(ex)) { FeedbackText.Text = ex.Message; }
    }

    private static string ValidatePath(string path)
    {
        var fullPath = DocumentLauncher.NormalizePath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("该 Markdown 文件不存在，请重新选择。", fullPath);
        return fullPath;
    }

    private void Refresh()
    {
        var query = SearchBox.Text.Trim();
        var visible = _items.Where(item => item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || item.Path.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(item => item.IsPinned)
            .Select(item => new { Item = item, Title = item.Name + (item.IsPinned ? "  ●" : ""), Editor = _launcher.GetEditorName(item) }).ToList();
        DocumentList.ItemsSource = visible;
        EmptyHint.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Text = _items.Count == 0 ? "选择或拖入 .md 文件添加快捷入口" : "没有匹配的 Markdown";
    }

    private bool Persist(List<DocumentShortcut> items, string message)
    {
        try
        {
            _store.Save(items);
            _items = items;
            Refresh();
            FeedbackText.Text = message;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FeedbackText.Text = "保存失败：" + ex.Message;
            return false;
        }
    }

    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or ArgumentException or NotSupportedException or InvalidOperationException or System.ComponentModel.Win32Exception;

    private void Open(DocumentShortcut item, DocumentOpenMode? mode = null)
    {
        try
        {
            _launch(_launcher.CreateStartInfo(item, mode));
            FeedbackText.Text = "已请求打开：" + item.Name;
        }
        catch (Exception ex) when (IsFileError(ex)) { FeedbackText.Text = ex.Message; }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DocumentShortcut item) Open(item);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DocumentShortcut item) return;
        _editing = item;
        NameBox.Text = item.Name;
        PathBox.Text = item.Path;
        OpenModeBox.SelectedIndex = (int)item.OpenMode;
        PinnedBox.IsChecked = item.IsPinned;
        FeedbackText.Text = "只修改快捷入口；正文在编辑器中修改";
        EditorPanel.Visibility = Visibility.Visible;
        NameBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        try
        {
            var path = ValidatePath(PathBox.Text.Trim());
            var mode = (DocumentOpenMode)OpenModeBox.SelectedIndex;
            if (mode == DocumentOpenMode.Obsidian && !_launcher.CanOpenInObsidian(path))
                throw new InvalidOperationException("此入口仅支持已登记 Obsidian 知识库内的 .md 文件，请选择自动或 VS Code。");
            if (_items.Any(item => item.Id != _editing.Id && string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("这个文档已经有快捷入口。");
            var replacement = new DocumentShortcut { Id = _editing.Id, Path = path,
                Name = string.IsNullOrWhiteSpace(NameBox.Text) ? Path.GetFileNameWithoutExtension(path) : NameBox.Text.Trim(),
                IsPinned = PinnedBox.IsChecked == true, OpenMode = mode };
            var candidate = _items.Select(item => item.Id == _editing.Id ? replacement : item).ToList();
            if (Persist(candidate, "文档入口已更新")) Cancel_Click(sender, e);
        }
        catch (Exception ex) when (IsFileError(ex)) { FeedbackText.Text = ex.Message; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _editing = null;
        EditorPanel.Visibility = Visibility.Collapsed;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DocumentShortcut item
            && _confirm($"删除“{item.Name}”的快捷入口？不会删除原文件。"))
            Persist(_items.Where(existing => existing.Id != item.Id).ToList(), "已删除快捷入口，原文件保留");
    }

    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 Markdown 文件", Filter = "Markdown 文件|*.md;*.markdown" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) PathBox.Text = dialog.FileName;
    }

    private void Row_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentShortcut item, ContextMenu: { } menu })
        {
            ((MenuItem)menu.Items[1]).IsEnabled = _launcher.CanOpenInObsidian(item.Path);
            ((MenuItem)menu.Items[1]).ToolTip = "仅限已登记 Obsidian 知识库内的 .md 文件";
        }
    }

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem action || action.Parent is not ContextMenu menu
            || menu.PlacementTarget is not FrameworkElement { Tag: DocumentShortcut item }) return;
        if (Equals(action.Tag, "vscode")) { Open(item, DocumentOpenMode.VSCode); return; }
        if (Equals(action.Tag, "obsidian")) { Open(item, DocumentOpenMode.Obsidian); return; }
        try
        {
            if (Equals(action.Tag, "copy")) { _copy(item.Path); FeedbackText.Text = "文档路径已复制"; }
            else
            {
                var directory = Path.GetDirectoryName(item.Path);
                if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("所在文件夹已不存在。");
                _launch(new ProcessStartInfo(directory) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (IsFileError(ex) || ex is ExternalException) { FeedbackText.Text = ex.Message; }
    }

    private void Files_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        e.Effects = _loaded && _editing is null && paths is { Length: > 0 } && paths.All(path => DocumentLauncher.IsMarkdownPath(path) && File.Exists(path))
            ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Files_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (_editing is null && e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
        e.Handled = true;
    }

    private void Row_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragged = (sender as FrameworkElement)?.Tag as DocumentShortcut;
        _dragStart = e.GetPosition(this);
    }

    private void Row_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragged is null) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = _dragged;
        _dragged = null;
        System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, item, System.Windows.DragDropEffects.Move);
    }

    private void Row_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        var source = e.Data.GetData(typeof(DocumentShortcut)) as DocumentShortcut;
        var target = (sender as FrameworkElement)?.Tag as DocumentShortcut;
        e.Effects = source is not null && target is not null && source.Id != target.Id && source.IsPinned == target.IsPinned
            ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Row_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DocumentShortcut target } element
            && e.Data.GetData(typeof(DocumentShortcut)) is DocumentShortcut source && source.IsPinned == target.IsPinned)
        {
            var candidate = _items.ToList();
            if (ItemOrder.Move(candidate, source, target, e.GetPosition(element).Y > element.ActualHeight / 2))
                Persist(candidate, "文档顺序已保存");
        }
        else FeedbackText.Text = "置顶项和普通项请分别排序";
        e.Handled = true;
    }

    private void Add_Click(object sender, RoutedEventArgs e) => BrowseFiles();
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (_loaded) Refresh(); }
}
