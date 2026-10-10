using System.IO;
using System.Windows;

namespace FloatingPhrases;

public partial class CommandEditorWindow : Window
{
    private readonly Guid _id;
    public CommandShortcut? Result { get; private set; }

    public CommandEditorWindow(CommandShortcut? command = null, bool confirmExecution = false)
    {
        InitializeComponent();
        _id = command?.Id ?? Guid.NewGuid();
        NameBox.Text = command?.Name ?? "";
        DirectoryBox.Text = command?.WorkingDirectory ?? "";
        CommandBox.Text = command?.Text ?? "";
        ShellBox.SelectedIndex = command is null ? 0 : (int)command.Shell;
        if (confirmExecution)
        {
            Title = "确认运行命令";
            NameBox.IsReadOnly = DirectoryBox.IsReadOnly = CommandBox.IsReadOnly = true;
            ShellBox.IsEnabled = BrowseButton.IsEnabled = false;
            SaveButton.Content = "运行";
            FeedbackText.Text = "此命令将以当前用户权限执行。请核对完整内容和工作目录；取消不会执行。";
        }
        Loaded += (_, _) => CommandBox.Focus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择命令的工作目录" };
        if (dialog.ShowDialog(this) == true) DirectoryBox.Text = dialog.FolderName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var command = new CommandShortcut
        {
            Id = _id,
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "未命名命令" : NameBox.Text.Trim(),
            Text = CommandBox.Text,
            WorkingDirectory = DirectoryBox.Text.Trim(),
            Shell = (CommandShell)ShellBox.SelectedIndex
        };
        try
        {
            CommandLauncher.CreateStartInfo(command);
            Result = command;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            FeedbackText.Text = ex.Message;
        }
    }
}
