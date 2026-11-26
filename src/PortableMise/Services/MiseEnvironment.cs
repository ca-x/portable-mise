using System.Diagnostics;
using PortableMise.Models;

using PortableMise.Localization;

namespace PortableMise.Services;

public static class MiseEnvironment
{
    public static void ValidatePathEntries(AppPaths paths)
    {
        if (paths.MiseBinDirectory.Contains(Path.PathSeparator) || paths.ShimsDirectory.Contains(Path.PathSeparator))
            throw new InvalidOperationException(UiText.T("mise 目录或工具目录不能包含 PATH 分隔符（;），请选择不含分号的目录。"));
    }

    public static IReadOnlyDictionary<string, string> Variables(AppPaths paths) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["MISE_DATA_DIR"] = paths.ToolsRoot,
        ["MISE_CONFIG_DIR"] = paths.ConfigDirectory,
        ["MISE_CACHE_DIR"] = paths.CacheDirectory,
        ["MISE_STATE_DIR"] = paths.StateDirectory,
        ["MISE_SHIMS_DIR"] = paths.ShimsDirectory,
        ["MISE_GLOBAL_CONFIG_FILE"] = Path.Combine(paths.ConfigDirectory, "config.toml")
    };

    public static void Apply(ProcessStartInfo start, AppSettings settings, AppPaths paths)
    {
        ValidatePathEntries(paths);
        foreach (var pair in Variables(paths)) start.Environment[pair.Key] = pair.Value;
        foreach (var pair in ProxySupport.GetProcessEnvironment(settings))
        {
            if (pair.Value is null) start.Environment.Remove(pair.Key);
            else start.Environment[pair.Key] = pair.Value;
        }
        start.Environment["NO_COLOR"] = "1";
        start.Environment["MISE_COLOR"] = "0";
        start.Environment["MISE_SELF_UPDATE_AUTO"] = "0";
        start.Environment.TryGetValue("PATH", out string? inheritedPath);
        start.Environment["PATH"] = paths.MiseBinDirectory + Path.PathSeparator + paths.ShimsDirectory + Path.PathSeparator + inheritedPath;
    }

    public static void EnsureDirectories(AppPaths paths)
    {
        foreach (string directory in new[] { paths.ConfigDirectory, paths.CacheDirectory, paths.StateDirectory, paths.ToolsRoot, paths.ShimsDirectory })
            Directory.CreateDirectory(directory);
    }
}
