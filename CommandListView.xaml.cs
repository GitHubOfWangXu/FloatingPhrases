using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace FloatingPhrases;

public partial class CommandListView : System.Windows.Controls.UserControl
{
    private readonly CommandStore _store;
    private readonly Func<CommandShortcut?, bool, CommandShortcut?> _edit;
    private readonly Action<ProcessStartInfo> _launch;
    private readonly Action<string> _copy;
    private List<CommandShortcut> _commands = [];
    private bool _loaded;

    public CommandListView() : this(new CommandStore()) { }

    public CommandListView(CommandStore store,
        Func<CommandShortcut?, bool, CommandShortcut?>? edit = null,
        Action<ProcessStartInfo>? launch = null, Action<string>? copy = null)
    {
        _store = store;
        _edit = edit ?? ShowEditor;
        _launch = launch ?? (info => { using var process = Process.Start(info) ?? throw new InvalidOperationException("终端未启动，请重试。"); });
        _copy = copy ?? System.Windows.Clipboard.SetText;
        InitializeComponent();
        try { _commands = store.Load().ToList(); _loaded = true; Refresh(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddButton.IsEnabled = false;
            FeedbackText.Text = $"命令读取失败，请检查文件权限后重新打开：{ex.Message}";
        }
    }

    private CommandShortcut? ShowEditor(CommandShortcut? command, bool confirmExecution)
    {
        var editor = new CommandEditorWindow(command, confirmExecution) { Owner = Window.GetWindow(this) };
        return editor.ShowDialog() == true ? editor.Result : null;
    }

    public void FocusSearch() => SearchBox.Focus();
    public void AddCommand()
    {
        if (!_loaded) return;
        if (_edit(null, false) is { } command) Save(_commands.Append(command).ToList());
    }
    private void Add_Click(object sender, RoutedEventArgs e) => AddCommand();
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (_loaded) Refresh(); }

    private void Refresh()
    {
        var query = SearchBox.Text.Trim();
        var visible = _commands.Where(command => command.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || command.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || command.WorkingDirectory.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        CommandList.ItemsSource = visible;
        EmptyHint.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Text = _commands.Count == 0 ? "还没有命令，点击新增保存常用命令" : "没有匹配的命令";
    }

    private void Save(List<CommandShortcut> next)
    {
        try { _store.Save(next); _commands = next; Refresh(); FeedbackText.Text = "命令入口已保存"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { FeedbackText.Text = $"保存失败，改动未生效：{ex.Message}"; }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CommandShortcut command } && _edit(command, false) is { } edited)
            Save(_commands.Select(item => item.Id == command.Id ? edited : item).ToList());
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CommandShortcut command })
            Save(_commands.Where(item => item.Id != command.Id).ToList());
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CommandShortcut command }) return;
        try
        {
            var info = CommandLauncher.CreateStartInfo(command);
            if (_edit(command with { WorkingDirectory = info.WorkingDirectory }, true) is null) return;
            _launch(info);
            FeedbackText.Text = "已启动终端，请在终端查看执行结果";
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or ArgumentException or InvalidOperationException
                                  or NotSupportedException or UnauthorizedAccessException)
        { FeedbackText.Text = $"启动失败：{ex.Message}"; }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CommandShortcut command }) return;
        try { _copy(command.Text); FeedbackText.Text = "命令已复制"; }
        catch (ExternalException) { FeedbackText.Text = "剪贴板暂时被占用，请重试"; }
    }
}
