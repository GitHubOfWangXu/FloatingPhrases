using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FloatingPhrases;

public sealed class DocumentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new OpenModeConverter() }
    };
    private readonly string _filePath;

    public DocumentStore(string? filePath = null) => _filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FloatingPhrases", "documents.json");

    public IReadOnlyList<DocumentShortcut> Load()
    {
        if (!File.Exists(_filePath))
        {
            Save([]);
            return [];
        }
        try
        {
            var documents = JsonSerializer.Deserialize<List<DocumentShortcut?>>(File.ReadAllText(_filePath), JsonOptions)
                ?? throw new JsonException("文档快捷入口文件为空。");
            return Normalize(documents).ToList();
        }
        catch (JsonException)
        {
            File.Move(_filePath, Path.Combine(Path.GetDirectoryName(_filePath)!,
                $"documents.corrupt-{DateTime.Now:yyyyMMdd-HHmmssfff}.json"), true);
            Save([]);
            return [];
        }
    }

    public void Save(IEnumerable<DocumentShortcut?> documents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("文档快捷入口文件路径无效。"));
        File.WriteAllText(_filePath + ".tmp", JsonSerializer.Serialize(Normalize(documents), JsonOptions));
        if (File.Exists(_filePath)) File.Copy(_filePath, _filePath + ".bak", true);
        File.Move(_filePath + ".tmp", _filePath, true);
    }

    public static bool IsMarkdownPath(string? path) => !string.IsNullOrWhiteSpace(path) &&
        (Path.GetExtension(path.Trim()).Equals(".md", StringComparison.OrdinalIgnoreCase) ||
         Path.GetExtension(path.Trim()).Equals(".markdown", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<DocumentShortcut> Normalize(IEnumerable<DocumentShortcut?> documents)
    {
        var seen = new HashSet<Guid>();
        return documents
        .Where(document => document is not null && IsMarkdownPath(document.Path))
        .Select(document => new DocumentShortcut
        {
            Id = document!.Id == Guid.Empty || !seen.Add(document.Id) ? Guid.NewGuid() : document.Id,
            Name = string.IsNullOrWhiteSpace(document.Name) ? Path.GetFileNameWithoutExtension(document.Path.Trim()) : document.Name.Trim(),
            Path = document.Path.Trim(),
            IsPinned = document.IsPinned,
            OpenMode = Enum.IsDefined(document.OpenMode) ? document.OpenMode : DocumentOpenMode.Auto
        });
    }

    private sealed class OpenModeConverter : JsonConverter<DocumentOpenMode>
    {
        public override DocumentOpenMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            DocumentOpenMode mode;
            if (reader.TokenType == JsonTokenType.String && Enum.TryParse(reader.GetString(), true, out mode) && Enum.IsDefined(mode)) return mode;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) && Enum.IsDefined((DocumentOpenMode)number)) return (DocumentOpenMode)number;
            reader.Skip();
            return DocumentOpenMode.Auto;
        }

        public override void Write(Utf8JsonWriter writer, DocumentOpenMode value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}
