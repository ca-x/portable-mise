using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using PortableMise.Models;
using PortableMise.Services.Networking;

namespace PortableMise.Services;

public sealed class MiseInstaller : IMiseInstaller
{
    private readonly string appDirectory;
    private readonly Func<AppSettings, HttpClient> clientFactory;
    private readonly Func<string, CancellationToken, Task<string?>>? versionReader;

    public MiseInstaller(string? appDirectory = null, Func<AppSettings, HttpClient>? clientFactory = null,
        Func<string, CancellationToken, Task<string?>>? installedVersionReader = null)
    {
        this.appDirectory = Path.GetFullPath(appDirectory ?? AppContext.BaseDirectory);
        this.clientFactory = clientFactory ?? ProxySupport.CreateClient;
        versionReader = installedVersionReader;
    }

    public async Task<string> InstallAsync(AppSettings settings, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        ProxySupport.Validate(settings);
        var paths = AppPaths.Resolve(settings, appDirectory);
        using var client = clientFactory(settings);
        string version = await MiseReleaseSource.ResolveAsync(client, cancellationToken).ConfigureAwait(false);
        string? installed = versionReader == null
            ? await ReadInstalledVersionAsync(paths, settings, cancellationToken).ConfigureAwait(false)
            : await versionReader(paths.MiseExecutable, cancellationToken).ConfigureAwait(false);
        if (File.Exists(paths.MiseExecutable) && File.Exists(paths.MiseShimExecutable) &&
            Version.TryParse(installed?.TrimStart('v').Split(' ')[0], out var existing) && existing >= Version.Parse(version))
        {
            log?.Invoke("mise 已安装且版本无需降级，保留现有工具与配置。");
            return installed!;
        }
        string architecture = RuntimeInformation.OSArchitecture switch {
            Architecture.X64 => "x64", Architecture.Arm64 => "arm64", _ => throw new InvalidOperationException("mise 官方 Windows 二进制仅支持 x64 和 ARM64。")
        };
        string asset = $"mise-v{version}-windows-{architecture}.zip";
        Directory.CreateDirectory(paths.CacheDirectory);
        string work = Path.Combine(paths.CacheDirectory, "mise-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            log?.Invoke($"正在下载 mise {version}（{architecture}），代理模式：{settings.ProxyMode}。");
            string archive = Path.Combine(work, asset);
            var githubBase = new Uri($"https://github.com/jdx/mise/releases/download/v{version}/");
            try
            {
                await HttpDownloads.DownloadAsync(client, new Uri($"https://mise.jdx.dev/v{version}/{asset}"), archive, 128L * 1024 * 1024, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is HttpRequestException or IOException)
            {
                if (File.Exists(archive)) File.Delete(archive);
                log?.Invoke("官方镜像暂不可用，正在使用 GitHub Releases。");
                await HttpDownloads.DownloadAsync(client, new Uri(githubBase, asset), archive, 128L * 1024 * 1024, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            string checksums = await HttpDownloads.GetTextAsync(client, new Uri(githubBase, "SHASUMS256.txt"), 2 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
            await VerifiedMiseArchive.VerifyAsync(archive, checksums, asset, cancellationToken).ConfigureAwait(false);
            string staged = Path.Combine(work, "staged");
            await VerifiedMiseArchive.ExtractAsync(archive, staged, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            BinaryPairDeployment.Apply(staged, paths.MiseBinDirectory, log);
            log?.Invoke("mise.exe 与 mise-shim.exe 已成对安装，SHA256 校验通过。");
            return version;
        }
        finally { Cleanup(work, paths.CacheDirectory, log); }
    }

    public async Task<string> TestProxyAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ProxySupport.Validate(settings);
        using var client = clientFactory(settings);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            using var response = await client.GetAsync("https://github.com/jdx/mise/releases/latest", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return $"连接成功 · {settings.ProxyMode} · GitHub HTTP {(int)response.StatusCode}";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("代理连接测试超时，请检查代理地址及网络连接。");
        }
    }

    private static async Task<string?> ReadInstalledVersionAsync(AppPaths paths, AppSettings settings, CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.MiseExecutable)) return null;
        MiseEnvironment.EnsureDirectories(paths);
        var start = new ProcessStartInfo(paths.MiseExecutable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = paths.ConfigDirectory };
        MiseEnvironment.Apply(start, settings, paths);
        start.ArgumentList.Add("--version");
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            string text = await output.ConfigureAwait(false);
            return process.ExitCode == 0 ? text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() : null;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { return null; }
    }

    private static void Cleanup(string directory, string cacheRoot, Action<string>? log)
    {
        string full = Path.GetFullPath(directory);
        string expected = Path.GetFullPath(cacheRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(expected, StringComparison.OrdinalIgnoreCase) || (new DirectoryInfo(full).Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("安装缓存目录不在预期位置，未清理。");
        try { Directory.Delete(full, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { log?.Invoke("安装缓存仍在使用，可稍后清理。"); }
    }
}
