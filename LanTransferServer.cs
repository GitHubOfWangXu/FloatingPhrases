using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FloatingPhrases;

public record TransferFile(string Id, string Name, long Size);
public record TransferAddress(IPAddress Address, string Name)
{
    public override string ToString() => $"{Name} · {Address}";
}

public sealed class LanTransferServer : IAsyncDisposable
{
    public const long MaxFileSize = 2L * 1024 * 1024 * 1024;
    private readonly object _filesLock = new();
    private readonly Dictionary<string, string> _files = new();
    private readonly SemaphoreSlim _upload = new(1);
    private WebApplication? _app;
    private CancellationTokenSource? _session;
    public string Url { get; private set; } = "";
    public string ReceiveDirectory { get; private set; } = "";
    public event Action<string>? StatusChanged;
    public event Action? FilesChanged;

    public IReadOnlyList<TransferFile> Files
    {
        get
        {
            lock (_filesLock)
                return _files.Select(pair =>
                {
                    var file = new FileInfo(pair.Value);
                    return new TransferFile(pair.Key, file.Name, file.Exists ? file.Length : 0);
                }).ToArray();
        }
    }

    public static IReadOnlyList<TransferAddress> GetAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
            && adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses
            .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivateAddress(ip.Address))
            .Select(ip => new TransferAddress(ip.Address, adapter.Name)))
        .DistinctBy(ip => ip.Address).ToArray();

    private static bool IsPrivateAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        var selected = paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var path in selected)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("请选择真实存在的文件。", path);
            if (new FileInfo(path).Length > MaxFileSize) throw new IOException("单个文件不能超过 2 GiB。");
        }
        lock (_filesLock)
            foreach (var path in selected)
                if (!_files.Values.Contains(path, StringComparer.OrdinalIgnoreCase)) _files.Add(Guid.NewGuid().ToString("N"), path);
        FilesChanged?.Invoke();
    }

    public void RemoveFile(string id)
    {
        lock (_filesLock) _files.Remove(id);
        FilesChanged?.Invoke();
    }

    public async Task StartAsync(IPAddress address, string receiveDirectory)
    {
        if (_app is not null) throw new InvalidOperationException("互传已经开启。");
        if (address.AddressFamily != AddressFamily.InterNetwork
            || (!IPAddress.IsLoopback(address) && !IsPrivateAddress(address)))
            throw new ArgumentException("请选择局域网 IPv4 地址。", nameof(address));
        ReceiveDirectory = Path.GetFullPath(receiveDirectory);
        Directory.CreateDirectory(ReceiveDirectory);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var prefix = "/" + token;
        using var pageStream = typeof(LanTransferServer).Assembly.GetManifestResourceStream("FloatingPhrases.TransferPage.html")!;
        using var reader = new StreamReader(pageStream);
        var page = await reader.ReadToEndAsync().ConfigureAwait(false);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders(); // 访问日志不能记录二维码内的令牌。
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(address, 0);
            options.Limits.MaxRequestBodySize = MaxFileSize;
            options.Limits.MaxConcurrentConnections = 16;
            options.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
        });
        var app = builder.Build();
        var session = new CancellationTokenSource();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            var sessionUrl = Url;
            if (session.IsCancellationRequested || !context.Request.Path.StartsWithSegments(prefix)
                || string.IsNullOrEmpty(sessionUrl) || !string.Equals(context.Request.Host.Value, new Uri(sessionUrl).Authority, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 404;
                return;
            }
            var origin = context.Request.Headers.Origin;
            if (origin.Count > 0 && (origin.Count != 1 || origin[0] != new Uri(sessionUrl).GetLeftPart(UriPartial.Authority)))
            {
                context.Response.StatusCode = 403;
                return;
            }
            try { await next(context).ConfigureAwait(false); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested || session.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusChanged?.Invoke("文件读写失败，请检查文件权限和剩余空间。");
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsJsonAsync(new { error = "文件读写失败，请在电脑上检查权限和剩余空间。" }).ConfigureAwait(false);
                }
                else context.Abort();
            }
        });
        app.MapGet(prefix + "/", () => Results.Content(page, "text/html; charset=utf-8"));
        app.MapGet(prefix + "/files", () => Results.Json(Files));
        app.MapGet(prefix + "/download/{id}", (string id, HttpContext context) =>
        {
            string? path;
            lock (_filesLock) _files.TryGetValue(id, out path);
            if (path is null || !File.Exists(path)) return Results.NotFound();
            context.Response.OnCompleted(() =>
            {
                if (!context.RequestAborted.IsCancellationRequested)
                    StatusChanged?.Invoke($"下载请求已完成：{Path.GetFileName(path)}");
                return Task.CompletedTask;
            });
            return Results.File(path, "application/octet-stream", Path.GetFileName(path), enableRangeProcessing: true);
        });
        app.MapPost(prefix + "/upload", (Func<HttpContext, Task<IResult>>)(context => ReceiveAsync(context, session.Token)));
        try
        {
            await app.StartAsync().ConfigureAwait(false);
            var endpoint = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            Url = endpoint.TrimEnd('/') + prefix + "/";
            _session = session;
            _app = app;
        }
        catch
        {
            session.Dispose();
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<IResult> ReceiveAsync(HttpContext context, CancellationToken sessionToken)
    {
        if (context.Request.Headers["X-Transfer-Upload"] != "1") return Results.BadRequest(new { error = "无效的上传请求。" });
        var names = context.Request.Headers["X-File-Name"];
        if (names.Count != 1) return Results.BadRequest(new { error = "缺少文件名。" });
        var name = Uri.UnescapeDataString(names[0]!);
        if (!IsSafeFileName(name)) return Results.BadRequest(new { error = "文件名无效，请重命名后重试。" });
        if (context.Request.ContentLength is > MaxFileSize) return Results.StatusCode(413);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, sessionToken);
        if (!await _upload.WaitAsync(0, cancellation.Token).ConfigureAwait(false))
            return Results.Json(new { error = "电脑正在接收其他文件，请稍后重试。" }, statusCode: 409);
        var temporary = Path.Combine(ReceiveDirectory, $".upload-{Guid.NewGuid():N}.partial");
        try
        {
            long size = 0;
            var timer = Stopwatch.StartNew();
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await context.Request.Body.ReadAsync(buffer, cancellation.Token).ConfigureAwait(false)) > 0)
                {
                    size += read;
                    if (size > MaxFileSize) return Results.StatusCode(413);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation.Token).ConfigureAwait(false);
                    if (timer.ElapsedMilliseconds > 250)
                    {
                        StatusChanged?.Invoke($"正在接收：{name} · {size / 1048576d:0.0} MiB");
                        timer.Restart();
                    }
                }
                await output.FlushAsync(cancellation.Token).ConfigureAwait(false);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            var savedName = name;
            for (var suffix = 1; ; suffix++)
            {
                try { File.Move(temporary, Path.Combine(ReceiveDirectory, savedName)); break; }
                catch (IOException) when (File.Exists(Path.Combine(ReceiveDirectory, savedName)))
                { savedName = $"{Path.GetFileNameWithoutExtension(name)} ({suffix}){Path.GetExtension(name)}"; }
            }
            StatusChanged?.Invoke($"已接收：{savedName} · {size / 1048576d:0.0} MiB");
            return Results.Json(new { name = savedName, size });
        }
        catch (BadHttpRequestException)
        {
            StatusChanged?.Invoke($"上传未完成：{name}，请在手机上重试。");
            return Results.BadRequest(new { error = "上传未完成，请重试。" });
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke($"已取消接收：{name}");
            throw;
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { StatusChanged?.Invoke("未完成上传的临时文件清理失败，请检查接收目录。"); }
            _upload.Release();
        }
    }

    private static bool IsSafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.EndsWith('.') || name.EndsWith(' ')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Any(char.IsControl)) return false;
        var stem = name.Split('.')[0].ToUpperInvariant();
        return stem is not "CON" and not "PRN" and not "AUX" and not "NUL"
            && !(stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9');
    }

    public async Task StopAsync()
    {
        var app = _app;
        if (app is null) return;
        _app = null;
        Url = "";
        _session!.Cancel();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await app.StopAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
            _session.Dispose();
            _session = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
