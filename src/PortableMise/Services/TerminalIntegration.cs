using System.Diagnostics;
using PortableMise.Models;

namespace PortableMise.Services;

public sealed class TerminalIntegration(string appDirectory, IMiseService mise, IUserEnvironmentStore? environment = null) : ITerminalIntegration
{
    private readonly string root = Path.GetFullPath(appDirectory);
    private readonly IUserEnvironmentStore environment = environment ?? new UserEnvironmentStore();

    private static string Normalize(string value) => value.Trim().Trim('"').TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public bool IsConfigured(AppSettings settings)
    {
        var paths = AppPaths.Resolve(settings, root);
        if (!File.Exists(paths.MiseExecutable) || !File.Exists(paths.MiseShimExecutable)) return false;
        var entries = (environment.Read("Path") ?? "").Split(';').Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return entries.Contains(Normalize(paths.MiseBinDirectory)) && entries.Contains(Normalize(paths.ShimsDirectory)) &&
            MiseEnvironment.Variables(paths).All(pair => string.Equals(environment.Read(pair.Key), pair.Value, StringComparison.OrdinalIgnoreCase));
    }

    public async Task RepairAsync(AppSettings settings, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        var paths = AppPaths.Resolve(settings, root);
        MiseEnvironment.ValidatePathEntries(paths);
        if (!File.Exists(paths.MiseExecutable) || !File.Exists(paths.MiseShimExecutable))
            throw new InvalidOperationException("需要 mise.exe 和 mise-shim.exe，请先完成一键安装。");
        MiseEnvironment.EnsureDirectories(paths);
        var result = await mise.RunAsync(settings, new[] { "reshim" }, log, cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess();
        cancellationToken.ThrowIfCancellationRequested();
        string[] managed = [paths.MiseBinDirectory, paths.ShimsDirectory];
        var remove = settings.LastRegisteredPaths.Concat(managed).Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] preserved = (environment.Read("Path") ?? "").Split(';')
            .Where(value => value.Length != 0 && !remove.Contains(Normalize(value))).ToArray();
        var desired = MiseEnvironment.Variables(paths).ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.OrdinalIgnoreCase);
        desired["Path"] = string.Join(';', managed.Concat(preserved));
        var previous = desired.Keys.ToDictionary(name => name, environment.Read, StringComparer.OrdinalIgnoreCase);
        var written = new List<string>();
        try
        {
            foreach (var pair in desired)
            {
                environment.Write(pair.Key, pair.Value);
                written.Add(pair.Key);
            }
        }
        catch (Exception failure)
        {
            var rollbackFailures = new List<Exception>();
            foreach (string name in written.AsEnumerable().Reverse())
                try { environment.Write(name, previous[name]); }
                catch (Exception error) { rollbackFailures.Add(error); }
            if (rollbackFailures.Count != 0) throw new AggregateException("终端设置写入失败，部分旧值也未能恢复，请检查用户环境变量。", new[] { failure }.Concat(rollbackFailures));
            throw;
        }
        settings.LastRegisteredPaths = [.. managed];
        environment.Broadcast();
        log?.Invoke("终端环境已恢复。重新打开终端后可使用 mise 和工具命令。");
    }

    public void OpenTerminal(AppSettings settings)
    {
        var paths = AppPaths.Resolve(settings, root);
        if (!File.Exists(paths.MiseExecutable)) throw new InvalidOperationException("请先安装 mise。");
        MiseEnvironment.EnsureDirectories(paths);
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, WorkingDirectory = paths.AppDirectory };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoExit");
        MiseEnvironment.Apply(start, settings, paths);
        _ = Process.Start(start) ?? throw new InvalidOperationException("无法打开 PowerShell。");
    }
}
