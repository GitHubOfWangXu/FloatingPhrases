using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FloatingPhrases;

public partial class TodoListView : System.Windows.Controls.UserControl
{
    private readonly TodoStore _store;
    private readonly Action<string> _copyText;
    private List<TodoItem> _items = [];
    private Guid? _editing;
    private TodoItem? _deleted;
    private int _deletedIndex;
    private bool _loaded;

    public TodoListView() : this(new TodoStore()) { }

    public TodoListView(TodoStore store, Action<string>? copyText = null)
    {
        _store = store;
        _copyText = copyText ?? System.Windows.Clipboard.SetText;
        InitializeComponent();
        try
        {
            _items = store.Load().ToList();
            _loaded = true;
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            InputBox.IsEnabled = SaveButton.IsEnabled = false;
            EmptyHint.Text = "待办读取失败，请检查文件访问权限后重新打开";
            FeedbackText.Text = ex.Message;
        }
    }

    public void FocusInput() => InputBox.Focus();

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (_loaded) Refresh();
    }

    private void Refresh()
    {
        var query = SearchBox.Text.Trim();
        var visible = _items.Where(item => item.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            && (FilterBox.SelectedIndex == 0 || item.IsCompleted == (FilterBox.SelectedIndex == 2)))
            .OrderBy(item => item.IsCompleted).ToList();
        TodoList.ItemsSource = visible;
        CopyButton.IsEnabled = visible.Count > 0;
        EmptyHint.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Text = _items.Count == 0 ? "还没有待办，先记下一件事吧" : "没有符合条件的待办";
        CountText.Text = $"未完成 {_items.Count(item => !item.IsCompleted)} · 已完成 {_items.Count(item => item.IsCompleted)}";
        UndoButton.Visibility = _deleted is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var items = TodoList.Items.Cast<TodoItem>().ToList();
        if (!_loaded || items.Count == 0) return;
        var text = string.Join(Environment.NewLine, items.Select(item =>
            $"- [{(item.IsCompleted ? "x" : " ")}] {item.Text.ReplaceLineEndings(Environment.NewLine + "  ")}"));
        try
        {
            _copyText(text);
            FeedbackText.Text = $"已复制 {items.Count} 条待办，可粘贴到工作日志";
        }
        catch (ExternalException)
        {
            FeedbackText.Text = "剪贴板暂时被占用，请重试";
        }
    }

    // Save before replacing the view's state: failed writes must not look successful.
    private bool Save(List<TodoItem> next, string message)
    {
        if (!_loaded) return false;
        try { _store.Save(next); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FeedbackText.Text = $"保存失败，改动未生效：{ex.Message}";
            Refresh();
            return false;
        }
        _items = next;
        FeedbackText.Text = message;
        Refresh();
        return true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var text = InputBox.Text.Trim();
        if (text.Length == 0) { FeedbackText.Text = "先输入待办内容"; FocusInput(); return; }
        var next = _items.ToList();
        if (_editing is { } id)
        {
            var index = next.FindIndex(item => item.Id == id);
            if (index < 0) return;
            next[index] = next[index] with { Text = text };
        }
        else next.Insert(0, new TodoItem { Text = text });
        if (Save(next, _editing is null ? "待办已添加" : "待办已更新")) ResetEditor();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TodoItem item }) return;
        _editing = item.Id;
        InputBox.Text = item.Text;
        SaveButton.Content = "保存";
        CancelButton.Visibility = Visibility.Visible;
        FocusInput();
        InputBox.SelectAll();
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TodoItem item }) return;
        Save(_items.Select(current => current.Id == item.Id ? current with { IsCompleted = !current.IsCompleted } : current).ToList(),
            item.IsCompleted ? "已恢复为未完成" : "待办已完成");
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TodoItem item }) return;
        var index = _items.FindIndex(current => current.Id == item.Id);
        if (index < 0 || !Save(_items.Where(current => current.Id != item.Id).ToList(), "待办已删除，可撤销")) return;
        _deleted = item;
        _deletedIndex = index;
        if (_editing == item.Id) ResetEditor();
        Refresh();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_deleted is null) return;
        var next = _items.ToList();
        next.Insert(Math.Min(_deletedIndex, next.Count), _deleted);
        if (Save(next, "已撤销删除")) { _deleted = null; Refresh(); }
    }

    private void ResetEditor()
    {
        _editing = null;
        InputBox.Clear();
        SaveButton.Content = "添加";
        CancelButton.Visibility = Visibility.Collapsed;
        FocusInput();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => ResetEditor();
    private void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Save_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Escape) { ResetEditor(); e.Handled = true; }
    }
}
