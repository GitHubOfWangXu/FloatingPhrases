using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FloatingPhrases;

internal static class TransferChecks
{
    public static void Run(string directory)
    {
        CheckAsync(directory).GetAwaiter().GetResult();
        CheckStopDuringUploadAsync(directory).GetAwaiter().GetResult();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await CheckViewAsync(directory); }
                catch (Exception ex) { failure = ex; }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal); }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("手机互传控件事件回归失败", failure);
    }

    private static async Task CheckViewAsync(string directory)
    {
        using var view = new TransferView(Path.Combine(directory, "view-transfer-received")) { Width = 338, Height = 455 };
        T Named<T>(string name) => (T)view.FindName(name);
        void Layout()
        {
            view.Measure(new Size(338, 455));
            view.Arrange(new Rect(0, 0, 338, 455));
            view.UpdateLayout();
        }
        void Toggle() => Named<Button>("ToggleButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Named<ComboBox>("AddressBox").ItemsSource = new[] { new TransferAddress(IPAddress.Loopback, "自检网络") };
        Named<ComboBox>("AddressBox").SelectedIndex = 0;
        Require(Named<FrameworkElement>("SessionPanel").Visibility == Visibility.Collapsed && Named<TextBox>("LinkBox").Text.Length == 0,
            "创建控件不得自动开启互传");
        Toggle();
        await Until(() => Named<TextBox>("LinkBox").Text.Length > 0 && Named<Button>("ToggleButton").IsEnabled, "开启互传按钮");
        var firstUrl = Named<TextBox>("LinkBox").Text;
        Require(Named<Image>("QrImage").Source is not null && Named<FrameworkElement>("SessionPanel").Visibility == Visibility.Visible,
            "真实开启按钮事件应显示二维码和链接");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        Require((await client.GetAsync(firstUrl)).IsSuccessStatusCode, "控件展示的链接必须真实可访问");
        Toggle();
        await Until(() => Named<TextBox>("LinkBox").Text.Length == 0 && Named<Button>("ToggleButton").IsEnabled, "关闭互传按钮");
        Require(Named<Image>("QrImage").Source is null && Named<FrameworkElement>("SessionPanel").Visibility == Visibility.Collapsed,
            "关闭后应清空二维码和链接");
        await Unavailable(client, firstUrl);
        var fixture = Path.Combine(directory, "拖入示例.txt");
        await File.WriteAllTextAsync(fixture, "通用控件事件样例");
        var dropped = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, [new DataObject(DataFormats.FileDrop, new[] { fixture }), DragDropKeyStates.None, DragDropEffects.Copy,
                view, new Point(10, 10)], null)!;
        dropped.RoutedEvent = System.Windows.DragDrop.DropEvent;
        view.RaiseEvent(dropped);
        await Until(() => Named<TextBox>("LinkBox").Text.Length > 0 && Named<ItemsControl>("SharedFilesList").Items.Count == 1
            && Named<Button>("AddFilesButton").IsEnabled, "拖入文件自动开启");
        var secondUrl = Named<TextBox>("LinkBox").Text;
        Require(new Uri(secondUrl).AbsolutePath != new Uri(firstUrl).AbsolutePath, "拖入文件重新开启应生成新令牌");
        Require((await client.GetFromJsonAsync<TransferFile[]>(secondUrl + "files")) is { Length: 1 }, "拖放应更新手机共享列表");
        Layout();
        var remove = Descendants(Named<ItemsControl>("SharedFilesList")).OfType<Button>().Single();
        remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Named<ItemsControl>("SharedFilesList").Items.Count == 0, "移除共享文件");
        Require(File.Exists(fixture) && (await client.GetFromJsonAsync<TransferFile[]>(secondUrl + "files")) is { Length: 0 },
            "真实移除事件应停止共享并保留原件");
        Toggle();
        await Until(() => Named<TextBox>("LinkBox").Text.Length == 0 && Named<Button>("ToggleButton").IsEnabled, "结束控件会话");
        Console.WriteLine("互传控件事件通过：开关、二维码状态、拖放自动开启、移除与链接更新。");
    }

    private static async Task Until(Func<bool> condition, string operation)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition()) return;
            await Task.Delay(50);
        }
        throw new InvalidOperationException(operation + "未在 10 秒内完成");
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

    private static async Task CheckAsync(string directory)
    {
        var sources = Path.Combine(directory, "transfer-source");
        var received = Path.Combine(directory, "transfer-received");
        Directory.CreateDirectory(sources);
        var selected = Path.Combine(sources, "中文 & 文件.txt");
        var privateFile = Path.Combine(sources, "未选中文件.txt");
        var bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("传输内容 & <原样保留>\r\n", 1024)));
        await File.WriteAllBytesAsync(selected, bytes);
        await File.WriteAllTextAsync(privateFile, "不能通过互传链接访问");
        await using var server = new LanTransferServer();
        await server.StartAsync(IPAddress.Loopback, received);
        server.AddFiles([selected, selected]);
        Require(server.Files.Count == 1, "重复添加同一文件应只展示一次");
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var url = server.Url;
        var baseUri = new Uri(url);
        Require(baseUri.Host == "127.0.0.1" && baseUri.AbsolutePath.Length > 20,
            "测试必须只绑定 loopback，并为会话使用随机链接");
        var page = await client.GetStringAsync(url);
        Require(page.Contains("upload") && page.Contains("download"), "手机页面应包含上传和下载入口");
        var files = await client.GetFromJsonAsync<TransferFile[]>(url + "files");
        Require(files is { Length: 1 } && files[0].Name == Path.GetFileName(selected) && files[0].Size == bytes.Length,
            "文件列表应只暴露显式选中文件的名称和大小");
        Require(!JsonSerializer.Serialize(files).Contains(sources, StringComparison.OrdinalIgnoreCase), "文件列表不能暴露电脑源路径");
        Require((await client.GetByteArrayAsync(url + "download/" + files![0].Id)).SequenceEqual(bytes),
            "下载后字节必须与原文件完全相同");
        await Rejected(client, new HttpRequestMessage(HttpMethod.Get, baseUri.GetLeftPart(UriPartial.Authority) + "/"), "缺令牌");
        await Rejected(client, new HttpRequestMessage(HttpMethod.Get, baseUri.GetLeftPart(UriPartial.Authority) + "/wrong-token/files"), "错误令牌");
        await Rejected(client, new HttpRequestMessage(HttpMethod.Get, url + "download/" + Uri.EscapeDataString(privateFile)), "未选中文件");
        var hostRequest = new HttpRequestMessage(HttpMethod.Get, url + "files");
        hostRequest.Headers.Host = "untrusted.example:" + baseUri.Port;
        await Rejected(client, hostRequest, "伪造 Host");
        var crossOrigin = Upload(url, "跨域.txt", bytes);
        crossOrigin.Headers.Add("Origin", "https://untrusted.example");
        await Rejected(client, crossOrigin, "跨 Origin 上传");
        var crossOriginDownload = new HttpRequestMessage(HttpMethod.Get, url + "download/" + files[0].Id);
        crossOriginDownload.Headers.Add("Origin", "https://untrusted.example");
        await Rejected(client, crossOriginDownload, "跨 Origin 下载");
        var missingMarker = Upload(url, "无标记.txt", bytes);
        missingMarker.Headers.Remove("X-Transfer-Upload");
        await Rejected(client, missingMarker, "缺上传专用标头");
        foreach (var name in new[] { "../逃逸.txt", "..\\逃逸.txt", "C:\\逃逸.txt", "NUL.txt", "末尾点." })
            await Rejected(client, Upload(url, name, bytes), "非法文件名 " + name);
        using (var oversized = new TcpClient())
        {
            await oversized.ConnectAsync(IPAddress.Loopback, baseUri.Port);
            var header = $"POST {baseUri.AbsolutePath}upload HTTP/1.1\r\nHost: {baseUri.Authority}\r\nX-Transfer-Upload: 1\r\nX-File-Name: oversized.bin\r\nContent-Length: {LanTransferServer.MaxFileSize + 1}\r\nConnection: close\r\n\r\n";
            await oversized.GetStream().WriteAsync(Encoding.ASCII.GetBytes(header));
            using var reader = new StreamReader(oversized.GetStream());
            var status = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Require(status?.Contains(" 413 ") == true, "超限请求应在接收大文件之前拒绝");
        }
        Require(!Directory.EnumerateFiles(received).Any(), "被拒绝的上传不得产生文件");

        var first = await Receive(client, url, "手机文件 中文.txt", bytes);
        var secondBytes = Encoding.UTF8.GetBytes("同名第二份内容");
        var second = await Receive(client, url, "手机文件 中文.txt", secondBytes);
        Require(first != second && Path.GetFileName(first) == first && Path.GetFileName(second) == second,
            "中文同名上传应返回两个不同且安全的文件名");
        Require((await File.ReadAllBytesAsync(Path.Combine(received, first))).SequenceEqual(bytes), "同名上传不能覆盖先前文件");
        Require((await File.ReadAllBytesAsync(Path.Combine(received, second))).SequenceEqual(secondBytes), "上传落盘字节必须保持不变");
        Require(server.Files.Count == 1, "收到的文件不能自动变为可下载的电脑文件");

        using (var socket = new TcpClient())
        {
            await socket.ConnectAsync(IPAddress.Loopback, baseUri.Port);
            var header = $"POST {baseUri.AbsolutePath}upload HTTP/1.1\r\nHost: {baseUri.Authority}\r\nX-Transfer-Upload: 1\r\nX-File-Name: interrupted.txt\r\nContent-Length: 100000\r\nConnection: close\r\n\r\nincomplete";
            await socket.GetStream().WriteAsync(Encoding.ASCII.GetBytes(header));
            for (var attempt = 0; attempt < 40 && Directory.EnumerateFiles(received).Count() == 2; attempt++)
                await Task.Delay(50);
            Require(Directory.EnumerateFiles(received).Count() == 3, "中断检查必须先观察到正在写入的临时文件");
        }
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(50);
            if (Directory.EnumerateFiles(received).Count() == 2) break;
        }
        Require(Directory.EnumerateFiles(received).Select(Path.GetFileName).Order().SequenceEqual(new[] { first, second }.Order()),
            "中断上传应清除临时文件，且不得留下成功文件");
        server.RemoveFile(files[0].Id);
        await Rejected(client, new HttpRequestMessage(HttpMethod.Get, url + "download/" + files[0].Id), "移除后的文件");
        Require(File.Exists(selected), "移除共享项不能删除原件");
        await server.StopAsync();
        await Unavailable(client, url);
        await server.StartAsync(IPAddress.Loopback, received);
        Require(new Uri(server.Url).AbsolutePath != baseUri.AbsolutePath, "重新开启必须更换会话令牌");
        await Rejected(client, new HttpRequestMessage(HttpMethod.Get, new Uri(server.Url).GetLeftPart(UriPartial.Authority) + baseUri.AbsolutePath + "files"),
            "重新开启后的旧令牌");
        await server.StopAsync();
        Require(Directory.EnumerateFiles(received).Count() == 2, "停止服务不得删除收到的完整文件");
        Console.WriteLine("局域网互传自检通过：权限边界、双向字节一致、同名保留、中断清理与会话关闭。");
    }

    private static async Task CheckStopDuringUploadAsync(string directory)
    {
        var received = Path.Combine(directory, "transfer-stop-received");
        await using var server = new LanTransferServer();
        await server.StartAsync(IPAddress.Loopback, received);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var completedBytes = Encoding.UTF8.GetBytes("关闭互传时必须保留的完整文件");
        var completedName = await Receive(client, server.Url, "完整文件.txt", completedBytes);
        var emptyName = await Receive(client, server.Url, "空文件.txt", []);
        Require(new FileInfo(Path.Combine(received, emptyName)).Length == 0, "零字节文件必须成功上传并保留");
        var oldUri = new Uri(server.Url);
        using var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, oldUri.Port);
        var header = $"POST {oldUri.AbsolutePath}upload HTTP/1.1\r\nHost: {oldUri.Authority}\r\nX-Transfer-Upload: 1\r\nX-File-Name: stopped.txt\r\nContent-Length: 100000\r\nConnection: close\r\n\r\nincomplete";
        await socket.GetStream().WriteAsync(Encoding.ASCII.GetBytes(header));
        await Until(() => Directory.EnumerateFiles(received, ".upload-*.partial").Any(), "关闭测试等待上传临时文件");
        await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Require(Directory.EnumerateFiles(received).Select(Path.GetFileName).Order()
            .SequenceEqual(new[] { completedName, emptyName }.Order()),
            "客户端保持未完成上传时停止服务，必须清理临时文件且不得留下成功文件");
        Require((await File.ReadAllBytesAsync(Path.Combine(received, completedName))).SequenceEqual(completedBytes)
            && new FileInfo(Path.Combine(received, emptyName)).Length == 0, "停止未完成上传不能损坏已收到的完整文件或空文件");
        await Unavailable(client, oldUri.AbsoluteUri);
        await server.StartAsync(IPAddress.Loopback, received);
        Require(new Uri(server.Url).AbsolutePath != oldUri.AbsolutePath, "停止未完成上传后重新开启必须更换令牌");
        var restarted = await Receive(client, server.Url, "重启后.txt", completedBytes);
        Require((await File.ReadAllBytesAsync(Path.Combine(received, restarted))).SequenceEqual(completedBytes),
            "停止未完成上传后重新开启，必须能够继续接收文件");
        await server.StopAsync();
        Console.WriteLine("互传生命周期协议通过：零字节上传、未完成上传时关闭并清理、完整文件保留、重启继续接收。");
    }

    private static HttpRequestMessage Upload(string url, string name, byte[] bytes)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url + "upload") { Content = new ByteArrayContent(bytes) };
        request.Headers.Add("X-Transfer-Upload", "1");
        request.Headers.Add("X-File-Name", Uri.EscapeDataString(name));
        return request;
    }

    private static async Task<string> Receive(HttpClient client, string url, string name, byte[] bytes)
    {
        using var request = Upload(url, name, bytes);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Require(json.RootElement.GetProperty("size").GetInt64() == bytes.Length, "成功响应必须报告实际落盘大小");
        return json.RootElement.GetProperty("name").GetString()!;
    }

    private static async Task Rejected(HttpClient client, HttpRequestMessage request, string context)
    {
        using (request)
        using (var response = await client.SendAsync(request))
            Require((int)response.StatusCode is >= 400 and < 500, context + "必须拒绝且不能返回文件内容");
    }

    private static async Task Unavailable(HttpClient client, string url)
    {
        try
        {
            using var response = await client.GetAsync(url);
            Require(!response.IsSuccessStatusCode, "关闭后旧链接不得继续提供页面");
        }
        catch (HttpRequestException) { }
    }

    public static void RunBrowser(string directory) => BrowserAsync(Path.GetFullPath(directory)).GetAwaiter().GetResult();

    private static async Task BrowserAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var fixture = Path.Combine(directory, "电脑示例.txt");
        if (File.Exists(fixture)) throw new InvalidOperationException("请选择没有演示样例文件的独立目录，避免覆盖现有文件。");
        await File.WriteAllTextAsync(fixture, "电脑到手机：通用互传示例。\n");
        await using var server = new LanTransferServer();
        await server.StartAsync(IPAddress.Loopback, Path.Combine(directory, "received"));
        server.AddFiles([fixture]);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; done.TrySetResult(); };
        Console.CancelKeyPress += handler;
        try
        {
            Console.WriteLine(server.Url);
            Console.WriteLine("仅 loopback 的浏览器验收服务；按 Ctrl+C 停止。");
            await done.Task;
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
