using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FloatingPhrases;

public sealed class TranslationKeyStore
{
    private readonly string _filePath;

    public TranslationKeyStore(string? filePath = null) => _filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FloatingPhrases", "translation-key.bin");

    public static void Validate(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 512 || key.Any(c => c <= ' ' || c >= 127))
            throw new InvalidOperationException("请填写所选服务的有效 API Key，不能包含空白或控制字符。");
    }

    private string KeyPath(TranslationProvider provider) => provider switch
    {
        TranslationProvider.Qwen => _filePath,
        TranslationProvider.GlmCoding => _filePath + ".glm-coding",
        TranslationProvider.SiliconFlowHunyuan or TranslationProvider.SiliconFlowGlm or TranslationProvider.SiliconFlowQwen => _filePath + ".siliconflow",
        _ => throw new InvalidOperationException("翻译服务无效，未读取或修改密钥。")
    };

    public string? Load(TranslationProvider provider = TranslationProvider.Qwen) => ReadValue(KeyPath(provider));

    public void Save(string key, TranslationProvider provider = TranslationProvider.Qwen) => WriteValue(KeyPath(provider), key);

    public TranslationProvider LoadProvider()
    {
        var value = ReadValue(_filePath + ".provider");
        if (value is null) return TranslationProvider.Qwen;
        if (Enum.TryParse<TranslationProvider>(value, out var provider) && Enum.IsDefined(provider)) return provider;
        throw new InvalidOperationException("保存的翻译服务无效，原文件保留，请重新选择服务。");
    }

    public void SaveProvider(TranslationProvider provider)
    {
        _ = KeyPath(provider);
        WriteValue(_filePath + ".provider", provider.ToString());
    }

    private static string? ReadValue(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(filePath), null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static void WriteValue(string filePath, string key)
    {
        Validate(key);
        var bytes = Encoding.UTF8.GetBytes(key);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var tempPath = filePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, encrypted);
            if (File.Exists(filePath)) File.Replace(tempPath, filePath, filePath + ".bak");
            else File.Move(tempPath, filePath);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
