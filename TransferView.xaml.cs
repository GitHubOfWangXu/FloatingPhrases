using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using QRCoder;

namespace FloatingPhrases;

public partial class TransferView : System.Windows.Controls.UserControl, IDisposable
{
    private readonly LanTransferServer _server = new();
    private readonly string _receiveDirectory;
    private bool _busy;
    private bool _disposed;

    public TransferView() : this(null) { }
    public TransferView(string? receiveDirectory)
    {
        _receiveDirectory = receiveDirectory ?? Path.Combine(GetDownloadsDirectory(), "FloatingPhrases");
        InitializeComponent();
        _server.StatusChanged += Server_StatusChanged;
        _server.FilesChanged += Server_FilesChanged;
        RefreshAddresses();
    }

    private static string GetDownloadsDirectory()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        var result = SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var path);
        if (result != 0) return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        try { return Marshal.PtrToStringUni(path)!; }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);

    private void RefreshAddresses()
    {
        try
        {
            AddressBox.ItemsSource = LanTransferServer.GetAddresses();
            AddressBox.SelectedIndex = 0;
            if (AddressBox.SelectedItem is null) StatusText.Text = "未找到局域网，请连接 Wi-Fi、网线或手机热点后刷新。";
        }
        catch (NetworkInformationException) { StatusText.Text = "网络信息读取失败，请检查网络后刷新。"; }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAddresses();
    public void FocusAction() => ToggleButton.Focus();

    private async Task StartAsync()
    {
        if (AddressBox.SelectedItem is not TransferAddress address)
            throw new InvalidOperationException("请先连接局域网，然后点击刷新并选择网络。");
        await _server.StartAsync(address.Address, _receiveDirectory);
        try
        {
            var png = PngByteQRCodeHelper.GetQRCode(_server.Url, QRCodeGenerator.ECCLevel.M, 6);
            using var stream = new MemoryStream(png);
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            QrImage.Source = image;
            LinkBox.Text = _server.Url;
            SessionPanel.Visibility = Visibility.Visible;
            ToggleButton.Content = "关闭互传";
            AddressBox.IsEnabled = RefreshButton.IsEnabled = false;
            StatusText.Text = "互传已开启。手机扫码即可上传和下载；若打不开，请检查同一 Wi-Fi 和 Windows 防火墙。";
        }
        catch { await _server.StopAsync(); throw; }
    }

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            if (_server.Url.Length == 0) await StartAsync();
            else
            {
                await _server.StopAsync();
                ResetSession();
                StatusText.Text = "互传已关闭，旧链接已失效。再次开启后请重新扫码。";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Net.Sockets.SocketException)
        { ResetSession(); StatusText.Text = $"无法开启或关闭互传：{ex.Message}"; }
        finally { SetBusy(false); }
    }

    private void ResetSession()
    {
        QrImage.Source = null;
        LinkBox.Clear();
        SessionPanel.Visibility = Visibility.Collapsed;
        ToggleButton.Content = "开启互传";
        AddressBox.IsEnabled = RefreshButton.IsEnabled = true;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ToggleButton.IsEnabled = AddFilesButton.IsEnabled = !busy;
    }

    public void BrowseFiles()
    {
        if (_busy) return;
        var picker = new Microsoft.Win32.OpenFileDialog { Title = "选择分享给手机的文件", Multiselect = true };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) AddFiles(picker.FileNames);
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e) => BrowseFiles();
    private async void AddFiles(IEnumerable<string> paths)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            _server.AddFiles(paths);
            if (_server.Url.Length == 0) await StartAsync();
            else StatusText.Text = "分享列表已更新，手机网页会自动刷新。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Net.Sockets.SocketException)
        { StatusText.Text = $"无法分享：{ex.Message}"; }
        finally { SetBusy(false); }
    }

    private void Files_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = !_busy && e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Files_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files) AddFiles(files);
        e.Handled = true;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string id) _server.RemoveFile(id);
    }

    private void Server_StatusChanged(string status)
    {
        if (!_disposed && !Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(new Action(() => { if (!_disposed) StatusText.Text = status; }));
    }

    private void Server_FilesChanged()
    {
        if (!_disposed && !Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed) return;
                SharedFilesList.ItemsSource = _server.Files;
                EmptyHint.Visibility = _server.Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }));
    }

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        if (_server.Url.Length == 0) return;
        try { System.Windows.Clipboard.SetText(_server.Url); StatusText.Text = "临时链接已复制，请勿公开转发。"; }
        catch (ExternalException) { StatusText.Text = "剪贴板暂时不可用，请重试或手动复制链接。"; }
    }

    private void OpenReceive_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_receiveDirectory);
            Process.Start(new ProcessStartInfo(_receiveDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        { StatusText.Text = $"无法打开接收目录：{ex.Message}"; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _server.StatusChanged -= Server_StatusChanged;
        _server.FilesChanged -= Server_FilesChanged;
        _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
