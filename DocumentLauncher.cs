using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text.Json;
using Microsoft.Win32;

namespace FloatingPhrases;

public sealed class DocumentLauncher
{
    private readonly string[] _vaultPaths;
    private readonly string? _codeExecutable;
    private readonly bool _obsidianAvailable;

    public DocumentLauncher() : this(FindVaultPaths(), FindCodeExecutable()) =>
        _obsidianAvailable = IsProtocolRegistered("obsidian");

    public DocumentLauncher(IEnumerable<string>? vaultPaths, string? codeExecutable)
    {
        _vaultPaths = (vaultPaths ?? []).Select(TryFullPath).Where(path => path is not null)
            .Select(path => Path.TrimEndingDirectorySeparator(path!)).ToArray();
        _codeExecutable = codeExecutable;
        _obsidianAvailable = _vaultPaths.Length > 0;
    }

    public bool CanOpenInObsidian(string path)
    {
        var fullPath = TryFullPath(path);
        if (fullPath is null || !Path.GetExtension(fullPath).Equals(".md", StringComparison.OrdinalIgnoreCase)) return false;
        return _vaultPaths.Any(vault => fullPath.StartsWith(Path.EndsInDirectorySeparator(vault) ? vault : vault + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase));
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
        if (ResolveMode(document, overrideMode) == DocumentOpenMode.Obsidian)
        {
            if (!CanOpenInObsidian(path)) throw new InvalidOperationException("此入口仅支持已登记 Obsidian 知识库内的 .md 文件，请使用 VS Code 打开。");
            if (!_obsidianAvailable) throw new IOException("未找到 Obsidian 打开协议，请先安装并启动 Obsidian。");
            return new ProcessStartInfo("obsidian://open?path=" + Uri.EscapeDataString(path) + "&paneType=tab") { UseShellExecute = true };
        }
        if (string.IsNullOrWhiteSpace(_codeExecutable) || !File.Exists(_codeExecutable))
            throw new IOException("未找到 VS Code，请安装 VS Code 后重启应用。");
        var info = new ProcessStartInfo(_codeExecutable) { UseShellExecute = false };
        info.Environment.Remove("ELECTRON_RUN_AS_NODE");
        info.ArgumentList.Add("--reuse-window");
        info.ArgumentList.Add(path);
        return info;
    }

    private DocumentOpenMode ResolveMode(DocumentShortcut document, DocumentOpenMode? overrideMode)
    {
        var mode = overrideMode ?? document.OpenMode;
        return !Enum.IsDefined(mode) || mode == DocumentOpenMode.Auto
            ? (CanOpenInObsidian(document.Path) ? DocumentOpenMode.Obsidian : DocumentOpenMode.VSCode) : mode;
    }

    private static string[] FindVaultPaths()
    {
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obsidian", "obsidian.json");
        try
        {
            if (!File.Exists(file)) return [];
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("vaults", out var vaults) || vaults.ValueKind != JsonValueKind.Object) return [];
            return vaults.EnumerateObject().Select(vault =>
                vault.Value.ValueKind == JsonValueKind.Object && vault.Value.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String
                    ? path.GetString() : null)
                .Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path))
                .Select(TryFullPath).Where(path => path is not null && Directory.Exists(path)).Cast<string>().ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    private static bool IsProtocolRegistered(string protocol)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(protocol);
            return key?.GetValue("URL Protocol") is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException) { return false; }
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
        try
        {
            using var protocol = Registry.ClassesRoot.OpenSubKey(@"vscode\shell\open\command");
            if (protocol?.GetValue(null) is string command && command.StartsWith('"'))
            {
                var end = command.IndexOf('"', 1);
                if (end > 1 && File.Exists(command[1..end])) return command[1..end];
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException) { }
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
