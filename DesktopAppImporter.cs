using System.IO;
using System.Runtime.InteropServices;

namespace FloatingPhrases;

public static class DesktopAppImporter
{
    private static readonly HashSet<string> LaunchableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".msi"
    };

    public static IReadOnlyList<AppShortcut> FindApps()
    {
        var results = new List<AppShortcut>();
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        dynamic? shell = shellType is null ? null : Activator.CreateInstance(shellType);

        try
        {
            foreach (var desktop in GetDesktopDirectories().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(desktop))
                {
                    continue;
                }

                foreach (var path in Directory.EnumerateFiles(desktop, "*", SearchOption.TopDirectoryOnly))
                {
                    var extension = Path.GetExtension(path);
                    if (LaunchableExtensions.Contains(extension))
                    {
                        AddResult(results, path, path);
                    }
                    else if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && shell is not null)
                    {
                        dynamic shortcut = shell.CreateShortcut(path);
                        string targetPath = shortcut.TargetPath;
                        if (File.Exists(targetPath) && LaunchableExtensions.Contains(Path.GetExtension(targetPath)))
                        {
                            AddResult(results, path, targetPath);
                        }

                        Marshal.FinalReleaseComObject(shortcut);
                    }
                }
            }
        }
        finally
        {
            if (shell is not null)
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        return results
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> GetDesktopDirectories()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    }

    private static void AddResult(ICollection<AppShortcut> results, string launchPath, string targetPath)
    {
        var name = Path.GetFileNameWithoutExtension(launchPath)
            .Replace(" - 快捷方式", "", StringComparison.CurrentCultureIgnoreCase);
        results.Add(new AppShortcut
        {
            Name = name,
            Path = launchPath,
            Category = AppCategories.Infer(name, targetPath)
        });
    }
}
