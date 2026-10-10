using System.IO;
using System.Text.Json;

namespace FloatingPhrases;

public sealed class CommandStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        IgnoreReadOnlyProperties = true
    };
    private readonly string _filePath;

    public CommandStore(string? filePath = null) => _filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FloatingPhrases", "commands.json");

    public IReadOnlyList<CommandShortcut> Load()
    {
        if (!File.Exists(_filePath)) return [];
        try
        {
            var commands = JsonSerializer.Deserialize<List<CommandShortcut?>>(File.ReadAllText(_filePath), JsonOptions)
                ?? throw new JsonException("命令文件为空。");
            return commands.Where(command => command is not null && !string.IsNullOrWhiteSpace(command.Text))
                .Select(command => Normalize(command!)).ToList();
        }
        catch (JsonException)
        {
            File.Move(_filePath, Path.Combine(Path.GetDirectoryName(_filePath)!,
                $"commands.corrupt-{DateTime.Now:yyyyMMdd-HHmmssfff}.json"), true);
            Save([]);
            return [];
        }
    }

    public void Save(IEnumerable<CommandShortcut> commands)
    {
        var items = commands.Select(Normalize).ToList();
        if (items.Any(command => string.IsNullOrWhiteSpace(command.Text)))
            throw new ArgumentException("命令内容不能为空。", nameof(commands));
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath + ".tmp", JsonSerializer.Serialize(items, JsonOptions));
        if (File.Exists(_filePath)) File.Copy(_filePath, _filePath + ".bak", true);
        File.Move(_filePath + ".tmp", _filePath, true);
    }

    private static CommandShortcut Normalize(CommandShortcut command) => command with
    {
        Id = command.Id == Guid.Empty ? Guid.NewGuid() : command.Id,
        Name = string.IsNullOrWhiteSpace(command.Name) ? "未命名命令" : command.Name.Trim(),
        WorkingDirectory = (command.WorkingDirectory ?? "").Trim()
    };
}
