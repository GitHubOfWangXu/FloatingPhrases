namespace FloatingPhrases;

public enum DocumentOpenMode { Auto, VSCode, Obsidian }

public sealed class DocumentShortcut
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsPinned { get; set; }
    public DocumentOpenMode OpenMode { get; set; } = DocumentOpenMode.Obsidian;
}
