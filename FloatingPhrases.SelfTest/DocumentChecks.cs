using System.Text.Json;
using FloatingPhrases;

internal static class DocumentChecks
{
    // Opt-in: sends a real open request; never edits the supplied document.
    public static void OpenInstalledEditor(string path)
    {
        var launcher = new DocumentLauncher();
        var document = new DocumentShortcut { Path = path };
        System.Diagnostics.Process.Start(launcher.CreateStartInfo(document));
        Console.WriteLine("Document open request sent to " + launcher.GetEditorName(document) + ".");
    }

    public static void Run(string tempDirectory)
    {
        var testDirectory = Path.Combine(tempDirectory, "document-checks");
        var vault = Path.Combine(testDirectory, "示例 Vault");
        var sibling = vault + "-other";
        Directory.CreateDirectory(vault);
        Directory.CreateDirectory(sibling);
        var documentPath = Path.Combine(vault, "中文 空格 & 100%.md");
        var outsidePath = Path.Combine(sibling, "外部.markdown");
        const string content = "# 原文\r\n  内容 & 100%\r\n";
        File.WriteAllText(documentPath, content);
        File.WriteAllText(outsidePath, content);
        var codeExecutable = Path.Combine(testDirectory, "Code.exe");
        File.WriteAllText(codeExecutable, "测试占位，不执行");
        var obsidianExecutable = Path.Combine(testDirectory, "Obsidian.exe");
        File.WriteAllText(obsidianExecutable, "测试占位，不执行");
        var dataPath = Path.Combine(testDirectory, "documents.json");
        var store = new DocumentStore(dataPath);
        var document = new DocumentShortcut { Name = " 测试文档 ", Path = documentPath, IsPinned = true, OpenMode = DocumentOpenMode.VSCode };
        store.Save([document, null, new() { Path = "" }, new() { Path = "example.txt" }, new() { Path = outsidePath }]);
        var loaded = store.Load();
        Require(loaded.Count == 2 && loaded[0].Id == document.Id && loaded[0].Name == "测试文档" && loaded[0].Path == documentPath &&
            loaded[0].IsPinned && loaded[0].OpenMode == DocumentOpenMode.VSCode && loaded[1].Name == "外部", "文档入口应归一化并完整往返，忽略无效条目");
        store.Save(loaded);
        Require(File.Exists(dataPath + ".bak"), "覆盖保存应保留备份");
        var duplicateIdsJson = JsonSerializer.Serialize(new[]
        {
            new DocumentShortcut { Id = document.Id, Path = documentPath },
            new DocumentShortcut { Id = document.Id, Path = outsidePath },
            new DocumentShortcut { Id = Guid.Empty, Path = outsidePath }
        });
        File.WriteAllText(dataPath, duplicateIdsJson);
        loaded = store.Load();
        Require(loaded.Count == 3 && loaded.Select(item => item.Id).Distinct().Count() == 3 && loaded.All(item => item.Id != Guid.Empty) &&
            loaded[0].Id == document.Id && File.ReadAllText(dataPath) == duplicateIdsJson, "读取应修复重复/空 Id，且不得改写原入口文件");
        File.WriteAllText(dataPath, JsonSerializer.Serialize(new object[]
        {
            new { path = documentPath, openMode = "FutureEditor" }, new { path = outsidePath, openMode = 999 },
            new { path = "missing.md", openMode = "Obsidian" }
        }));
        loaded = store.Load();
        Require(loaded.Count == 3 && loaded.All(item => item.OpenMode == DocumentOpenMode.Obsidian),
            "未知字符串/数字模式应归 Obsidian，丢失文件入口应保留");
        var legacyJson = JsonSerializer.Serialize(new object[]
        {
            new { path = documentPath, openMode = "Auto" }, new { path = outsidePath, openMode = 0 },
            new { path = outsidePath }, new { path = documentPath, openMode = "VSCode" },
            new { path = outsidePath, openMode = "Obsidian" }
        });
        File.WriteAllText(dataPath, legacyJson);
        loaded = store.Load();
        Require(loaded.Take(3).All(item => item.OpenMode == DocumentOpenMode.Obsidian) &&
            loaded[3].OpenMode == DocumentOpenMode.VSCode && loaded[4].OpenMode == DocumentOpenMode.Obsidian &&
            File.ReadAllText(dataPath) == legacyJson, "旧 Auto/缺省模式应兼容为 Obsidian，保留明确选择；读取不改原文件");
        store.Save(loaded);
        Require(File.ReadAllText(dataPath + ".bak") == legacyJson && !File.ReadAllText(dataPath).Contains("\"Auto\""),
            "首次保存应写入明确打开方式，并备份旧 JSON");
        File.WriteAllText(dataPath, "{broken json");
        Require(store.Load().Count == 0 && Directory.EnumerateFiles(testDirectory, "documents.corrupt-*.json").Any(), "损坏文档入口文件应保留并恢复为空列表");

        var launcher = new DocumentLauncher(obsidianExecutable, codeExecutable);
        var longExtension = Path.Combine(vault, "兼容扩展.markdown");
        File.WriteAllText(longExtension, content);
        Require(launcher.GetEditorName(new() { Path = longExtension }) == "Obsidian", "默认打开方式不能根据路径或扩展名切换软件");
        document.OpenMode = DocumentOpenMode.Auto;
        var obsidian = launcher.CreateStartInfo(document);
        Require(launcher.GetEditorName(document) == "Obsidian" && !obsidian.UseShellExecute &&
            obsidian.FileName == obsidianExecutable && obsidian.ArgumentList.SequenceEqual([documentPath]),
            "Obsidian 应直接接收原文件路径，通过 ArgumentList 保留中文、空格和特殊字符");
        var code = launcher.CreateStartInfo(document, DocumentOpenMode.VSCode);
        Require(code.FileName == codeExecutable && !code.UseShellExecute && code.ArgumentList.SequenceEqual(["--reuse-window", documentPath]),
            "覆盖为 VS Code 应直接执行并通过 ArgumentList 完整保留中文、空格和特殊字符");
        Require(!code.Environment.ContainsKey("ELECTRON_RUN_AS_NODE") && !obsidian.Environment.ContainsKey("ELECTRON_RUN_AS_NODE"),
            "编辑器启动不能继承 Electron 的 Node 模式");
        var outside = new DocumentShortcut { Path = outsidePath };
        Require(launcher.GetEditorName(outside) == "Obsidian" && launcher.CreateStartInfo(outside).FileName == obsidianExecutable,
            "库外文件也应默认选择 Obsidian");
        Require(launcher.CreateStartInfo(outside, DocumentOpenMode.Obsidian).ArgumentList.Single() == outsidePath,
            "库外文件应允许显式选择 Obsidian");
        outside.OpenMode = DocumentOpenMode.VSCode;
        Require(launcher.GetEditorName(outside) == "VS Code" && launcher.CreateStartInfo(outside).FileName == codeExecutable,
            "每个文件明确选择 VS Code 后应按该设置打开");
        RequireThrows<IOException>(() => new DocumentLauncher(null, codeExecutable).CreateStartInfo(document),
            "缺少 Obsidian 应给出错误，不能悄悄改用 VS Code");
        RequireThrows<IOException>(() => new DocumentLauncher(obsidianExecutable, null).CreateStartInfo(outside), "缺少 VS Code 应给出错误");
        RequireThrows<IOException>(() => launcher.CreateStartInfo(new() { Path = Path.Combine(vault, "missing.md") }), "丢失文件不能启动");
        RequireThrows<InvalidOperationException>(() => launcher.CreateStartInfo(new() { Path = Path.Combine(vault, "example.txt") }), "非 Markdown 文件不能启动");
        var markdownDirectory = Path.Combine(vault, "folder.md");
        Directory.CreateDirectory(markdownDirectory);
        RequireThrows<IOException>(() => launcher.CreateStartInfo(new() { Path = markdownDirectory }), "扩展名为 md 的目录不能启动");
        Require(DocumentLauncher.IsMarkdownPath("EXAMPLE.MARKDOWN") && !DocumentLauncher.IsMarkdownPath(""), "扩展名检查应忽略大小写并拒绝空白");
        const string environmentKey = "FLOATINGPHRASES_DOCUMENT_SELFTEST";
        var previous = Environment.GetEnvironmentVariable(environmentKey);
        try
        {
            Environment.SetEnvironmentVariable(environmentKey, documentPath);
            Require(DocumentLauncher.NormalizePath(" %" + environmentKey + "% ") == documentPath, "应先展开环境变量再检查 Markdown 扩展名");
        }
        finally { Environment.SetEnvironmentVariable(environmentKey, previous); }
        Require(File.ReadAllText(documentPath) == content && File.ReadAllText(outsidePath) == content, "存储和准备启动参数不得修改文档正文");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }
}
