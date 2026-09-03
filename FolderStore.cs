using System.IO;
using System.Text.Json;

namespace FloatingPhrases;

public sealed class FolderStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;

    public FolderStore(string? filePath = null)
    {
        _filePath = filePath ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FloatingPhrases",
            "folders.json");
    }

    public IReadOnlyList<FolderShortcut> Load()
    {
        if (!File.Exists(_filePath))
        {
            Save([]);
            return [];
        }

        try
        {
            var folders = JsonSerializer.Deserialize<List<FolderShortcut?>>(
                File.ReadAllText(_filePath), JsonOptions)
                ?? throw new JsonException("文件夹快捷入口文件为空。");

            return folders
                .Where(folder => folder is not null && !string.IsNullOrWhiteSpace(folder.Path))
                .Select(folder => Normalize(folder!))
                .ToList();
        }
        catch (JsonException)
        {
            var corruptPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(_filePath)!,
                $"folders.corrupt-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
            File.Move(_filePath, corruptPath, true);
            Save([]);
            return [];
        }
    }

    public void Save(IEnumerable<FolderShortcut> folders)
    {
        var directory = System.IO.Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("文件夹快捷入口文件路径无效。");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(folders.Select(Normalize), JsonOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(_filePath))
        {
            File.Copy(_filePath, _filePath + ".bak", true);
        }

        File.Move(tempPath, _filePath, true);
    }

    private static FolderShortcut Normalize(FolderShortcut folder) => new()
    {
        Id = folder.Id == Guid.Empty ? Guid.NewGuid() : folder.Id,
        Name = string.IsNullOrWhiteSpace(folder.Name) ? folder.Path : folder.Name.Trim(),
        Path = folder.Path.Trim(),
        IsPinned = folder.IsPinned
    };
}
