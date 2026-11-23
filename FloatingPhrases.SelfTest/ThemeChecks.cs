using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FloatingPhrases;

internal static class ThemeChecks
{
    public static void Run(string directory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { CheckThemes(directory); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("主题切换检查失败", failure);
        Console.WriteLine("主题检查通过：旧设置兼容、即时切换、正文字重、选择保存与失败保护。");
    }

    private static void CheckThemes(string directory)
    {
        var path = Path.Combine(directory, "theme-settings.json");
        const string legacy = "{\"mode\":\"normal\",\"idleOpacity\":0.61}";
        File.WriteAllText(path, legacy);
        var store = new SettingsStore(path);
        var settings = store.Load();
        Check(settings.Theme == AppTheme.Shadcn && settings.Mode == WindowMode.Normal
            && settings.IdleOpacity == 0.61 && File.ReadAllText(path) == legacy, "旧设置应兼容读取，且不改写原文件");

        var defaults = new WindowSettings();
        Check(settings == (defaults with { Mode = WindowMode.Normal, IdleOpacity = 0.61 }),
            "旧设置应包含四个默认快捷键");
        const string screenshotOnly = """{"mode":"normal","idleOpacity":0.61,"screenshotHotkey":{"modifiers":"control, shift","key":"d3"}}""";
        File.WriteAllText(path, screenshotOnly);
        var customHotkey = new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.D3);
        settings = store.Load();
        Check(settings.ScreenshotHotkey == customHotkey && customHotkey.ToString() == "Ctrl+Shift+3"
            && settings.TranslationHotkey == defaults.TranslationHotkey && File.ReadAllText(path) == screenshotOnly,
            "只有截图快捷键的旧文件应兼容读取，且不改写原文件");
        var invalidHotkey = new Hotkey(ModifierKeys.None, Key.S);
        store.Save(settings with { ScreenshotHotkey = invalidHotkey });
        Check(store.Load().ScreenshotHotkey == defaults.ScreenshotHotkey, "普通字母键不能单独注册为全局截图快捷键");
        settings.VisibilityHotkey = new Hotkey(ModifierKeys.Alt, Key.F8);
        settings.ClickThroughHotkey = new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.F9);
        settings.TranslationHotkey = default;
        store.Save(settings);
        Check(store.Load() == settings, "重启读回应保留自定义与已关闭的快捷键");

        var resources = new ResourceDictionary();
        var body = new TextBlock { Text = "普通正文和 English 123" };
        var title = new TextBlock { Text = "标题", FontWeight = FontWeights.SemiBold };
        var cardContent = new StackPanel();
        cardContent.Children.Add(title);
        cardContent.Children.Add(body);
        var card = new Button { Content = cardContent };
        card.SetResourceReference(FrameworkElement.StyleProperty, "CardButton");
        var combo = new ComboBox { SelectedIndex = 0 };
        combo.Items.Add(new ComboBoxItem { Content = "普通选项" });
        var panel = new StackPanel();
        panel.Children.Add(card);
        panel.Children.Add(combo);
        var view = new UserControl { Resources = resources, Content = panel };
        view.SetResourceReference(FrameworkElement.StyleProperty, typeof(UserControl));
        NameScope.SetNameScope(view, new NameScope());
        var computerTab = new TabItem();
        view.RegisterName("ComputerTab", computerTab);
        panel.Children.Add(computerTab);
        var addEntry = new Button { Content = "+" };
        addEntry.SetResourceReference(FrameworkElement.StyleProperty, "AddEntryButtonStyle");
        panel.Children.Add(addEntry);

        foreach (var theme in new[] { AppTheme.Shadcn, AppTheme.Classic, AppTheme.Shadcn })
        {
            AppThemeManager.SaveAndApply(resources, store, settings, theme);
            view.Measure(new Size(430, 650));
            view.Arrange(new Rect(0, 0, 430, 650));
            view.UpdateLayout();
            Check(body.FontWeight == FontWeights.Normal && title.FontWeight == FontWeights.SemiBold,
                "主题切换不能让正文继承按钮加粗，也不能清除明确设置的标题字重");
            Check(((ComboBoxItem)combo.Items[0]).FontWeight == FontWeights.Normal, "选中下拉项不能自动加粗");
            Check(view.FontFamily.Source == SystemFonts.MessageFontFamily.Source
                && view.FontSize == SystemFonts.MessageFontSize, "两套主题应沿用系统字体和字号");
            Check(store.Load().Theme == theme && settings.Theme == theme && AppThemeManager.Current == theme,
                "当前主题和保存的选择应一致");
            Check(store.Load() == settings, "保存主题不能改变窗口模式、透明度和各功能快捷键");
            var color = ((System.Windows.Media.SolidColorBrush)resources["PrimaryBrush"]).Color;
            Check(color.ToString() == (theme == AppTheme.Classic ? "#FF4462E8" : "#FF18181B"), "现有控件应切换到对应主题资源");
            computerTab.IsSelected = true;
            Check(!addEntry.IsEnabled, "切换主题后电脑页仍应禁用新增入口");
            computerTab.IsSelected = false;
            Check(addEntry.IsEnabled, "离开电脑页后应恢复新增入口");
        }

        var previous = resources.MergedDictionaries[0];
        var blockedPath = Path.Combine(directory, "blocked-settings.json");
        Directory.CreateDirectory(blockedPath);
        try
        {
            AppThemeManager.SaveAndApply(resources, new SettingsStore(blockedPath), settings, AppTheme.Classic);
            throw new InvalidOperationException("保存失败场景未触发");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Check(ReferenceEquals(previous, resources.MergedDictionaries[0]) && settings.Theme == AppTheme.Shadcn
            && AppThemeManager.Current == AppTheme.Shadcn, "保存失败必须保留原主题与原设置");
        Check(File.Exists(path + ".bak"), "主题保存应保留原设置的恢复副本");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
