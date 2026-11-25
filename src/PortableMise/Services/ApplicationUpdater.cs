using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using PortableMise.Models;
using PortableMise.Services.AppUpdate;
using PortableMise.Services.Networking;

namespace PortableMise.Services;

public sealed class ApplicationUpdater : IApplicationUpdater
{
    private const string Api = "https://api.github.com/repos/ca-x/portable-mise/releases/latest";
    private const string ReleasePrefix = "https://github.com/ca-x/portable-mise/releases/download/";
    private readonly string root;
    private readonly Func<AppSettings, HttpClient> clients;
    private readonly Version currentVersion;
    private readonly Architecture architecture;

    public ApplicationUpdater(string appDirectory, Func<AppSettings, HttpClient>? clientFactory = null,
        Version? currentVersion = null, Architecture? architecture = null)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory));
        clients = clientFactory ?? ProxySupport.CreateClient;
        this.currentVersion = Normalize(currentVersion ?? Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0));
        this.architecture = architecture ?? RuntimeInformation.ProcessArchitecture;
    }

    private string Runtime => architecture switch
    {
        Architecture.X64 => "win-x64",
        Architecture.Arm64 => "win-arm64",
        _ => throw new InvalidOperationException("桌面程序更新仅支持 Windows x64 和 ARM64。")
    };

    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

    private static Version ParseVersion(string value)
    {
        string normalized = value.StartsWith('v') ? value[1..] : value;
        if (!Version.TryParse(normalized, out var version) || version.Build < 0 || version.Revision > 0)
            throw new InvalidOperationException("程序更新版本号格式不受支持。");
        return Normalize(version);
    }

    public async Task<AppRelease?> CheckAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ProxySupport.Validate(settings);
        using var client = clients(settings);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("portable-mise-desktop/0.1");
        string json;
        try { json = await HttpDownloads.GetTextAsync(client, new Uri(Api), 131072, cancellationToken).ConfigureAwait(false); }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound) { return null; }
        catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            return await CheckReleasePageAsync(client, cancellationToken).ConfigureAwait(false);
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            var release = document.RootElement;
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
            string tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
            Version version = ParseVersion(tag);
            if (version <= currentVersion) return null;
            string name = $"PortableMise-v{version.ToString(3)}-{Runtime}.exe";
            string? binary = null;
            string? checksums = null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                string? assetName = asset.GetProperty("name").GetString();
                if (assetName == name) binary = asset.GetProperty("browser_download_url").GetString();
                if (assetName == "SHA256SUMS.txt") checksums = asset.GetProperty("browser_download_url").GetString();
            }
            if (binary == null || checksums == null) return null;
            var result = new AppRelease(version.ToString(3), name, new Uri(binary), new Uri(checksums));
            ValidateRelease(result);
            return result;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            throw new InvalidOperationException("GitHub 程序更新信息格式无效。", error);
        }
    }

    private async Task<AppRelease?> CheckReleasePageAsync(HttpClient client, CancellationToken cancellationToken)
    {
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        Uri final;
        using (var response = await client.GetAsync("https://github.com/ca-x/portable-mise/releases/latest", HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            final = response.RequestMessage?.RequestUri ?? throw new InvalidOperationException("无法确认程序发布地址。");
        }
        const string tagPrefix = "/ca-x/portable-mise/releases/tag/";
        if (final.Scheme != Uri.UriSchemeHttps || final.Host != "github.com" || !final.IsDefaultPort || final.UserInfo.Length != 0 ||
            final.Query.Length != 0 || final.Fragment.Length != 0)
            throw new InvalidOperationException("程序发布页面跳转到了未知来源。");
        if (final.AbsolutePath == "/ca-x/portable-mise/releases") return null;
        if (!final.AbsolutePath.StartsWith(tagPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("程序发布页面跳转到了未知来源。");
        string tag = final.AbsolutePath[tagPrefix.Length..];
        Version version = ParseVersion(tag);
        if (tag != "v" + version.ToString(3)) throw new InvalidOperationException("程序发布页面版本格式不受支持。");
        if (version <= currentVersion) return null;
        string html;
        try
        {
            html = await HttpDownloads.GetTextAsync(client, new Uri("https://github.com/ca-x/portable-mise/releases/expanded_assets/" + tag), 1048576, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound) { return null; }
        var links = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(html, """<a\b[^>]*\bhref\s*=\s*(?<quote>["'])(?<href>.*?)\k<quote>""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            if (Uri.TryCreate(new Uri("https://github.com"), WebUtility.HtmlDecode(match.Groups["href"].Value), out var link)) links.Add(link.AbsoluteUri);
        string name = $"PortableMise-{tag}-{Runtime}.exe";
        var binary = new Uri(ReleasePrefix + tag + "/" + name);
        var checksums = new Uri(ReleasePrefix + tag + "/SHA256SUMS.txt");
        if (!links.Contains(binary.AbsoluteUri) || !links.Contains(checksums.AbsoluteUri)) return null;
        var result = new AppRelease(version.ToString(3), name, binary, checksums);
        ValidateRelease(result);
        return result;
    }

    private void ValidateRelease(AppRelease release)
    {
        string version = ParseVersion(release.Version).ToString(3);
        string expected = $"PortableMise-v{version}-{Runtime}.exe";
        string prefix = ReleasePrefix + "v" + version + "/";
        if (release.AssetName != expected || release.DownloadUrl.AbsoluteUri != prefix + expected ||
            release.ChecksumsUrl.AbsoluteUri != prefix + "SHA256SUMS.txt")
            throw new InvalidOperationException("更新附件不属于当前架构或受支持的程序发布来源。");
    }

    public async Task<StagedAppUpdate> StageAsync(AppRelease release, AppSettings settings, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        ValidateRelease(release);
        ProxySupport.Validate(settings);
        using var client = clients(settings);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("portable-mise-desktop/0.1");
        string sums = await HttpDownloads.GetTextAsync(client, release.ChecksumsUrl, 131072, cancellationToken).ConfigureAwait(false);
        string expected = Checksum(sums, release.AssetName);
        string directory = Path.Combine(AppPaths.Resolve(settings, root).UpdatesDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, release.AssetName);
        try
        {
            log?.Invoke("正在下载桌面程序更新…");
            await HttpDownloads.DownloadAsync(client, release.DownloadUrl, destination, 134217728, null, cancellationToken).ConfigureAwait(false);
            string actual = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(destination, cancellationToken).ConfigureAwait(false))).ToLowerInvariant();
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("程序更新 SHA256 不一致，未执行附件。");
            log?.Invoke("桌面程序更新已通过 SHA256 校验。");
            return new StagedAppUpdate(destination, actual, release.Version);
        }
        catch
        {
            if (File.Exists(destination)) File.Delete(destination);
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            throw;
        }
    }

    private static string Checksum(string text, string asset)
    {
        string? expected = null;
        foreach (string line in text.Split('\n'))
        {
            string[] fields = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 2) continue;
            string name = fields[1].TrimStart('*');
            if (name.StartsWith("./", StringComparison.Ordinal)) name = name[2..];
            if (name != asset) continue;
            if (!UpdateApplier.IsHash(fields[0]) || expected != null && !expected.Equals(fields[0], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("更新附件校验记录无效。");
            expected = fields[0];
        }
        return expected ?? throw new InvalidOperationException("未找到此程序附件的 SHA256，未下载或执行更新。");
    }

    public void LaunchApply(StagedAppUpdate update)
    {
        string target = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定当前程序位置。");
        if (!Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(target))!).Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("当前程序不在配置目录中，无法自动替换。");
        UpdateApplier.ValidateStagedFile(update.FilePath, target, update.Sha256);
        using var current = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(update.FilePath) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(update.FilePath)! };
        string targetHash;
        using (var input = File.OpenRead(target)) targetHash = Convert.ToHexString(SHA256.HashData(input));
        foreach (string value in new[] { "--apply-update", "--target", target, "--parent-pid", current.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), "--parent-start", current.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), "--sha256", update.Sha256, "--target-sha256", targetHash })
            start.ArgumentList.Add(value);
        using var helper = Process.Start(start) ?? throw new InvalidOperationException("无法启动程序更新助手。");
    }

    public static Task<bool> TryApplyUpdateModeAsync(string[] args, Action<string>? log = null) => UpdateApplier.TryRunAsync(args, log);

}
