using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FloatingPhrases;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _filePath;

    public SettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FloatingPhrases",
            "settings.json");
    }

    public WindowSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            var defaults = new WindowSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<WindowSettings>(
                File.ReadAllText(_filePath), JsonOptions)
                ?? throw new JsonException("设置文件为空。");
            return Normalize(settings);
        }
        catch (JsonException)
        {
            var corruptPath = Path.Combine(
                Path.GetDirectoryName(_filePath)!,
                $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
            File.Move(_filePath, corruptPath, true);

            var defaults = new WindowSettings();
            Save(defaults);
            return defaults;
        }
    }

    public void Save(WindowSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("设置文件路径无效。");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(Normalize(settings), JsonOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(_filePath))
        {
            File.Copy(_filePath, _filePath + ".bak", true);
        }

        File.Move(tempPath, _filePath, true);
    }

    private static WindowSettings Normalize(WindowSettings settings) => new()
    {
        Mode = Enum.IsDefined(settings.Mode) ? settings.Mode : WindowMode.Floating,
        IdleOpacity = double.IsFinite(settings.IdleOpacity)
            ? Math.Clamp(settings.IdleOpacity, 0.2, 0.9)
            : 0.35
    };
}
