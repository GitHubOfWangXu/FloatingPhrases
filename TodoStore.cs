using System.IO;
using System.Text.Json;

namespace FloatingPhrases;

public sealed class TodoStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private readonly string _filePath;

    public TodoStore(string? filePath = null) => _filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FloatingPhrases", "todos.json");

    public IReadOnlyList<TodoItem> Load()
    {
        if (!File.Exists(_filePath)) return [];
        try
        {
            var items = JsonSerializer.Deserialize<List<TodoItem?>>(File.ReadAllText(_filePath), JsonOptions)
                ?? throw new JsonException("待办文件为空。");
            var seen = new HashSet<Guid>();
            return items.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Text))
                .Select(item => item!.Normalize() with
                {
                    Id = item!.Id == Guid.Empty || !seen.Add(item.Id) ? Guid.NewGuid() : item.Id
                }).ToList();
        }
        catch (JsonException)
        {
            File.Move(_filePath, Path.Combine(Path.GetDirectoryName(_filePath)!,
                $"todos.corrupt-{DateTime.Now:yyyyMMdd-HHmmssfff}.json"), true);
            Save([]);
            return [];
        }
    }

    public void Save(IEnumerable<TodoItem> items)
    {
        var normalized = items.Select(item => item.Normalize()).ToList();
        if (normalized.Any(item => string.IsNullOrWhiteSpace(item.Text)))
            throw new ArgumentException("待办内容不能为空。", nameof(items));
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(normalized, JsonOptions));
        if (File.Exists(_filePath)) File.Copy(_filePath, _filePath + ".bak", true);
        File.Move(tempPath, _filePath, true);
    }
}
