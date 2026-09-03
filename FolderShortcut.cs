namespace FloatingPhrases;

public sealed class FolderShortcut
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsPinned { get; set; }
}
