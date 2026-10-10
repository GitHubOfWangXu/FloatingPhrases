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
        Require(loaded.Count == 3 && loaded[0].OpenMode == DocumentOpenMode.Auto && loaded[1].OpenMode == DocumentOpenMode.Auto &&
            loaded[2].OpenMode == DocumentOpenMode.Obsidian, "未知字符串/数字模式应归 Auto，丢失文件入口和合法模式应保留");
        File.WriteAllText(dataPath, "{broken json");
        Require(store.Load().Count == 0 && Directory.EnumerateFiles(testDirectory, "documents.corrupt-*.json").Any(), "损坏文档入口文件应保留并恢复为空列表");

        var launcher = new DocumentLauncher([vault], codeExecutable);
        var longExtension = Path.Combine(vault, "兼容扩展.markdown");
        File.WriteAllText(longExtension, content);
        Require(!launcher.CanOpenInObsidian(longExtension) && launcher.GetEditorName(new() { Path = longExtension }) == "VS Code",
            "Obsidian 原生笔记仅使用 .md，库内 .markdown 应交给 VS Code");
        document.OpenMode = DocumentOpenMode.Auto;
        Require(launcher.CanOpenInObsidian(documentPath) && !launcher.CanOpenInObsidian(outsidePath), "Vault 必须按目录边界匹配，不能误认兄弟前缀");
        Require(!launcher.CanOpenInObsidian(Path.Combine(vault, "..", Path.GetFileName(sibling), "外部.markdown")), "父目录跳转必须先归一化再匹配 Vault");
        Require(!launcher.CanOpenInObsidian("\0invalid.md") && launcher.GetEditorName(new() { Path = "\0invalid.md" }) == "VS Code",
            "损坏路径不能使列表刷新崩溃");
        var obsidian = launcher.CreateStartInfo(document);
        Require(launcher.GetEditorName(document) == "Obsidian" && obsidian.UseShellExecute &&
            obsidian.FileName == "obsidian://open?path=" + Uri.EscapeDataString(documentPath) + "&paneType=tab", "Vault 内 Auto 应生成完整转义的新标签 Obsidian URI");
        var code = launcher.CreateStartInfo(document, DocumentOpenMode.VSCode);
        Require(code.FileName == codeExecutable && !code.UseShellExecute && code.ArgumentList.SequenceEqual(["--reuse-window", documentPath]),
            "覆盖为 VS Code 应直接执行并通过 ArgumentList 完整保留中文、空格和特殊字符");
        Require(!code.Environment.ContainsKey("ELECTRON_RUN_AS_NODE"), "VS Code 启动不能继承 Electron 的 Node 模式");
        var outside = new DocumentShortcut { Path = outsidePath };
        Require(launcher.GetEditorName(outside) == "VS Code" && launcher.CreateStartInfo(outside).FileName == codeExecutable, "Vault 外 Auto 应选择 VS Code");
        RequireThrows<InvalidOperationException>(() => launcher.CreateStartInfo(outside, DocumentOpenMode.Obsidian), "Vault 外显式 Obsidian 应拒绝");
        RequireThrows<IOException>(() => new DocumentLauncher(null, null).CreateStartInfo(outside), "缺少 VS Code 应给出错误");
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
