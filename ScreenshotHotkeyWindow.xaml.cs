using System.Windows;
using System.Windows.Input;

namespace FloatingPhrases;

public partial class ScreenshotHotkeyWindow : Window
{
    private readonly Func<ScreenshotHotkey, string?> _save;

    public ScreenshotHotkeyWindow(ScreenshotHotkey shortcut, Func<ScreenshotHotkey, string?> save)
    {
        InitializeComponent();
        _save = save;
        KeyBox.ItemsSource = Enum.GetValues<Key>().Distinct().Where(ScreenshotHotkey.SupportsKey)
            .Select(key => new { Key = key, Label = ScreenshotHotkey.KeyLabel(key) }).ToArray();
        SetSelection(shortcut);
    }

    private void SetSelection(ScreenshotHotkey shortcut)
    {
        CtrlBox.IsChecked = shortcut.Modifiers.HasFlag(ModifierKeys.Control);
        AltBox.IsChecked = shortcut.Modifiers.HasFlag(ModifierKeys.Alt);
        ShiftBox.IsChecked = shortcut.Modifiers.HasFlag(ModifierKeys.Shift);
        KeyBox.SelectedValue = shortcut.Key;
        ErrorText.Text = "";
    }

    private void KeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var shortcut = new ScreenshotHotkey(Keyboard.Modifiers, key);
        if (!shortcut.IsValid())
        {
            return;
        }

        SetSelection(shortcut);
        KeyBox.IsDropDownOpen = false;
        e.Handled = true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => SetSelection(ScreenshotHotkey.Default);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var modifiers = (CtrlBox.IsChecked == true ? ModifierKeys.Control : ModifierKeys.None)
            | (AltBox.IsChecked == true ? ModifierKeys.Alt : ModifierKeys.None)
            | (ShiftBox.IsChecked == true ? ModifierKeys.Shift : ModifierKeys.None);
        var shortcut = new ScreenshotHotkey(modifiers, KeyBox.SelectedValue is Key key ? key : Key.None);
        if (!shortcut.IsValid())
        {
            ErrorText.Text = "请选择 F1–F11，或为字母、数字和空格加上 Ctrl 或 Alt。";
            return;
        }

        var error = _save(shortcut);
        if (error is not null)
        {
            ErrorText.Text = error;
            return;
        }

        DialogResult = true;
    }
}
