using System.Windows;
using System.Windows.Controls;

namespace FloatingPhrases;

public partial class SettingsWindow : Window
{
    private readonly Func<WindowSettings, string?> _save;

    public SettingsWindow(WindowSettings settings, Func<WindowSettings, string?> save, string status)
    {
        InitializeComponent();
        _save = save;
        ModeBox.SelectedIndex = (int)settings.Mode;
        ThemeBox.SelectedIndex = (int)settings.Theme;
        OpacitySlider.Value = settings.IdleOpacity * 100;
        SetHotkeys(settings);
        ErrorText.Text = status;
    }

    private void SetHotkeys(WindowSettings settings)
    {
        VisibilityPicker.Value = settings.VisibilityHotkey;
        ClickThroughPicker.Value = settings.ClickThroughHotkey;
        ScreenshotPicker.Value = settings.ScreenshotHotkey;
        TranslationPicker.Value = settings.TranslationHotkey;
    }

    private void ModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OpacitySlider is not null)
        {
            OpacitySlider.IsEnabled = ModeBox.SelectedIndex != (int)WindowMode.Normal;
        }
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        SetHotkeys(new WindowSettings());
        ErrorText.Text = "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = new WindowSettings
        {
            Mode = (WindowMode)ModeBox.SelectedIndex,
            Theme = (AppTheme)ThemeBox.SelectedIndex,
            IdleOpacity = OpacitySlider.Value / 100,
            VisibilityHotkey = VisibilityPicker.Value,
            ClickThroughHotkey = ClickThroughPicker.Value,
            ScreenshotHotkey = ScreenshotPicker.Value,
            TranslationHotkey = TranslationPicker.Value
        };
        var error = _save(settings);
        if (error is not null)
        {
            ErrorText.Text = error;
            return;
        }

        DialogResult = true;
    }
}
