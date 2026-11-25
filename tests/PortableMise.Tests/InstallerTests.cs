using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using PortableMise.Models;
using PortableMise.Services;

namespace PortableMise.Tests;

public static class InstallerTests
{
    public static async Task<int> RunAsync(string root)
    {
        int count = 0;
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); count++; Console.WriteLine("PASS: " + name); }
        async Task Reject(Func<Task> action, string name) { bool rejected = false; try { await action(); } catch (Exception e) when (e is InvalidOperationException or InvalidDataException or ArgumentException) { rejected = true; } Check(rejected, name); }
        Directory.CreateDirectory(root);
        var settings = new AppSettings { ProxyMode = ProxyMode.Disabled };
        var custom = new AppSettings { ProxyMode = ProxyMode.Custom, ProxyAddress = "http://127.0.0.1:7890" };
        var variables = ProxySupport.GetProcessEnvironment(custom);
        Check(variables["HTTP_PROXY"] == custom.ProxyAddress && variables["HTTPS_PROXY"] == custom.ProxyAddress, "custom proxy propagates to mise children");
        Check(ProxySupport.GetProcessEnvironment(settings)["HTTPS_PROXY"] == null, "direct mode clears inherited proxy");
        Check(!ProxySupport.Sanitize("https://name:secret@host/path").Contains("secret", StringComparison.Ordinal), "proxy credentials are redacted");
        await Reject(() => { ProxySupport.Validate(new AppSettings { ProxyMode = ProxyMode.Custom, ProxyAddress = "file:///secret" }); return Task.CompletedTask; }, "unsupported proxy protocol rejected");
        await Reject(() => { ProxySupport.Validate(new AppSettings { ProxyMode = ProxyMode.Custom, ProxyAddress = "http://name:secret@proxy:8888" }); return Task.CompletedTask; }, "credentials are not accepted as plain settings URL");
        foreach (ProxyMode mode in new[] { ProxyMode.System, ProxyMode.Disabled })
            await Reject(() => { ProxySupport.Validate(new AppSettings { ProxyMode = mode, ProxyAddress = "http://name:secret@proxy:8888" }); return Task.CompletedTask; }, mode + " rejects credentials in inactive proxy address");

        byte[] valid = Archive(false, false);
        var handler = new FixtureHandler(valid);
        var installer = new MiseInstaller(Path.Combine(root, "good"), _ => new HttpClient(handler, false), (_, _) => Task.FromResult<string?>(null));
        string version = await installer.InstallAsync(settings);
        var paths = AppPaths.Resolve(settings, Path.Combine(root, "good"));
        Check(version == "2026.10.1" && File.Exists(paths.MiseExecutable) && File.Exists(paths.MiseShimExecutable), "installer deploys mise and companion shim together");
        Check(handler.Requests.Any(u => u.Host == "github.com" && u.AbsolutePath.EndsWith("SHASUMS256.txt", StringComparison.Ordinal)), "checksums always come from GitHub releases");
        Check(handler.Requests.Any(u => u.Host == "mise.jdx.dev" && u.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal)), "official mirror is preferred");
        var keep = new MiseInstaller(Path.Combine(root, "good"), _ => new HttpClient(handler, false), (_, _) => Task.FromResult<string?>("9999.0.0"));
        int before = handler.Requests.Count;
        Check(await keep.InstallAsync(settings) == "9999.0.0" && !handler.Requests.Skip(before).Any(u => u.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal)), "newer existing mise is not downgraded");

        foreach (var item in new[] { (Name: "bad-hash", Data: valid, BadHash: true), (Name: "missing-shim", Data: Archive(true, false), BadHash: false), (Name: "traversal", Data: Archive(false, true), BadHash: false) })
        {
            string scenario = Path.Combine(root, item.Name);
            var fake = new FixtureHandler(item.Data) { BadHash = item.BadHash };
            var rejected = new MiseInstaller(scenario, _ => new HttpClient(fake, false), (_, _) => Task.FromResult<string?>(null));
            await Reject(() => rejected.InstallAsync(settings), item.Name + " rejected before deployment");
            Check(!File.Exists(AppPaths.Resolve(settings, scenario).MiseExecutable), item.Name + " does not install unverified binary");
        }

        var fallbackHandler = new FixtureHandler(valid) { MirrorUnavailable = true };
        var fallback = new MiseInstaller(Path.Combine(root, "fallback"), _ => new HttpClient(fallbackHandler, false), (_, _) => Task.FromResult<string?>(null));
        await fallback.InstallAsync(settings);
        Check(fallbackHandler.Requests.Any(u => u.Host == "github.com" && u.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal)), "unavailable mirror falls back to GitHub");

        string rollbackRoot = Path.Combine(root, "rollback");
        var rollbackPaths = AppPaths.Resolve(settings, rollbackRoot);
        Directory.CreateDirectory(rollbackPaths.MiseBinDirectory);
        byte[] originalExe = Encoding.ASCII.GetBytes("MZoriginal version");
        File.WriteAllBytes(rollbackPaths.MiseExecutable, originalExe);
        Directory.CreateDirectory(rollbackPaths.MiseShimExecutable);
        var rollback = new MiseInstaller(rollbackRoot, _ => new HttpClient(handler, false), (_, _) => Task.FromResult<string?>(null));
        await Reject(() => rollback.InstallAsync(settings), "companion deployment failure is reported");
        Check(File.ReadAllBytes(rollbackPaths.MiseExecutable).SequenceEqual(originalExe), "failed companion deployment restores old mise binary");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var accepted = Task.Run(async () => {
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            using NetworkStream stream = connection.GetStream();
            byte[] buffer = new byte[4096];
            int length = await stream.ReadAsync(buffer, timeout.Token);
            string request = Encoding.ASCII.GetString(buffer, 0, length);
            byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK");
            await stream.WriteAsync(response, timeout.Token);
            return request;
        }, timeout.Token);
        try
        {
            using var client = ProxySupport.CreateClient(new AppSettings { ProxyMode = ProxyMode.Custom, ProxyAddress = $"http://127.0.0.1:{port}" });
            string response = await client.GetStringAsync("http://portable-mise.invalid/proxy-check", timeout.Token);
            string request = await accepted;
            Check(response == "OK" && request.StartsWith("GET http://portable-mise.invalid/proxy-check", StringComparison.Ordinal), "HTTP download actually routes through configured proxy");
        }
        finally { listener.Stop(); }
        Console.WriteLine("INSTALLER_TOTAL: " + count);
        return count;
    }

    private static byte[] Archive(bool missingShim, bool traversal)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (string name in missingShim ? new[] { "mise/bin/mise.exe" } : new[] { "mise/bin/mise.exe", "mise/bin/mise-shim.exe" })
            { using var stream = zip.CreateEntry(name).Open(); stream.Write(Encoding.ASCII.GetBytes("MZfixture executable bytes")); }
            if (traversal) { using var stream = zip.CreateEntry("../../outside.exe").Open(); stream.WriteByte(1); }
        }
        return output.ToArray();
    }

    private sealed class FixtureHandler(byte[] archive) : HttpMessageHandler
    {
        public bool BadHash { get; init; }
        public bool MirrorUnavailable { get; init; }
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri ?? throw new InvalidOperationException("Missing URI");
            Requests.Add(uri);
            HttpResponseMessage response;
            if (uri.AbsolutePath.EndsWith("releases.tsv", StringComparison.Ordinal))
                response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("v2026.10.1\t1\nv9999.0.0\t9999999999\n") };
            else if (uri.AbsolutePath.EndsWith("SHASUMS256.txt", StringComparison.Ordinal))
            {
                string hash = BadHash ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(archive));
                response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(hash + "  ./mise-v2026.10.1-windows-x64.zip\n" + hash + "  ./mise-v2026.10.1-windows-arm64.zip\n") };
            }
            else if (uri.Host == "mise.jdx.dev" && MirrorUnavailable) response = new HttpResponseMessage(HttpStatusCode.BadGateway);
            else response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
            return Task.FromResult(response);
        }
    }
}
