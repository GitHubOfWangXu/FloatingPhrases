namespace FloatingPhrases;

public enum CommandShell { PowerShell, Cmd }

public sealed record CommandShortcut
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Text { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public CommandShell Shell { get; init; }
    public string ShellName => Shell switch { CommandShell.PowerShell => "PowerShell", CommandShell.Cmd => "CMD", _ => "未知终端" };
}
