using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FloatingPhrases;

internal static class CommandChecks
{
    public static void Run(string directory)
    {
        var path = Path.Combine(directory, "commands.json");
        var store = new CommandStore(path);
        Check(store.Load().Count == 0 && !File.Exists(path), "读取空命令列表不能写入或执行命令");
        var command = new CommandShortcut { Name = "中文测试", Text = "  Write-Output '中文 \"引号\"'\nGet-Location  ", WorkingDirectory = directory };
        store.Save([command]);
        Check(store.Load()[0] == command, "命令文本、空白、终端和目录应完整往返保存");
        store.Save([command with { Shell = CommandShell.Cmd, Text = "echo sample" }]);
        Check(new CommandStore(path + ".bak").Load()[0] == command, "修改前的命令数据应保留备份");
        File.WriteAllText(path, "[null,{\"text\":\"echo sample\",\"name\":null,\"workingDirectory\":null}]");
        Check(store.Load().Count == 1 && store.Load()[0].Name == "未命名命令", "空条目与空名称应安全归一化");
        File.WriteAllText(path, "{broken json");
        Check(store.Load().Count == 0 && Directory.EnumerateFiles(directory, "commands.corrupt-*.json").Any(),
            "损坏文件应留档并恢复为空列表");
        var info = CommandLauncher.CreateStartInfo(command);
        Check(info.UseShellExecute && info.WorkingDirectory == directory && info.Arguments.Contains("-NoExit"),
            "应在指定目录打开可保留输出的终端");
        Check(Encoding.Unicode.GetString(Convert.FromBase64String(info.Arguments.Split(' ')[^1])) == command.Text,
            "编码命令应完整保留中文、多行、引号和空白");
        Check(CommandLauncher.CreateStartInfo(command with { WorkingDirectory = "" }).WorkingDirectory ==
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "空目录应使用用户目录");
        Reject(command with { Text = " " });
        Reject(command with { Text = "echo\0sample" });
        Reject(command with { WorkingDirectory = "relative-path" });
        Reject(command with { WorkingDirectory = Path.Combine(directory, "missing") });
        Reject(command with { Shell = (CommandShell)42 });
        Reject(command with { Text = new string('x', 15000) });
        Reject(command with { Shell = CommandShell.Cmd, Text = "echo one\necho two" });
        RunProcesses(directory, false);
        RunView(store, command);
        Console.WriteLine("Command checks passed: storage, validation, real shell quoting and UI actions.");
    }

    // Opt-in visible terminal smoke; commands write only disposable markers and exit.
    public static void RunProcesses(string directory, bool visible)
    {
        var work = Path.Combine(directory, "command space 中文");
        Directory.CreateDirectory(work);
        var marker = "中文 \"quoted\" & $literal";
        Launch(new CommandShortcut
        {
            Text = "$value = '" + marker + "'\nSet-Content -LiteralPath 'ps-result.txt' -Value $value -Encoding UTF8\nexit 0",
            WorkingDirectory = work
        }, visible);
        Check(File.ReadAllText(Path.Combine(work, "ps-result.txt")).Trim() == marker,
            "PowerShell 应实际执行中文、多行和带引号的内容，并使用指定工作目录");
        var script = Path.Combine(work, "sample script.cmd");
        File.WriteAllText(script, "@echo off\r\necho %~1>cmd-result.txt\r\n", Encoding.ASCII);
        Launch(new CommandShortcut
        {
            Shell = CommandShell.Cmd, WorkingDirectory = work,
            Text = "\"" + script + "\" \"quoted argument\" & exit 0"
        }, visible);
        Check(File.ReadAllText(Path.Combine(work, "cmd-result.txt")).Trim() == "quoted argument",
            "CMD 应支持中文和空格路径、带引号参数、组合命令及指定目录");
        Console.WriteLine(visible ? "Visible PowerShell/CMD terminal smoke passed." : "Isolated PowerShell/CMD process checks passed.");
    }

    private static void Launch(CommandShortcut command, bool visible)
    {
        var info = CommandLauncher.CreateStartInfo(command);
        if (!visible) { info.UseShellExecute = false; info.CreateNoWindow = true; }
        using var process = Process.Start(info) ?? throw new InvalidOperationException("自测进程未启动");
        if (!process.WaitForExit(10000))
        {
            process.Kill(true);
            throw new InvalidOperationException("自测终端未按时退出");
        }
        Check(process.ExitCode == 0, "自测命令应成功退出");
    }

    private static void RunView(CommandStore store, CommandShortcut command)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var confirmed = false;
                var launches = 0;
                var edits = 0;
                string? copied = null;
                var view = new CommandListView(store, (item, preview) =>
                {
                    if (preview)
                    {
                        Check(item!.WorkingDirectory == command.WorkingDirectory, "确认界面应显示实际执行目录");
                        return confirmed ? item : null;
                    }
                    edits++;
                    return item is null ? command : item with { Name = "已修改" };
                }, _ => launches++, text => copied = text) { Width = 340, Height = 480 };
                T Named<T>(string name) => (T)view.FindName(name);
                void ClickRow(string label)
                {
                    view.Measure(new Size(340, 480)); view.Arrange(new Rect(0, 0, 340, 480)); view.UpdateLayout();
                    Descendants(Named<ItemsControl>("CommandList")).OfType<Button>()
                        .First(button => Equals(button.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                view.AddCommand();
                Check(store.Load().Count == 1 && launches == 0, "新增和保存不能自动执行");
                ClickRow("运行"); Check(launches == 0, "取消确认不能执行命令");
                confirmed = true;
                ClickRow("运行");
                Check(launches == 1 && Named<TextBlock>("FeedbackText").Text.StartsWith("已启动终端"),
                    "明确确认后应启动一次，并提示用户查看终端结果");
                ClickRow("复制"); Check(copied == command.Text && launches == 1, "复制只复制完整命令");
                ClickRow("改"); Check(store.Load()[0].Name == "已修改" && edits == 2, "编辑应持久化但不执行");
                Named<TextBox>("SearchBox").Text = "not-found";
                Check(Named<ItemsControl>("CommandList").Items.Count == 0, "搜索应过滤命令");
                Named<TextBox>("SearchBox").Text = "";
                var blocked = store.Load().ToList();
                var storePath = Path.Combine(command.WorkingDirectory, "commands.json");
                Directory.CreateDirectory(storePath + ".tmp");
                ClickRow("删");
                Check(Named<ItemsControl>("CommandList").Items.Count == 1 && store.Load().SequenceEqual(blocked)
                    && Named<TextBlock>("FeedbackText").Text.StartsWith("保存失败"), "保存失败不能让界面丢失原命令");
                Directory.Delete(storePath + ".tmp");
                ClickRow("删"); Check(store.Load().Count == 0 && launches == 1, "删除仅移除入口");
                var preview = new CommandEditorWindow(command, true);
                Check(((TextBox)preview.FindName("CommandBox")).IsReadOnly &&
                    ((TextBox)preview.FindName("CommandBox")).Text == command.Text &&
                    !((ComboBox)preview.FindName("ShellBox")).IsEnabled,
                    "确认窗口应显示完整命令并禁止静默改写执行内容");
                preview.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("命令界面自测失败", failure);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Reject(CommandShortcut command)
    {
        try { CommandLauncher.CreateStartInfo(command); }
        catch (Exception ex) when (ex is ArgumentException or IOException) { return; }
        throw new InvalidOperationException("无效命令配置应被拒绝");
    }
    private static void Check(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
}
