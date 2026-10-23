using System.ComponentModel;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FloatingPhrases;

public partial class ProcessMemoryWindow : Window
{
    private const string ReadFailedMessage = "无法读取进程列表，请稍后刷新重试。";
    private readonly Func<ProcessMemorySnapshot> _read;
    private readonly Func<string, bool> _confirm;
    private readonly Func<ProcessMemoryInfo, string> _end;
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<ProcessMemoryInfo> _items = [];
    private int _skipped;
    private bool _refreshing;
    private bool _ending;
    private bool _closed;

    public ProcessMemoryWindow() : this(ProcessMemory.Read, null, ProcessMemory.End) { }

    public ProcessMemoryWindow(Func<ProcessMemorySnapshot> read, Func<string, bool>? confirm, Func<ProcessMemoryInfo, string> end)
    {
        _read = read;
        _end = end;
        _confirm = confirm ?? (message => System.Windows.MessageBox.Show(this, message, "确认结束进程",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible) { _timer.Start(); await RefreshAsync(); }
            else _timer.Stop();
        };
        Closed += (_, _) => { _closed = true; _timer.Stop(); };
    }

    public void ShowSnapshot(ProcessMemorySnapshot snapshot)
    {
        if (FeedbackText.Text == ReadFailedMessage) FeedbackText.Text = "选择一个进程查看控制选项。";
        _items = snapshot.Processes;
        _skipped = snapshot.Skipped;
        MemorySummary.Text = snapshot.Memory is { } memory
            ? $"内存 {memory.UsedPercent:0}% · 已用 {memory.UsedBytes / 1073741824.0:0.0} GiB · 可用 {memory.AvailableBytes / 1073741824.0:0.0} GiB"
            : "暂时无法读取系统内存";
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (ProcessList is null) return;
        var selected = ProcessList.SelectedItem as ProcessMemoryInfo;
        var query = SearchBox.Text.Trim();
        var visible = _items.Where(item => item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || item.Id.ToString().Contains(query, StringComparison.Ordinal))
            .OrderByDescending(item => item.WorkingSetBytes).ThenBy(item => item.Id).ToArray();
        ProcessList.ItemsSource = visible;
        ProcessList.SelectedItem = selected is null ? null
            : visible.FirstOrDefault(item => item.Id == selected.Id && item.StartedFileTime == selected.StartedFileTime);
        CountText.Text = $"显示 {visible.Length} / {_items.Count} 个进程" + (_skipped > 0 ? $"；{_skipped} 个已退出或无法读取" : "");
        UpdateSelection();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || _ending || _closed) return;
        _refreshing = true;
        RefreshButton.IsEnabled = false;
        try
        {
            var snapshot = await Task.Run(_read);
            if (!_closed && !_ending) ShowSnapshot(snapshot);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException or SecurityException)
        {
            if (!_closed)
            {
                _items = [];
                ApplyFilter();
                MemorySummary.Text = "读取失败，当前没有有效的内存快照";
                FeedbackText.Text = ReadFailedMessage;
            }
        }
        finally { _refreshing = false; if (!_closed) RefreshButton.IsEnabled = true; }
    }

    private void UpdateSelection()
    {
        if (EndButton is null) return;
        var selected = ProcessList.SelectedItem as ProcessMemoryInfo;
        EndButton.IsEnabled = !_ending && selected is { CanEnd: true };
        EndButton.ToolTip = selected?.EndBlockedReason ?? "结束前需确认；未保存的内容可能丢失。";
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => UpdateSelection();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void End_Click(object sender, RoutedEventArgs e)
    {
        if (_ending || ProcessList.SelectedItem is not ProcessMemoryInfo selected) return;
        if (selected.EndBlockedReason is { } blocked) { FeedbackText.Text = blocked; return; }
        _ending = true;
        UpdateSelection();
        try
        {
            if (!_confirm($"确定强制结束 {selected.Name}（PID {selected.Id}）吗？\n\n未保存的内容可能丢失，相关任务或连接会中断。只结束这个进程，不结束其子进程。\n\n建议先在程序内保存并正常退出。")) return;
            FeedbackText.Text = await Task.Run(() => _end(selected));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException or SecurityException)
        {
            FeedbackText.Text = $"未能结束所选进程：{ex.Message}";
        }
        finally
        {
            _ending = false;
            UpdateSelection();
        }
        if (!_closed) await RefreshAsync();
    }
}
