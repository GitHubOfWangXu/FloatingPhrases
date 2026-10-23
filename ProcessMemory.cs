using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace FloatingPhrases;

public sealed record ProcessMemoryInfo(int Id, string Name, long WorkingSetBytes, long PrivateBytes,
    long? StartedFileTime, int? SessionId)
{
    public string WorkingSetText => $"{WorkingSetBytes / 1048576.0:0.0} MiB";
    public string PrivateText => $"{PrivateBytes / 1048576.0:0.0} MiB";
    public string? EndBlockedReason => ProcessMemory.GetEndBlockedReason(this);
    public bool CanEnd => EndBlockedReason is null;
}

public sealed record ProcessMemorySnapshot(MemoryUsage? Memory, IReadOnlyList<ProcessMemoryInfo> Processes, int Skipped);

public static class ProcessMemory
{
    private const uint QueryLimitedInformation = 0x1000, Terminate = 0x0001, Synchronize = 0x00100000, TokenQuery = 0x0008;
    private static readonly int CurrentSession = ReadCurrentSession();

    private static int ReadCurrentSession()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    public static ProcessMemorySnapshot Read()
    {
        var items = new List<ProcessMemoryInfo>();
        var skipped = 0;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    process.Refresh();
                    long? started = null;
                    int? session = null;
                    try
                    {
                        started = process.StartTime.ToUniversalTime().ToFileTimeUtc();
                        session = process.SessionId;
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
                    var name = process.ProcessName;
                    var workingSet = process.WorkingSet64;
                    var privateBytes = process.PrivateMemorySize64;
                    items.Add(new(process.Id, name, workingSet, privateBytes, started, session));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    skipped++;
                }
            }
        }
        return new(SystemMemory.Read(), items.OrderByDescending(item => item.WorkingSetBytes).ThenBy(item => item.Id).ToArray(), skipped);
    }

    public static string? GetEndBlockedReason(ProcessMemoryInfo target) => target.Id switch
    {
        <= 4 => "不能结束系统进程。",
        var id when id == Environment.ProcessId => "不能结束 Floating Phrases 自身。",
        _ when target.StartedFileTime is null => "无法核验启动时间，请使用任务管理器查看。",
        _ when target.SessionId is null || target.SessionId == 0 || target.SessionId != CurrentSession
            => "系统服务及其他会话的进程请使用系统管理工具。",
        _ => null
    };

    public static string End(ProcessMemoryInfo target)
    {
        if (GetEndBlockedReason(target) is { } blocked) throw new InvalidOperationException(blocked);
        // Keep one handle for identity checks and termination, so PID reuse cannot target a different process.
        using var handle = OpenProcess(QueryLimitedInformation | Terminate | Synchronize, false, target.Id);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!GetProcessTimes(handle, out var started, out _, out _, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (started != target.StartedFileTime)
            throw new InvalidOperationException("进程已退出或身份已变化，请刷新后重新选择。");
        if (!IsProcessCritical(handle, out var critical)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (critical) throw new InvalidOperationException("不能结束 Windows 关键进程。");
        if (!OpenProcessToken(handle, TokenQuery, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        using (var owner = new WindowsIdentity(token.DangerousGetHandle()))
        using (var current = WindowsIdentity.GetCurrent())
        {
            if (owner.User is null || current.User is null || !owner.User.Equals(current.User))
                throw new InvalidOperationException("只能结束当前用户的普通进程。");
        }
        if (!TerminateProcess(handle, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return WaitForSingleObject(handle, 3000) switch
        {
            0 => $"已结束 {target.Name}（PID {target.Id}）。",
            258 => "已请求结束，但进程尚未退出，请刷新确认。",
            _ => throw new Win32Exception(Marshal.GetLastWin32Error())
        };
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int id);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessCritical(SafeProcessHandle process, [MarshalAs(UnmanagedType.Bool)] out bool critical);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
}
