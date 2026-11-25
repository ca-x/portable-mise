namespace PortableMise.Services.Networking;

public static class BinaryPairDeployment
{
    public static void Apply(string stagedDirectory, string binDirectory, Action<string>? log = null)
    {
        Directory.CreateDirectory(binDirectory);
        string transaction = Guid.NewGuid().ToString("N");
        var applied = new List<(string Target, string? Backup)>();
        var temporary = new List<string>();
        bool completed = false;
        try
        {
            foreach (string name in new[] { "mise.exe", "mise-shim.exe" })
            {
                string target = Path.Combine(binDirectory, name);
                string temp = Path.Combine(binDirectory, ".mise-new-" + transaction + "-" + name);
                temporary.Add(temp);
                File.Copy(Path.Combine(stagedDirectory, name), temp, false);
                string? backup = File.Exists(target) ? Path.Combine(binDirectory, ".mise-previous-" + transaction + "-" + name) : null;
                if (backup == null) File.Move(temp, target);
                else File.Replace(temp, target, backup);
                applied.Add((target, backup));
            }
            completed = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var item in applied.AsEnumerable().Reverse())
            {
                try
                {
                    if (item.Backup == null) File.Delete(item.Target);
                    else File.Replace(item.Backup, item.Target, null);
                }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException) { rollbackErrors.Add(rollback); }
            }
            if (rollbackErrors.Count != 0)
                throw new IOException("安装未完成，部分原件仍保留为 .mise-previous 备份，请关闭正在使用 mise 的进程后恢复。", new AggregateException(new[] { error }.Concat(rollbackErrors)));
            throw new InvalidOperationException("mise 安装未完成，原有程序已保留；请关闭正在使用 mise 的进程并检查目录权限。", error);
        }
        finally
        {
            foreach (string path in temporary) DeleteIfPossible(path, log);
            if (completed) foreach (var item in applied) if (item.Backup != null) DeleteIfPossible(item.Backup, log);
        }
    }

    private static void DeleteIfPossible(string path, Action<string>? log)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { log?.Invoke("仍在使用的安装临时文件已保留，可稍后清理。"); }
    }
}
