using System.Diagnostics;
using System.IO;
using System.Text;

namespace FloatingPhrases;

public static class CommandLauncher
{
    // Build only; loading, editing and searching a command must never execute it.
    public static ProcessStartInfo CreateStartInfo(CommandShortcut command)
    {
        if (string.IsNullOrWhiteSpace(command.Text) || command.Text.Contains('\0'))
            throw new ArgumentException("请输入有效的命令内容。");
        if (!Enum.IsDefined(command.Shell)) throw new ArgumentException("请选择 PowerShell 或 CMD。");
        var directory = string.IsNullOrWhiteSpace(command.WorkingDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Environment.ExpandEnvironmentVariables(command.WorkingDirectory.Trim());
        if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("工作目录请填写完整路径。");
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("工作目录不存在，请重新选择。");

        string executable, arguments;
        if (command.Shell == CommandShell.PowerShell)
        {
            executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");
            arguments = "-NoLogo -NoProfile -NoExit -EncodedCommand " +
                Convert.ToBase64String(Encoding.Unicode.GetBytes(command.Text));
        }
        else
        {
            var text = command.Text.Trim();
            if (text.Contains('\r') || text.Contains('\n'))
                throw new ArgumentException("CMD 支持单行命令（可用 && 或 & 连接）；多行脚本请选择 PowerShell。");
            executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            arguments = "/D /S /K \"" + text + "\"";
            if (arguments.Length > 8000) throw new ArgumentException("CMD 命令过长，请改用脚本文件。");
        }
        if (arguments.Length + executable.Length > 30000)
            throw new ArgumentException("命令过长，请将内容保存为脚本文件后调用。");
        if (!File.Exists(executable)) throw new FileNotFoundException("未找到所选终端程序。", executable);
        return new ProcessStartInfo(executable, arguments)
        {
            WorkingDirectory = directory,
            UseShellExecute = true
        };
    }
}
