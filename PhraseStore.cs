using System.IO;
using System.Text.Json;

namespace FloatingPhrases;

public sealed class PhraseStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly bool _seedDefaults;

    public PhraseStore(string? filePath = null, bool seedDefaults = true)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FloatingPhrases",
            "phrases.json");
        _seedDefaults = seedDefaults;
    }

    public IReadOnlyList<Phrase> Load()
    {
        if (!File.Exists(_filePath))
        {
            var defaults = CreateDefaults();
            Save(defaults);
            return defaults;
        }

        try
        {
            var phrases = JsonSerializer.Deserialize<List<Phrase?>>(
                File.ReadAllText(_filePath), JsonOptions);

            if (phrases is null)
            {
                throw new JsonException("短语文件为空。");
            }

            return phrases
                .Where(phrase => phrase is not null && !string.IsNullOrWhiteSpace(phrase.Text))
                .Select(phrase => Normalize(phrase!))
                .ToList();
        }
        catch (JsonException)
        {
            var corruptPath = Path.Combine(
                Path.GetDirectoryName(_filePath)!,
                $"{Path.GetFileNameWithoutExtension(_filePath)}.corrupt-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
            File.Move(_filePath, corruptPath, true);

            var defaults = CreateDefaults();
            Save(defaults);
            return defaults;
        }
    }

    public void Save(IEnumerable<Phrase> phrases)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("短语文件路径无效。");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(phrases.Select(Normalize), JsonOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(_filePath))
        {
            File.Copy(_filePath, _filePath + ".bak", true);
        }

        File.Move(tempPath, _filePath, true);
    }

    private static Phrase Normalize(Phrase phrase) => new()
    {
        Id = phrase.Id == Guid.Empty ? Guid.NewGuid() : phrase.Id,
        Title = string.IsNullOrWhiteSpace(phrase.Title)
            ? phrase.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "未命名"
            : phrase.Title.Trim(),
        Group = string.IsNullOrWhiteSpace(phrase.Group) ? "常用" : phrase.Group.Trim(),
        Text = phrase.Text,
        IsPinned = phrase.IsPinned
    };

    private List<Phrase> CreateDefaults() => _seedDefaults
        ? [
            new() { Title = "收到", Group = "常用", Text = "收到，我马上处理。", IsPinned = true },
            new() { Title = "感谢反馈", Group = "常用", Text = "您好，感谢您的反馈，我们已经记录，会尽快给您答复。" },
            new() { Title = "稍后回复", Group = "常用", Text = "我正在确认相关信息，稍后给您回复。" }
        ]
        : [];
}
