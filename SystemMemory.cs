using System.Runtime.InteropServices;

namespace FloatingPhrases;

public readonly record struct MemoryUsage(ulong TotalBytes, ulong AvailableBytes)
{
    public ulong UsedBytes => TotalBytes - Math.Min(AvailableBytes, TotalBytes);
    public double UsedPercent => TotalBytes == 0 ? 0 : 100.0 * UsedBytes / TotalBytes;
    public string Summary => $"内存 {UsedPercent:0}%";
    public string Details => $"系统物理内存：已用 {UsedBytes / 1073741824.0:0.0} / 共 {TotalBytes / 1073741824.0:0.0} GiB";
}

public static class SystemMemory
{
    public static MemoryUsage? Read()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) && status.TotalPhysical > 0
            ? new MemoryUsage(status.TotalPhysical, status.AvailablePhysical)
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
        public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
