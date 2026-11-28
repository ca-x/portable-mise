using PortableMise.Localization;

namespace PortableMise.Models;

public enum ToolStatus
{
    /// <summary>Currently selected by mise for the session environment.</summary>
    Active,
    /// <summary>Installed but not the active version for this tool.</summary>
    Installed,
}

public sealed record ToolInfo(string Name, string Version, bool Active, bool Installed, string Requested = "")
{
    public ToolStatus Status => Active ? ToolStatus.Active : ToolStatus.Installed;
}
public sealed record ToolInstallRequest(string Specification, IReadOnlyDictionary<string, string>? Options = null);
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public void EnsureSuccess()
    {
        if (ExitCode != 0) throw new InvalidOperationException(UiText.F("mise 命令退出码为 {0}，请查看操作日志。", ExitCode));
    }
}
public sealed record AppRelease(string Version, string AssetName, Uri DownloadUrl, Uri ChecksumsUrl);
public sealed record StagedAppUpdate(string FilePath, string Sha256, string Version);
