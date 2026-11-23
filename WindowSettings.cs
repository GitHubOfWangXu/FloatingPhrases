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

public sealed record WindowSettings
{
    public WindowMode Mode { get; set; } = WindowMode.Floating;
    public double IdleOpacity { get; set; } = 0.35;
    public AppTheme Theme { get; set; } = AppTheme.Shadcn;
    public ScreenshotHotkey ScreenshotHotkey { get; set; } = ScreenshotHotkey.Default;
}
