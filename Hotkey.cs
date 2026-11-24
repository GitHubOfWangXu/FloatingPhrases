using System.Windows.Input;

namespace FloatingPhrases;

public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public bool IsEnabled() => Key != Key.None;

    public static bool SupportsKey(Key key) =>
        key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.F1 and <= Key.F11 or Key.Space;

    public bool IsValid() => this == default
        || SupportsKey(Key)
        && (Modifiers & ~(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) == 0
        && ((Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0 || Key is >= Key.F1 and <= Key.F11);

    public static string KeyLabel(Key key) =>
        key switch
        {
            Key.None => "关闭",
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            _ => key.ToString()
        };

    public override string ToString() =>
        (Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "")
        + (Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "")
        + (Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : "")
        + KeyLabel(Key);
}
