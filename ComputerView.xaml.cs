using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FloatingPhrases;

public partial class ComputerView : System.Windows.Controls.UserControl
{
    private readonly DispatcherTimer _timer;
    private readonly Action<ProcessStartInfo> _launch;
    private readonly Action _showMemory;
    private SystemStatus? _reader;
    private bool _refreshing;

    public ComputerView() : this(null) { }

    public ComputerView(Action<ProcessStartInfo>? launch, Action? showMemory = null)
    {
        _launch = launch ?? (info => { using var process = Process.Start(info); });
        _showMemory = showMemory ?? (() => new ProcessMemoryWindow { Owner = Window.GetWindow(this) }.ShowDialog());
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => RefreshStatus();
        Loaded += (_, _) => UpdateMonitoring();
        Unloaded += (_, _) => StopMonitoring();
        IsVisibleChanged += (_, _) => UpdateMonitoring();
    }

    public void FocusAction() => TaskManagerButton.Focus();

    private void MemoryDetails_Click(object sender, RoutedEventArgs e) => _showMemory();

    private void UpdateMonitoring()
    {
        if (!IsLoaded || !IsVisible) { StopMonitoring(); return; }
        if (_reader is not null) return;
        _reader = new SystemStatus();
        CpuText.Text = "采样中…";
        CpuBar.Value = 0;
        NetworkList.ItemsSource = null;
        NetworkMessage.Text = "采样中…";
        _timer.Start();
        RefreshStatus();
    }

    private void StopMonitoring()
    {
        _timer.Stop();
        _reader = null;
    }

    private async void RefreshStatus()
    {
        if (_reader is not { } reader || _refreshing) return;
        _refreshing = true;
        try
        {
            var snapshot = await Task.Run(reader.Read);
            // A previous page session must not overwrite a newly started sample.
            if (ReferenceEquals(_reader, reader)) ShowStatus(snapshot);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (ReferenceEquals(_reader, reader))
            {
                ShowStatus(new(null, false, null, [], "暂时无法读取磁盘列表", [], "暂时无法读取网卡列表"));
            }
        }
        finally { _refreshing = false; }
    }

    public void ShowStatus(ComputerSnapshot snapshot)
    {
        CpuText.Text = snapshot.CpuPercent is { } cpu ? $"{cpu:0}%" : snapshot.CpuAvailable ? "采样中…" : "暂不可用";
        CpuBar.Value = snapshot.CpuPercent ?? 0;
        MemoryText.Text = snapshot.Memory is { } memory ? $"{memory.UsedPercent:0}%" : "--";
        MemoryBar.Value = snapshot.Memory?.UsedPercent ?? 0;
        MemoryDetails.Text = snapshot.Memory?.Details ?? "暂时无法读取系统内存";
        DiskList.ItemsSource = snapshot.Disks;
        DiskMessage.Text = snapshot.DiskMessage;
        NetworkList.ItemsSource = snapshot.Networks;
        NetworkMessage.Text = snapshot.NetworkMessage;
    }

    private void OpenTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: SystemTool tool } button) return;
        try
        {
            _launch(SystemTools.CreateStartInfo(tool));
            FeedbackText.Text = $"已请求打开{button.Content}，请在系统窗口中操作。";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            FeedbackText.Text = $"无法打开{button.Content}，请检查 Windows 是否支持或限制了该入口。";
        }
    }
}
