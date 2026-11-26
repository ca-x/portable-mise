using PortableMise.Localization;

namespace PortableMise.Models;

public sealed record AppPaths(string AppDirectory, string MiseRoot, string ToolsRoot)
{
    public string MiseBinDirectory => Path.Combine(MiseRoot, "bin");
    public string MiseExecutable => Path.Combine(MiseBinDirectory, "mise.exe");
    public string MiseShimExecutable => Path.Combine(MiseBinDirectory, "mise-shim.exe");
    public string ShimsDirectory => Path.Combine(ToolsRoot, "shims");
    public string ConfigDirectory => Path.Combine(AppDirectory, "data", "config");
    public string CacheDirectory => Path.Combine(AppDirectory, "data", "cache");
    public string StateDirectory => Path.Combine(AppDirectory, "data", "state");
    public string UpdatesDirectory => Path.Combine(AppDirectory, "data", "updates");
    public string SettingsPath => Path.Combine(AppDirectory, "portable-mise.json");

    public static AppPaths Resolve(AppSettings settings, string? appDirectory = null)
    {
        string root = Path.GetFullPath(appDirectory ?? AppContext.BaseDirectory);
        return new AppPaths(root, ResolveDirectory(settings.MiseDirectory, root), ResolveDirectory(settings.ToolsDirectory, root));
    }

    private static string ResolveDirectory(string value, string root)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(UiText.T("目录不能为空。"));
        if ((Path.IsPathRooted(value) && !Path.IsPathFullyQualified(value)) || (!Path.IsPathRooted(value) && value.Contains(':')))
            throw new InvalidOperationException(UiText.T("请使用完整目录或相对程序目录的路径，不支持盘符相对目录。"));
        return Path.GetFullPath(value, root);
    }

    public static string PortablePath(string selectedDirectory, string appDirectory)
    {
        string relative = Path.GetRelativePath(appDirectory, Path.GetFullPath(selectedDirectory));
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative) ? relative : Path.GetFullPath(selectedDirectory);
    }
}
