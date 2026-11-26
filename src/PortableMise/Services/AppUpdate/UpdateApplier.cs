using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;

using PortableMise.Localization;

namespace PortableMise.Services.AppUpdate;

public static class UpdateApplier
{
    public static bool IsHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    public static void ValidateStagedFile(string stagedFile, string targetFile, string expectedHash)
    {
        string target = Path.GetFullPath(targetFile);
        string staged = Path.GetFullPath(stagedFile);
        string root = Path.GetDirectoryName(target) ?? throw new InvalidOperationException(UiText.T("无效的程序位置。"));
        string updateRoot = Path.Combine(root, "data", "updates") + Path.DirectorySeparatorChar;
        if (!Path.GetFileName(target).StartsWith("PortableMise", StringComparison.OrdinalIgnoreCase) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !staged.StartsWith(updateRoot, StringComparison.OrdinalIgnoreCase) || !staged.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || staged.Equals(target, StringComparison.OrdinalIgnoreCase) ||
            !IsHash(expectedHash) || !File.Exists(staged))
            throw new InvalidOperationException(UiText.T("程序更新路径或校验值无效。"));
        using var file = File.OpenRead(staged);
        string actual = Convert.ToHexString(SHA256.HashData(file));
        if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(UiText.T("待更新程序校验失败，未修改当前程序。"));
    }

    public static string ApplyVerifiedFile(string stagedFile, string targetFile, string expectedHash, string? expectedTargetHash = null)
    {
        ValidateStagedFile(stagedFile, targetFile, expectedHash);
        string target = Path.GetFullPath(targetFile);
        string root = Path.GetDirectoryName(target)!;
        string temporary = Path.Combine(root, ".portable-mise-" + Guid.NewGuid().ToString("N") + ".tmp");
        string rollback = Path.Combine(root, "data", "updates", "rollback", Guid.NewGuid().ToString("N"));
        string backup = Path.Combine(rollback, Path.GetFileName(target));
        try
        {
            File.Copy(stagedFile, temporary, false);
            using (var stream = File.OpenRead(temporary))
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(UiText.T("更新临时文件校验失败。"));
            if (expectedTargetHash != null)
            {
                if (!File.Exists(target)) throw new InvalidOperationException(UiText.T("原程序文件已变化，停止自动替换。"));
                using var current = File.OpenRead(target);
                if (!IsHash(expectedTargetHash) || !Convert.ToHexString(SHA256.HashData(current)).Equals(expectedTargetHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(UiText.T("原程序文件已更新或修改，停止替换。"));
            }
            Directory.CreateDirectory(rollback);
            if (File.Exists(target)) File.Replace(temporary, target, backup);
            else File.Move(temporary, target);
            return backup;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task<bool> TryRunAsync(string[] args, Action<string>? log)
    {
        if (args.Length == 0 || args[0] != "--apply-update") return false;
        if (args.Length != 11) throw new InvalidOperationException(UiText.T("程序更新助手参数不完整。"));
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 1; index < args.Length; index += 2)
            if (!values.TryAdd(args[index], args[index + 1])) throw new InvalidOperationException(UiText.T("程序更新助手参数重复。"));
        if (values.Count != 5 || !values.TryGetValue("--target", out string? target) || !values.TryGetValue("--parent-pid", out string? pidText) ||
            !values.TryGetValue("--parent-start", out string? startText) || !values.TryGetValue("--sha256", out string? sha) ||
            !values.TryGetValue("--target-sha256", out string? targetHash) || !IsHash(targetHash) ||
            !int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out int pid) ||
            !long.TryParse(startText, NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) || pid <= 0 || pid == Environment.ProcessId)
            throw new InvalidOperationException(UiText.T("程序更新助手参数无效。"));
        string staged = Environment.ProcessPath ?? throw new InvalidOperationException(UiText.T("无法确定更新程序位置。"));
        ValidateStagedFile(staged, target, sha);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        Process? parent = null;
        try { parent = Process.GetProcessById(pid); }
        catch (ArgumentException) { }
        using (parent)
        {
            if (parent != null)
            {
                try
                {
                    if (!parent.HasExited)
                    {
                        if (parent.StartTime.ToUniversalTime().Ticks != ticks ||
                            !Path.GetFullPath(parent.MainModule?.FileName ?? string.Empty).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException(UiText.T("程序更新助手的原进程身份不匹配。"));
                        await parent.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    }
                }
                catch (InvalidOperationException) when (parent.HasExited) { }
                catch (System.ComponentModel.Win32Exception) when (parent.HasExited) { }
            }
        }
        ApplyVerifiedFile(staged, target, sha, targetHash);
        log?.Invoke(UiText.T("桌面程序已替换，正在重新启动。"));
        using var restarted = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target)! })
            ?? throw new InvalidOperationException(UiText.T("程序已更新，请手动重新启动。"));
        return true;
    }
}
