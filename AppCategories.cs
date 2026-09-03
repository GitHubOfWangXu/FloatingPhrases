namespace FloatingPhrases;

public static class AppCategories
{
    public const string Development = "开发工具";
    public const string Office = "办公沟通";
    public const string Browsing = "浏览阅读";
    public const string FilesAndNetwork = "文件网络";
    public const string Remote = "远程连接";
    public const string System = "系统工具";
    public const string Other = "其他";

    public static IReadOnlyList<string> All { get; } =
    [
        Development,
        Office,
        Browsing,
        FilesAndNetwork,
        Remote,
        System,
        Other
    ];

    public static string Normalize(string? category) =>
        All.Contains(category, StringComparer.Ordinal) ? category! : Other;

    public static string Infer(string name, string targetPath)
    {
        var value = $"{name} {targetPath}".ToLowerInvariant();
        if (ContainsAny(value, "apifox", "dbeaver", "docker", "finalshell", "git", "idea", "intellij",
                "mqttx", "notepad++", "redis", "reqable", "visual studio", "vscode", "winmerge", "zcode"))
        {
            return Development;
        }

        if (ContainsAny(value, "飞书", "微信", "weixin", "mailmaster", "邮箱", "wps", "doubaowork", "豆包工作"))
        {
            return Office;
        }

        if (ContainsAny(value, "chrome", "edge", "quark", "夸克", "obsidian", "z-library", "doubao", "豆包"))
        {
            return Browsing;
        }

        if (ContainsAny(value, "baidu", "网盘", "bili23", "clash", "localsend", "omniget", "v2ray"))
        {
            return FilesAndNetwork;
        }

        if (ContainsAny(value, "todesk", "gameviewer", "uu远程", "cloud computer", "云电脑", "运维客户端"))
        {
            return Remote;
        }

        if (ContainsAny(value, "bandizip", "cc switch", "cc-switch", "电脑管家", "qqpc", "桌面整理", "desktopmgr"))
        {
            return System;
        }

        return Other;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.Ordinal));
}
