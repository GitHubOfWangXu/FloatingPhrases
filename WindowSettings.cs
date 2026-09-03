namespace FloatingPhrases;

public enum WindowMode
{
    Normal,
    Floating,
    ClickThrough
}

public sealed class WindowSettings
{
    public WindowMode Mode { get; set; } = WindowMode.Floating;
    public double IdleOpacity { get; set; } = 0.35;
}
