using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace FloatingPhrases;

public sealed class DocumentLauncher
{
    private readonly string? _obsidianExecutable;
    private readonly string? _codeExecutable;

    public DocumentLauncher() : this(FindRegisteredExecutable("obsidian"), FindCodeExecutable()) { }

    public DocumentLauncher(string? obsidianExecutable, string? codeExecutable)
    {
        _obsidianExecutable = obsidianExecutable;
        _codeExecutable = codeExecutable;
    }

    public string GetEditorName(DocumentShortcut document) => ResolveMode(document, null) == DocumentOpenMode.Obsidian ? "Obsidian" : "VS Code";

    public static bool IsMarkdownPath(string? path) => DocumentStore.IsMarkdownPath(path);

    public static string NormalizePath(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim());
        if (!IsMarkdownPath(path)) throw new InvalidOperationException("请选择 .md 或 .markdown 文档。");
        return Path.GetFullPath(path);
    }

    public ProcessStartInfo CreateStartInfo(DocumentShortcut document, DocumentOpenMode? overrideMode = null)
    {
        var path = NormalizePath(document.Path);
        if (!File.Exists(path)) throw new IOException("文档不存在或已移动，请重新选择文件。");
        var mode = ResolveMode(document, overrideMode);
        var executable = mode == DocumentOpenMode.VSCode ? _codeExecutable : _obsidianExecutable;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new IOException(mode == DocumentOpenMode.VSCode
                ? "未找到 VS Code，请安装 VS Code 后重启应用。"
                : "未找到 Obsidian，请安装最新版并运行一次后重启应用。");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false };
        info.Environment.Remove("ELECTRON_RUN_AS_NODE");
        if (mode == DocumentOpenMode.VSCode) info.ArgumentList.Add("--reuse-window");
        info.ArgumentList.Add(path);
        return info;
    }

    private DocumentOpenMode ResolveMode(DocumentShortcut document, DocumentOpenMode? overrideMode)
    {
        var mode = overrideMode ?? document.OpenMode;
        return mode == DocumentOpenMode.VSCode ? DocumentOpenMode.VSCode : DocumentOpenMode.Obsidian;
    }

    private static string? FindRegisteredExecutable(string protocol)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(protocol + @"\shell\open\command");
            if (key?.GetValue(null) is string command && command.StartsWith('"'))
            {
                var end = command.IndexOf('"', 1);
                if (end > 1 && File.Exists(command[1..end])) return command[1..end];
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException) { }
        return null;
    }

    private static string? TryFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim())); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException or SecurityException) { return null; }
    }

    private static string? FindCodeExecutable()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var trimmed = TryFullPath(directory.Trim().Trim('"'));
            if (trimmed is null) continue;
            var candidate = Path.Combine(trimmed, "Code.exe");
            if (File.Exists(candidate)) return candidate;
            if (File.Exists(Path.Combine(trimmed, "code.cmd")))
            {
                candidate = TryFullPath(Path.Combine(trimmed, "..", "Code.exe"));
                if (candidate is not null && File.Exists(candidate)) return candidate;
            }
        }
        if (FindRegisteredExecutable("vscode") is { } registered) return registered;
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            var candidate = Path.Combine(root, root == Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                ? @"Programs\Microsoft VS Code\Code.exe" : @"Microsoft VS Code\Code.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
