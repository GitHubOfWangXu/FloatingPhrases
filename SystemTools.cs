using System.Diagnostics;
using System.IO;

namespace FloatingPhrases;

public enum SystemTool { TaskManager, ResourceMonitor, Sound, Display, Network, Power, Storage, Apps, Startup, Update }

public static class SystemTools
{
    public static ProcessStartInfo CreateStartInfo(SystemTool tool)
    {
        var target = tool switch
        {
            SystemTool.TaskManager => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Taskmgr.exe"),
            SystemTool.ResourceMonitor => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "resmon.exe"),
            SystemTool.Sound => "ms-settings:sound",
            SystemTool.Display => "ms-settings:display",
            SystemTool.Network => "ms-settings:network-status",
            SystemTool.Power => "ms-settings:powersleep",
            SystemTool.Storage => "ms-settings:storagesense",
            SystemTool.Apps => "ms-settings:appsfeatures",
            SystemTool.Startup => "ms-settings:startupapps",
            SystemTool.Update => "ms-settings:windowsupdate",
            _ => throw new ArgumentOutOfRangeException(nameof(tool))
        };
        return new(target) { UseShellExecute = true };
    }
}
