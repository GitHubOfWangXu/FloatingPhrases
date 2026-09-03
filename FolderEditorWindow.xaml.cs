using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace FloatingPhrases;

public partial class FolderEditorWindow : Window
{
    private string _selectedPath = "";

    public FolderEditorWindow(FolderShortcut? shortcut = null)
    {
        InitializeComponent();

        if (shortcut is not null)
        {
            NameBox.Text = shortcut.Name;
            PathBox.Text = shortcut.Path;
            PinnedBox.IsChecked = shortcut.IsPinned;
        }

        Loaded += (_, _) => NameBox.Focus();
    }

    public string ShortcutName => string.IsNullOrWhiteSpace(NameBox.Text)
        ? GetDefaultName(_selectedPath)
        : NameBox.Text.Trim();

    public string FolderPath => _selectedPath;
    public bool IsPinned => PinnedBox.IsChecked == true;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择要添加的本地文件夹",
            Multiselect = false
        };

        var currentPath = Environment.ExpandEnvironmentVariables(PathBox.Text.Trim());
        if (Directory.Exists(currentPath))
        {
            dialog.InitialDirectory = currentPath;
        }

        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FolderName;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(PathBox.Text.Trim());
            if (string.IsNullOrWhiteSpace(expandedPath))
            {
                ShowPathError("请选择一个文件夹。");
                return;
            }

            _selectedPath = Path.GetFullPath(expandedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ShowPathError("文件夹路径格式无效。");
            return;
        }

        if (!Directory.Exists(_selectedPath))
        {
            ShowPathError("该文件夹不存在，请重新选择。");
            return;
        }

        DialogResult = true;
    }

    private void ShowPathError(string message)
    {
        System.Windows.MessageBox.Show(this, message, "Floating Phrases", MessageBoxButton.OK, MessageBoxImage.Information);
        PathBox.Focus();
        PathBox.SelectAll();
    }

    private static string GetDefaultName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
