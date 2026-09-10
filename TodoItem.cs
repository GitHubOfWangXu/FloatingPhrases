namespace FloatingPhrases;

public sealed record TodoItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Text { get; init; } = "";
    public bool IsCompleted { get; init; }
    public int Progress { get; init; }

    // Old files have only IsCompleted; migrate in memory and persist on the next save.
    public TodoItem Normalize()
    {
        var progress = IsCompleted ? 100 : Math.Clamp(Progress, 0, 100);
        return this with { Text = Text.Trim(), Progress = progress, IsCompleted = progress == 100 };
    }
}
