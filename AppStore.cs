using System.IO;
using System.Text.Json;

namespace FloatingPhrases;

public sealed class AppStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;

    public AppStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FloatingPhrases",
            "apps.json");
    }

    public IReadOnlyList<AppShortcut> Load()
    {
        if (!File.Exists(_filePath))
        {
            Save([]);
            return [];
        }

        try
        {
            var apps = JsonSerializer.Deserialize<List<AppShortcut?>>(File.ReadAllText(_filePath), JsonOptions)
                ?? throw new JsonException("软件快捷入口文件为空。");

            return apps
                .Where(app => app is not null && !string.IsNullOrWhiteSpace(app.Path))
                .Select(app => Normalize(app!))
                .ToList();
        }
        catch (JsonException)
        {
            var corruptPath = Path.Combine(
                Path.GetDirectoryName(_filePath)!,
                $"apps.corrupt-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
            File.Move(_filePath, corruptPath, true);
            Save([]);
            return [];
        }
    }

    public void Save(IEnumerable<AppShortcut> apps)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("软件快捷入口文件路径无效。");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(apps.Select(Normalize), JsonOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(_filePath))
        {
            File.Copy(_filePath, _filePath + ".bak", true);
        }

        File.Move(tempPath, _filePath, true);
    }

    private static AppShortcut Normalize(AppShortcut app) => new()
    {
        Id = app.Id == Guid.Empty ? Guid.NewGuid() : app.Id,
        Name = string.IsNullOrWhiteSpace(app.Name) ? Path.GetFileNameWithoutExtension(app.Path) : app.Name.Trim(),
        Path = app.Path.Trim(),
        Category = AppCategories.Normalize(app.Category),
        IsPinned = app.IsPinned
    };
}
