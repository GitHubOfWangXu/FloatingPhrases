using System.Windows.Controls;
using System.Windows.Input;

namespace FloatingPhrases;

public partial class HotkeyPicker : System.Windows.Controls.UserControl
{
    private static readonly System.Collections.IEnumerable KeyOptions = Enum.GetValues<Key>().Distinct()
        .Where(key => key == Key.None || Hotkey.SupportsKey(key))
        .Select(key => new { Key = key, Label = Hotkey.KeyLabel(key) }).ToArray();

    public HotkeyPicker()
    {
        InitializeComponent();
        KeyBox.ItemsSource = KeyOptions;
    }

    public Hotkey Value
    {
        get
        {
            if (KeyBox.SelectedValue is not Key key || key == Key.None)
            {
                return default;
            }

            var modifiers = (CtrlBox.IsChecked == true ? ModifierKeys.Control : ModifierKeys.None)
                | (AltBox.IsChecked == true ? ModifierKeys.Alt : ModifierKeys.None)
                | (ShiftBox.IsChecked == true ? ModifierKeys.Shift : ModifierKeys.None);
            return new Hotkey(modifiers, key);
        }
        set
        {
            CtrlBox.IsChecked = value.Modifiers.HasFlag(ModifierKeys.Control);
            AltBox.IsChecked = value.Modifiers.HasFlag(ModifierKeys.Alt);
            ShiftBox.IsChecked = value.Modifiers.HasFlag(ModifierKeys.Shift);
            KeyBox.SelectedValue = value.Key;
        }
    }

    private void KeyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var enabled = KeyBox.SelectedValue is Key key && key != Key.None;
        CtrlBox.IsEnabled = AltBox.IsEnabled = ShiftBox.IsEnabled = enabled;
    }

    private void KeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var shortcut = new Hotkey(Keyboard.Modifiers, key);
        if (shortcut.IsEnabled() && shortcut.IsValid())
        {
            Value = shortcut;
            KeyBox.IsDropDownOpen = false;
            e.Handled = true;
        }
    }
}
