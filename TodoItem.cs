namespace FloatingPhrases;

public sealed record TodoItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Text { get; init; } = "";
    public bool IsCompleted { get; init; }
}
