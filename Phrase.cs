namespace FloatingPhrases;

public sealed class Phrase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Group { get; set; } = "常用";
    public string Text { get; set; } = "";
    public bool IsPinned { get; set; }
}
