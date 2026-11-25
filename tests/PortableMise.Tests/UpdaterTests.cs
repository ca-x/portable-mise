using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using PortableMise.Models;
using PortableMise.Services;
using PortableMise.Services.AppUpdate;

public static class UpdaterTests
{
    private static int count;
    private const string Asset = "PortableMise-v0.2.0-win-x64.exe";
    private const string Base = "https://github.com/ca-x/portable-mise/releases/download/v0.2.0/";

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + label);
        count++;
        Console.WriteLine("PASS: " + label);
    }

    private static async Task RejectAsync(Func<Task> action, string label)
    {
        bool rejected = false;
        try { await action(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, label);
    }

    private static string Manifest(string tag = "v0.2.0", bool draft = false, string architecture = "x64") =>
        $$"""{"tag_name":"{{tag}}","draft":{{draft.ToString().ToLowerInvariant()}},"prerelease":false,"assets":[{"name":"PortableMise-{{tag}}-win-{{architecture}}.exe","browser_download_url":"https://github.com/ca-x/portable-mise/releases/download/{{tag}}/PortableMise-{{tag}}-win-{{architecture}}.exe"},{"name":"SHA256SUMS.txt","browser_download_url":"https://github.com/ca-x/portable-mise/releases/download/{{tag}}/SHA256SUMS.txt"}]}""";

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }

    private static ApplicationUpdater Updater(string root, Func<HttpRequestMessage, HttpResponseMessage> handler,
        Version? current = null, Architecture architecture = Architecture.X64) =>
        new(root, _ => new HttpClient(new Handler(handler)), current ?? new Version(0, 1, 0, 0), architecture);

    private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };

    public static async Task<int> RunAsync(string scratchRoot)
    {
        count = 0;
        Directory.CreateDirectory(scratchRoot);
        var settings = new AppSettings();
        Check(await Updater(scratchRoot, _ => new(HttpStatusCode.NotFound)).CheckAsync(settings) == null, "application with no GitHub release remains usable");
        Check(await Updater(scratchRoot, _ => Text(Manifest("v0.1.0"))).CheckAsync(settings) == null, "normalized equal version is not an update");
        Check(await Updater(scratchRoot, _ => Text(Manifest()), new Version(1, 0)).CheckAsync(settings) == null, "older application release is ignored");
        Check(await Updater(scratchRoot, _ => Text(Manifest(draft: true))).CheckAsync(settings) == null, "draft application release is ignored");
        var release = await Updater(scratchRoot, _ => Text(Manifest())).CheckAsync(settings);
        Check(release?.AssetName == Asset, "application release selects native x64 asset");
        var arm = await Updater(scratchRoot, _ => Text(Manifest(architecture: "arm64")), architecture: Architecture.Arm64).CheckAsync(settings);
        Check(arm?.AssetName.EndsWith("win-arm64.exe", StringComparison.Ordinal) == true, "application release selects native arm64 asset");
        Check(await Updater(scratchRoot, _ => Text(Manifest(architecture: "arm64"))).CheckAsync(settings) == null, "release without the native asset is not offered");
        string links = "<a href=\"/ca-x/portable-mise/releases/download/v0.2.0/" + Asset + "\">exe</a><a href=\"/ca-x/portable-mise/releases/download/v0.2.0/SHA256SUMS.txt\">sum</a>";
        int factories = 0;
        var routes = new List<string>();
        var proxySettings = new AppSettings { ProxyMode = ProxyMode.Custom, ProxyAddress = "http://127.0.0.1:7890" };
        var fallback = new ApplicationUpdater(scratchRoot, received => {
            Check(ReferenceEquals(received, proxySettings), "release fallback preserves selected proxy settings");
            factories++;
            return new HttpClient(new Handler(request => {
                routes.Add(request.RequestUri!.AbsoluteUri);
                if (request.RequestUri.Host == "api.github.com") return new(HttpStatusCode.Forbidden);
                if (request.RequestUri.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal)) return Page("https://github.com/ca-x/portable-mise/releases/tag/v0.2.0");
                return Text(links);
            }));
        }, new Version(0, 1, 0), Architecture.X64);
        Check((await fallback.CheckAsync(proxySettings))?.AssetName == Asset, "API rate limit falls back to verified release page assets");
        Check(factories == 1 && routes.Count == 3 && routes[2].EndsWith("/releases/expanded_assets/v0.2.0", StringComparison.Ordinal), "release page fallback reuses the same proxy client");
        var rateLimited = Updater(scratchRoot, request => request.RequestUri!.Host == "api.github.com" ? new(HttpStatusCode.TooManyRequests)
            : request.RequestUri.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal) ? Page("https://github.com/ca-x/portable-mise/releases/tag/v0.2.0") : Text(links));
        Check((await rateLimited.CheckAsync(settings))?.AssetName == Asset, "HTTP 429 uses the same bounded release-page fallback");
        var missingPageAsset = Updater(scratchRoot, request => request.RequestUri!.Host == "api.github.com" ? new(HttpStatusCode.Forbidden)
            : request.RequestUri.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal) ? Page("https://github.com/ca-x/portable-mise/releases/tag/v0.2.0") : Text("<a href='/other.exe'>other</a>"));
        Check(await missingPageAsset.CheckAsync(settings) == null, "release page without both exact assets is not offered");
        var wrongRepository = Updater(scratchRoot, request => request.RequestUri!.Host == "api.github.com" ? new(HttpStatusCode.Forbidden) : Page("https://github.com/other/portable-mise/releases/tag/v0.2.0"));
        await RejectAsync(() => wrongRepository.CheckAsync(settings), "release fallback rejects redirects to another repository");
        var noPage = Updater(scratchRoot, request => request.RequestUri!.Host == "api.github.com" ? new(HttpStatusCode.Forbidden) : new(HttpStatusCode.NotFound));
        Check(await noPage.CheckAsync(settings) == null, "rate-limited repository with no release returns empty update state");
        var releaseIndex = Updater(scratchRoot, request => request.RequestUri!.Host == "api.github.com" ? new(HttpStatusCode.Forbidden) : Page("https://github.com/ca-x/portable-mise/releases"));
        Check(await releaseIndex.CheckAsync(settings) == null, "repository release-index redirect means no published update");
        var unstablePage = Updater(scratchRoot, request => request.RequestUri!.Host == "api.github.com" ? new(HttpStatusCode.Forbidden) : Page("https://github.com/ca-x/portable-mise/releases/tag/v0.2.0-beta.1"));
        await RejectAsync(() => unstablePage.CheckAsync(settings), "release page fallback rejects unstable tag versions");
        byte[] payload = Encoding.UTF8.GetBytes("verified application payload");
        string sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var requested = new AppRelease("0.2.0", Asset, new Uri(Base + Asset), new Uri(Base + "SHA256SUMS.txt"));
        var updater = Updater(scratchRoot, request => request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
            ? Text(sha + "  " + Asset + "\n") : new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        var staged = await updater.StageAsync(requested, settings);
        Check(staged.Sha256 == sha && File.ReadAllBytes(staged.FilePath).SequenceEqual(payload), "staged application bytes match the release checksum");
        Check(Path.GetFullPath(staged.FilePath).StartsWith(Path.Combine(Path.GetFullPath(scratchRoot), "data", "updates") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "application staging remains within the portable update directory");
        var bad = Updater(scratchRoot, request => request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
            ? Text(new string('0', 64) + "  " + Asset) : new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        await RejectAsync(() => bad.StageAsync(requested, settings), "application checksum mismatch is rejected");
        var missing = Updater(scratchRoot, _ => Text(sha + "  other.exe"));
        await RejectAsync(() => missing.StageAsync(requested, settings), "checksum for another asset cannot authorize the application");
        await RejectAsync(() => updater.StageAsync(requested with { AssetName = "../outside.exe" }, settings), "traversal asset name is rejected before download");
        await RejectAsync(() => updater.StageAsync(requested with { DownloadUrl = new Uri("https://example.invalid/payload.exe") }, settings), "application payload must come from the configured release repository");
        await RejectAsync(() => updater.StageAsync(requested with { ChecksumsUrl = new Uri(Base + "other.txt") }, settings), "application checksum URL is bound to its release");
        string target = Path.Combine(scratchRoot, "PortableMise.exe");
        File.WriteAllText(target, "old executable");
        string backup = UpdateApplier.ApplyVerifiedFile(staged.FilePath, target, sha);
        Check(File.ReadAllBytes(target).SequenceEqual(payload) && File.ReadAllText(backup) == "old executable", "verified replacement preserves the previous application");
        string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)));
        await RejectAsync(() => Task.Run(() => UpdateApplier.ApplyVerifiedFile(staged.FilePath, target, new string('0', 64))), "replacement rechecks staged bytes before changing the application");
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))) == before, "failed replacement preserves the current application");
        await RejectAsync(() => Task.Run(() => UpdateApplier.ApplyVerifiedFile(staged.FilePath, target, sha, new string('f', 64))), "replacement rejects an application changed since staging");
        Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))) == before, "stale application snapshot cannot overwrite the newer file");
        string outside = Path.Combine(scratchRoot, "outside.exe");
        File.WriteAllBytes(outside, payload);
        await RejectAsync(() => Task.Run(() => UpdateApplier.ApplyVerifiedFile(outside, target, sha)), "replacement cannot consume a file outside portable staging");
        Check(!await ApplicationUpdater.TryApplyUpdateModeAsync([]), "normal startup is not treated as update helper mode");
        await RejectAsync(() => ApplicationUpdater.TryApplyUpdateModeAsync(["--apply-update"]), "incomplete helper arguments fail before file mutation");
        Console.WriteLine("UPDATER_TOTAL: " + count);
        return count;
    }

    private static HttpResponseMessage Page(string finalUrl) => new(HttpStatusCode.OK)
    {
        RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUrl),
        Content = new StringContent("<html>release</html>")
    };
}
