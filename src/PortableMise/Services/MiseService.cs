using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PortableMise.Models;

using PortableMise.Localization;

namespace PortableMise.Services;

public sealed class MiseService(string appDirectory) : IMiseService
{
    private readonly string root = Path.GetFullPath(appDirectory);

    public ProcessStartInfo CreateStartInfo(AppSettings settings, IReadOnlyList<string> arguments)
    {
        ProxySupport.Validate(settings);
        var paths = AppPaths.Resolve(settings, root);
        var start = new ProcessStartInfo(paths.MiseExecutable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = paths.ConfigDirectory
        };
        foreach (string argument in arguments)
        {
            if (argument.IndexOf('\0') >= 0) throw new ArgumentException(UiText.T("命令参数含无效字符。"), nameof(arguments));
            start.ArgumentList.Add(argument);
        }
        MiseEnvironment.Apply(start, settings, paths);
        return start;
    }

    public async Task<ProcessResult> RunAsync(AppSettings settings, IReadOnlyList<string> arguments, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        var paths = AppPaths.Resolve(settings, root);
        if (!File.Exists(paths.MiseExecutable)) throw new InvalidOperationException(UiText.T("尚未安装 mise，请先一键安装。"));
        cancellationToken.ThrowIfCancellationRequested();
        MiseEnvironment.EnsureDirectories(paths);
        using var process = new Process { StartInfo = CreateStartInfo(settings, arguments) };
        log?.Invoke("mise " + ProxySupport.Sanitize(string.Join(" ", arguments)));
        if (!process.Start()) throw new InvalidOperationException(UiText.T("无法启动 mise。"));
        Task<string> output = ReadOutputAsync(process.StandardOutput, log);
        Task<string> error = ReadOutputAsync(process.StandardError, log);
        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            throw;
        }
        return new ProcessResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, Action<string>? log)
    {
        var collected = new StringBuilder();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            string safe = ProxySupport.Sanitize(line);
            collected.AppendLine(safe);
            log?.Invoke(safe);
        }
        return collected.ToString();
    }

    public async Task<string> GetVersionAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(AppPaths.Resolve(settings, root).MiseExecutable)) return UiText.T("未安装");
        var result = await RunAsync(settings, new[] { "--version" }, cancellationToken: cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess();
        return result.StandardOutput.Trim();
    }

    public async Task<IReadOnlyList<ToolInfo>> GetToolsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(AppPaths.Resolve(settings, root).MiseExecutable)) return [];
        var result = await RunAsync(settings, new[] { "ls", "--installed", "--json" }, cancellationToken: cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess();
        return ParseTools(result.StandardOutput);
    }

    public static IReadOnlyList<ToolInfo> ParseTools(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException(UiText.T("mise 工具列表格式不受支持。"));
            var tools = new List<ToolInfo>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array) throw new InvalidDataException(UiText.T("mise 工具版本列表格式不受支持。"));
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (!item.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException(UiText.T("mise 工具记录缺少版本。"));
                    bool active = item.TryGetProperty("active", out var activeValue) && activeValue.ValueKind == JsonValueKind.True;
                    bool installed = !item.TryGetProperty("installed", out var installedValue) || installedValue.ValueKind == JsonValueKind.True;
                    string requested = item.TryGetProperty("requested_version", out var requestedValue) && requestedValue.ValueKind == JsonValueKind.String ? requestedValue.GetString() ?? "" : "";
                    tools.Add(new ToolInfo(property.Name, version.GetString()!, active, installed, requested));
                }
            }
            return tools.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Version, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (JsonException error) { throw new InvalidDataException(UiText.T("mise 返回的工具列表不是有效 JSON。"), error); }
    }

    public async Task UpgradeToolsAsync(AppSettings settings, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(settings, new[] { "upgrade", "--yes" }, log, cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess();
    }

    public async Task SelfUpdateAsync(AppSettings settings, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(settings, new[] { "self-update", "--yes" }, log, cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess();
    }

    public Task InstallToolAsync(AppSettings settings, string specification, Action<string>? log = null, CancellationToken cancellationToken = default)
        => InstallToolsAsync(settings, [new ToolInstallRequest(specification)], log, cancellationToken);

    public async Task InstallToolsAsync(AppSettings settings, IReadOnlyList<ToolInstallRequest> requests, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(settings, ToolSpecifications.InstallArguments(requests), log, cancellationToken).ConfigureAwait(false);
        result.EnsureSuccess();
    }
}
