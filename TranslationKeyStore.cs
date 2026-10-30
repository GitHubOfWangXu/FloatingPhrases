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
            throw new InvalidOperationException("请填写有效的百炼 API Key，不能包含空白或控制字符。");
    }

    public string? Load()
    {
        if (!File.Exists(_filePath)) return null;
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(_filePath), null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void Save(string key)
    {
        Validate(key);
        var bytes = Encoding.UTF8.GetBytes(key);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tempPath = _filePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, encrypted);
            if (File.Exists(_filePath)) File.Replace(tempPath, _filePath, _filePath + ".bak");
            else File.Move(tempPath, _filePath);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
