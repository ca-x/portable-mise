namespace PortableMise.Models;

public sealed record ToolInfo(string Name, string Version, bool Active, bool Installed, string Requested = "");
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public void EnsureSuccess()
    {
        if (ExitCode != 0) throw new InvalidOperationException($"mise 命令退出码为 {ExitCode}，请查看操作日志。");
    }
}
public sealed record AppRelease(string Version, string AssetName, Uri DownloadUrl, Uri ChecksumsUrl);
public sealed record StagedAppUpdate(string FilePath, string Sha256, string Version);
