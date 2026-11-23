using System.Windows.Input;

namespace FloatingPhrases;

public enum WindowMode
{
    Normal,
    Floating,
    ClickThrough
}

public enum AppTheme
{
    Shadcn,
    Classic
}

internal enum HotkeyAction
{
    ToggleVisibility,
    ToggleClickThrough,
    Screenshot,
    Translate
}

public sealed record WindowSettings
{
    public WindowMode Mode { get; set; } = WindowMode.Floating;
    public double IdleOpacity { get; set; } = 0.35;
    public AppTheme Theme { get; set; } = AppTheme.Shadcn;
    public Hotkey VisibilityHotkey { get; set; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.Space);
    public Hotkey ClickThroughHotkey { get; set; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.T);
    public Hotkey ScreenshotHotkey { get; set; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.S);
    public Hotkey TranslationHotkey { get; set; } = new(ModifierKeys.Control, Key.T);

    internal (HotkeyAction Action, string Label, Hotkey Key)[] Shortcuts() =>
    [
        (HotkeyAction.ToggleVisibility, "显示 / 隐藏", VisibilityHotkey),
        (HotkeyAction.ToggleClickThrough, "鼠标穿透", ClickThroughHotkey),
        (HotkeyAction.Screenshot, "截图", ScreenshotHotkey),
        (HotkeyAction.Translate, "翻译剪贴板", TranslationHotkey)
    ];
}
