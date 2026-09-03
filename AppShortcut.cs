namespace FloatingPhrases;

public sealed class AppShortcut
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Category { get; set; } = AppCategories.Other;
    public bool IsPinned { get; set; }
}
