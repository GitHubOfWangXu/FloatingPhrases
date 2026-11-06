using System.Windows;

namespace FloatingPhrases;

public static class AppThemeManager
{
    public static AppTheme Current { get; private set; } = AppTheme.Shadcn;

    private static ResourceDictionary Load(AppTheme theme) => new()
    {
        Source = new Uri($"/FloatingPhrases;component/Themes/{(theme == AppTheme.Classic ? "Classic" : "Shadcn")}.xaml", UriKind.Relative)
    };

    public static void Apply(ResourceDictionary resources, AppTheme theme) =>
        Replace(resources, Load(theme), theme);

    public static void SaveAndApply(ResourceDictionary resources, SettingsStore store, WindowSettings settings, AppTheme theme)
    {
        var dictionary = Load(theme);
        store.Save(new WindowSettings { Mode = settings.Mode, IdleOpacity = settings.IdleOpacity, Theme = theme });
        Replace(resources, dictionary, theme);
        settings.Theme = theme;
    }

    private static void Replace(ResourceDictionary resources, ResourceDictionary dictionary, AppTheme theme)
    {
        // All switchable resources live in this one application-level dictionary.
        resources.MergedDictionaries.Clear();
        resources.MergedDictionaries.Add(dictionary);
        Current = theme;
    }
}
