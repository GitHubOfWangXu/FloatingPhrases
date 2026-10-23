using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace FloatingPhrases;

public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User)
{
    public double? UsageSince(CpuTimes previous)
    {
        if (Idle < previous.Idle || Kernel < previous.Kernel || User < previous.User) return null;
        // Windows includes idle time in kernel time.
        var total = (double)(Kernel - previous.Kernel) + (User - previous.User);
        var idle = Idle - previous.Idle;
        return total > 0 && idle <= total ? 100 * (total - idle) / total : null;
    }
}

public sealed record DiskStatus(string Name, long? TotalBytes, long? AvailableBytes)
{
    public double UsedPercent => TotalBytes is > 0 && AvailableBytes is { } available
        ? 100 * (1 - Math.Clamp((double)available / TotalBytes.Value, 0, 1)) : 0;
    public string Details => TotalBytes is > 0 && AvailableBytes is { } available
        ? $"可用 {available / 1073741824.0:0.0} / 共 {TotalBytes.Value / 1073741824.0:0.0} GiB"
        : "未就绪或暂时无法读取";
}

public sealed record NetworkStatus(string Name, double? ReceiveBytesPerSecond, double? SendBytesPerSecond,
    bool Available = true)
{
    public string Details => !Available ? "暂时无法读取"
        : $"↓ {FormatRate(ReceiveBytesPerSecond)}    ↑ {FormatRate(SendBytesPerSecond)}";

    public static string FormatRate(double? bytes) => bytes switch
    {
        null => "采样中…",
        >= 1048576 => $"{bytes / 1048576:0.0} MiB/s",
        >= 1024 => $"{bytes / 1024:0.0} KiB/s",
        _ => $"{bytes:0} B/s"
    };

    public static double? RateSince(long previous, long current, double seconds) =>
        previous >= 0 && current >= previous && double.IsFinite(seconds) && seconds > 0
            ? (current - previous) / seconds : null;
}

public sealed record ComputerSnapshot(double? CpuPercent, bool CpuAvailable, MemoryUsage? Memory,
    IReadOnlyList<DiskStatus> Disks, string DiskMessage,
    IReadOnlyList<NetworkStatus> Networks, string NetworkMessage);

public sealed class SystemStatus
{
    private CpuTimes? _previousCpu;
    private Dictionary<string, (long Received, long Sent, long Timestamp)> _previousNetwork = new();

    public ComputerSnapshot Read()
    {
        CpuTimes? cpu = GetSystemTimes(out var idle, out var kernel, out var user)
            ? new CpuTimes(idle, kernel, user) : null;
        var percent = cpu is { } current && _previousCpu is { } previous ? current.UsageSince(previous) : null;
        _previousCpu = cpu;
        var (disks, diskMessage) = ReadDisks();
        var (networks, networkMessage) = ReadNetworks();
        return new(percent, cpu is not null, SystemMemory.Read(), disks, diskMessage, networks, networkMessage);
    }

    private static (IReadOnlyList<DiskStatus>, string) ReadDisks()
    {
        var disks = new List<DiskStatus>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed) continue;
                    disks.Add(drive.IsReady
                        ? new(drive.Name, drive.TotalSize, drive.AvailableFreeSpace)
                        : new(drive.Name, null, null));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    disks.Add(new(drive.Name, null, null));
                }
            }
            return (disks, disks.Count == 0 ? "未找到本地固定磁盘" : "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (disks, "暂时无法读取磁盘列表");
        }
    }

    private (IReadOnlyList<NetworkStatus>, string) ReadNetworks()
    {
        var networks = new List<NetworkStatus>();
        var samples = new Dictionary<string, (long Received, long Sent, long Timestamp)>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up
                        || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                        continue;
                    var counters = adapter.GetIPStatistics();
                    var timestamp = Stopwatch.GetTimestamp();
                    double? received = null, sent = null;
                    if (_previousNetwork.TryGetValue(adapter.Id, out var previous))
                    {
                        var seconds = Stopwatch.GetElapsedTime(previous.Timestamp, timestamp).TotalSeconds;
                        received = NetworkStatus.RateSince(previous.Received, counters.BytesReceived, seconds);
                        sent = NetworkStatus.RateSince(previous.Sent, counters.BytesSent, seconds);
                    }
                    samples[adapter.Id] = (counters.BytesReceived, counters.BytesSent, timestamp);
                    networks.Add(new(adapter.Name, received, sent));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    networks.Add(new(adapter.Name, null, null, false));
                }
            }
            return (networks, networks.Count == 0 ? "没有已连接的网卡" : "");
        }
        catch (Win32Exception)
        {
            return (networks, "暂时无法读取网卡列表");
        }
        finally
        {
            _previousNetwork = samples;
        }
    }

    // shortcut: over 64 logical processors this measures the calling processor group; use group-aware counters if needed.
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}
