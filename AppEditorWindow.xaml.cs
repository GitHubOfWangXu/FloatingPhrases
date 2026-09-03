using System.IO;
using System.Windows;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace FloatingPhrases;

public partial class AppEditorWindow : Window
{
    private string _selectedPath = "";

    public AppEditorWindow(AppShortcut? shortcut = null)
    {
        InitializeComponent();
        CategoryBox.ItemsSource = AppCategories.All;
        CategoryBox.SelectedItem = AppCategories.Other;

        if (shortcut is not null)
        {
            NameBox.Text = shortcut.Name;
            PathBox.Text = shortcut.Path;
            CategoryBox.SelectedItem = AppCategories.Normalize(shortcut.Category);
            PinnedBox.IsChecked = shortcut.IsPinned;
        }

        Loaded += (_, _) => NameBox.Focus();
    }

    public string ShortcutName => string.IsNullOrWhiteSpace(NameBox.Text)
        ? Path.GetFileNameWithoutExtension(_selectedPath)
        : NameBox.Text.Trim();
    public string AppPath => _selectedPath;
    public string Category => CategoryBox.SelectedItem as string ?? AppCategories.Other;
    public bool IsPinned => PinnedBox.IsChecked == true;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要启动的本地软件",
            Filter = "程序和快捷方式|*.exe;*.com;*.bat;*.cmd;*.msi;*.lnk|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        var currentPath = Environment.ExpandEnvironmentVariables(PathBox.Text.Trim());
        if (File.Exists(currentPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
        }

        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName)
                    .Replace(" - 快捷方式", "", StringComparison.CurrentCultureIgnoreCase);
            }

            CategoryBox.SelectedItem = AppCategories.Infer(NameBox.Text.Trim(), dialog.FileName);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(PathBox.Text.Trim());
            if (string.IsNullOrWhiteSpace(expandedPath))
            {
                ShowPathError("请选择一个程序或快捷方式。");
                return;
            }

            _selectedPath = Path.GetFullPath(expandedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ShowPathError("程序路径格式无效。");
            return;
        }

        if (!File.Exists(_selectedPath))
        {
            ShowPathError("该程序或快捷方式不存在，请重新选择。");
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

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
