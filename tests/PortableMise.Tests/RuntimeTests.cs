using System.Text.Json;
using System.Diagnostics;
using PortableMise.Models;
using PortableMise.Services;

namespace PortableMise.Tests;

public static class RuntimeTests
{
    private static int count;
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + name);
        count++;
        Console.WriteLine("PASS: " + name);
    }

    public static async Task<int> RunAsync(string scratchRoot)
    {
        count = 0;
        string first = Path.GetFullPath(Path.Combine(scratchRoot, "runtime-first"));
        string moved = Path.GetFullPath(Path.Combine(scratchRoot, "runtime-moved"));
        Directory.CreateDirectory(first);
        var settings = new AppSettings();
        var store = new SettingsStore(first);
        Check(store.Load().MiseDirectory == "mise", "missing settings use portable defaults");
        await store.SaveAsync(settings);
        string json = File.ReadAllText(Path.Combine(first, "portable-mise.json"));
        Check(!json.Contains(first, StringComparison.OrdinalIgnoreCase), "default paths are not persisted as machine-specific paths");
        Directory.CreateDirectory(moved);
        File.Copy(Path.Combine(first, "portable-mise.json"), Path.Combine(moved, "portable-mise.json"));
        Check(AppPaths.Resolve(new SettingsStore(moved).Load(), moved).MiseRoot == Path.Combine(moved, "mise"), "portable settings follow moved application directory");
        settings.ToolsDirectory = "custom tools";
        await store.SaveAsync(settings);
        Check(store.Load().ToolsDirectory == "custom tools" && File.Exists(Path.Combine(first, "portable-mise.json.bak")), "settings update preserves previous configuration backup");
        File.WriteAllText(Path.Combine(first, "portable-mise.json"), "{bad-json");
        bool rejected = false;
        try { store.Load(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && File.ReadAllText(Path.Combine(first, "portable-mise.json")) == "{bad-json", "invalid settings are reported and preserved");

        var env = new MemoryEnvironment();
        env.Values["Path"] = @"%USERPROFILE%\bin;C:\Existing;C:\Existing\";
        var fake = new FakeMise();
        var terminal = new TerminalIntegration(first, fake, env);
        var install = new AppSettings();
        var paths = AppPaths.Resolve(install, first);
        Directory.CreateDirectory(paths.MiseBinDirectory);
        File.WriteAllBytes(paths.MiseExecutable, [1]);
        File.WriteAllBytes(paths.MiseShimExecutable, [1]);
        await terminal.RepairAsync(install);
        Check(env.Values["Path"]!.Contains(@"%USERPROFILE%\bin", StringComparison.Ordinal), "terminal repair preserves unexpanded unrelated PATH entries");
        Check(env.Values["MISE_DATA_DIR"] == paths.ToolsRoot && env.Values["MISE_CONFIG_DIR"] == paths.ConfigDirectory, "terminal repair registers portable data and config locations");
        Check(terminal.IsConfigured(install), "terminal configuration can be verified");
        string once = env.Values["Path"]!;
        await terminal.RepairAsync(install);
        Check(env.Values["Path"] == once, "terminal repair is idempotent");
        Check(fake.Arguments.Any(a => a.SequenceEqual(new[] { "reshim" })), "terminal repair regenerates tool shims");
        var relocated = new TerminalIntegration(moved, fake, env);
        var movedPaths = AppPaths.Resolve(install, moved);
        Directory.CreateDirectory(movedPaths.MiseBinDirectory);
        File.WriteAllBytes(movedPaths.MiseExecutable, [1]);
        File.WriteAllBytes(movedPaths.MiseShimExecutable, [1]);
        await relocated.RepairAsync(install);
        Check(!env.Values["Path"]!.Split(';').Contains(paths.MiseBinDirectory, StringComparer.OrdinalIgnoreCase) && env.Values["Path"]!.Split(';').Contains(movedPaths.MiseBinDirectory, StringComparer.OrdinalIgnoreCase), "relocation removes only previously registered mise path and adds new path");
        Check(env.Values["Path"]!.Contains(@"C:\Existing", StringComparison.Ordinal), "relocation retains unrelated PATH entries");
        var failingEnvironment = new MemoryEnvironment();
        failingEnvironment.Values["MISE_DATA_DIR"] = @"C:\PreviousData";
        failingEnvironment.Values["Path"] = @"C:\OriginalPath";
        failingEnvironment.FailOnName = "MISE_CACHE_DIR";
        bool environmentRejected = false;
        try { await new TerminalIntegration(first, fake, failingEnvironment).RepairAsync(new AppSettings()); }
        catch (IOException) { environmentRejected = true; }
        Check(environmentRejected && failingEnvironment.Values["MISE_DATA_DIR"] == @"C:\PreviousData" && failingEnvironment.Values["Path"] == @"C:\OriginalPath" && !failingEnvironment.Values.ContainsKey("MISE_CONFIG_DIR"), "partial environment write rolls back previously changed values");
        await CheckUnsafePathEntriesAsync(scratchRoot);

        var runtime = new MiseService(first);
        var custom = new AppSettings { ProxyMode = ProxyMode.Custom, ProxyAddress = "http://127.0.0.1:17890" };
        var start = runtime.CreateStartInfo(custom, new[] { "upgrade", "--yes" });
        Check(start.Environment["HTTPS_PROXY"] == custom.ProxyAddress && start.Environment["HTTP_PROXY"] == custom.ProxyAddress, "mise child receives required proxy variables");
        Check(start.ArgumentList.SequenceEqual(new[] { "upgrade", "--yes" }), "mise arguments remain separate and are not shell interpolated");
        Check(start.WorkingDirectory == paths.ConfigDirectory && start.Environment["MISE_DATA_DIR"] == paths.ToolsRoot, "mise runs from controlled config directory with portable environment");
        var direct = runtime.CreateStartInfo(new AppSettings { ProxyMode = ProxyMode.Disabled }, new[] { "version" });
        Check(!direct.Environment.ContainsKey("HTTPS_PROXY") && !direct.Environment.ContainsKey("HTTP_PROXY"), "disabled proxy removes inherited proxy settings from child");
        var parsed = MiseService.ParseTools("{\"node\":[{\"version\":\"22.12.0\",\"active\":true,\"installed\":true,\"requested_version\":\"22\"}],\"python\":[{\"version\":\"3.13.1\",\"active\":false,\"installed\":true}]}");
        Check(parsed.Count == 2 && parsed.Single(t => t.Name == "node").Active && parsed.Single(t => t.Name == "python").Version == "3.13.1", "mise keyed tool JSON is parsed without inventing active versions");
        bool badJson = false;
        try { MiseService.ParseTools("not json"); } catch (InvalidDataException) { badJson = true; }
        Check(badJson, "malformed mise JSON is reported");
        var absent = new MiseService(Path.Combine(scratchRoot, "no-install"));
        Check(await absent.GetVersionAsync(new AppSettings()) == "未安装" && (await absent.GetToolsAsync(new AppSettings())).Count == 0, "absent installation produces empty state instead of starting unrelated mise");
        await CheckActualProcessAsync(first, runtime, custom);
        Console.WriteLine("RUNTIME_TOTAL: " + count);
        return count;
    }

    private static async Task CheckUnsafePathEntriesAsync(string scratchRoot)
    {
        foreach (bool unsafeMise in new[] { true, false })
        {
            string root = Path.GetFullPath(Path.Combine(scratchRoot, unsafeMise ? "bad-mise-path" : "bad-shims-path"));
            var settings = new AppSettings
            {
                MiseDirectory = unsafeMise ? "unsafe;mise" : "mise",
                ToolsDirectory = unsafeMise ? "tools" : "unsafe;tools"
            };
            var paths = AppPaths.Resolve(settings, root);
            Directory.CreateDirectory(paths.MiseBinDirectory);
            File.WriteAllBytes(paths.MiseExecutable, [1]);
            File.WriteAllBytes(paths.MiseShimExecutable, [1]);
            var fake = new FakeMise();
            var environment = new MemoryEnvironment();
            environment.Values["Path"] = @"C:\OriginalPath";
            bool repairRejected = false;
            try { await new TerminalIntegration(root, fake, environment).RepairAsync(settings); }
            catch (InvalidOperationException) { repairRejected = true; }
            Check(repairRejected && environment.WriteCount == 0 && fake.Arguments.Count == 0 && environment.Values["Path"] == @"C:\OriginalPath", "semicolon PATH entry is rejected before reshim or user environment writes");
            var start = new ProcessStartInfo();
            start.Environment["MISE_DATA_DIR"] = "unchanged";
            bool childRejected = false;
            try { MiseEnvironment.Apply(start, settings, paths); }
            catch (InvalidOperationException) { childRejected = true; }
            Check(childRejected && start.Environment["MISE_DATA_DIR"] == "unchanged", "semicolon PATH entry is rejected before changing child environment");
            bool saveRejected = false;
            try { await new SettingsStore(root).SaveAsync(settings); }
            catch (InvalidOperationException) { saveRejected = true; }
            Check(saveRejected && !File.Exists(paths.SettingsPath), "semicolon PATH entry cannot be saved as working terminal configuration");
        }
        string otherRoot = Path.GetFullPath(Path.Combine(scratchRoot, "allowed;config"));
        var allowed = new AppSettings { MiseDirectory = Path.GetFullPath(Path.Combine(scratchRoot, "safe-mise")), ToolsDirectory = Path.GetFullPath(Path.Combine(scratchRoot, "safe-tools")) };
        var info = new MiseService(otherRoot).CreateStartInfo(allowed, ["version"]);
        Check(info.WorkingDirectory.Contains(';'), "semicolon in a directory not inserted into PATH remains supported");
    }

    private static async Task CheckActualProcessAsync(string root, MiseService runtime, AppSettings custom)
    {
        string? repository = Environment.GetEnvironmentVariable("PORTABLE_MISE_TEST_REPOSITORY") ?? AppContext.BaseDirectory;
        while (repository != null && !File.Exists(Path.Combine(repository, "global.json"))) repository = Directory.GetParent(repository)?.FullName;
        if (repository is null) throw new InvalidOperationException("Cannot locate test fixture repository.");
        string fixtureRoot = Path.Combine(repository, "tests", "PortableMise.FakeMise", "bin");
        string fixture = Directory.GetFiles(fixtureRoot, "PortableMise.FakeMise.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).First();
        var paths = AppPaths.Resolve(custom, root);
        foreach (string file in Directory.GetFiles(Path.GetDirectoryName(fixture)!)) File.Copy(file, Path.Combine(paths.MiseBinDirectory, Path.GetFileName(file)), true);
        File.Copy(fixture, paths.MiseExecutable, true);
        var probe = await runtime.RunAsync(custom, new[] { "--probe-environment" });
        probe.EnsureSuccess();
        using var json = JsonDocument.Parse(probe.StandardOutput);
        Check(json.RootElement.GetProperty("HTTPS_PROXY").GetString() == custom.ProxyAddress && json.RootElement.GetProperty("MISE_DATA_DIR").GetString() == paths.ToolsRoot, "actual mise child inherits proxy and portable directories");
        string? oldProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY");
        try
        {
            Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://inherited.invalid:8123");
            var direct = custom.Copy(); direct.ProxyMode = ProxyMode.Disabled;
            var directProbe = await runtime.RunAsync(direct, new[] { "--probe-environment" });
            using var directJson = JsonDocument.Parse(directProbe.StandardOutput);
            Check(directJson.RootElement.GetProperty("HTTPS_PROXY").ValueKind == JsonValueKind.Null, "direct mode removes inherited proxy from actual child");
        }
        finally { Environment.SetEnvironmentVariable("HTTPS_PROXY", oldProxy); }
        var failure = await runtime.RunAsync(custom, new[] { "--fail" });
        Check(failure.ExitCode == 7 && failure.StandardError.Contains("intentional fixture failure", StringComparison.Ordinal), "child failure preserves stderr and nonzero exit code");
        using var cancellation = new CancellationTokenSource();
        int childId = 0;
        bool cancelled = false;
        var waiting = runtime.RunAsync(custom, new[] { "--wait" }, text => {
            if (text.StartsWith("fixture-pid=", StringComparison.Ordinal))
            {
                childId = int.Parse(text[12..], System.Globalization.CultureInfo.InvariantCulture);
                cancellation.Cancel();
            }
        }, cancellation.Token);
        try { await waiting.WaitAsync(TimeSpan.FromSeconds(15)); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && childId != 0, "cancellation stops owned mise child");
        bool gone;
        try { using var child = Process.GetProcessById(childId); gone = child.HasExited; } catch (ArgumentException) { gone = true; }
        Check(gone, "cancelled child process does not remain running");
    }

    private sealed class MemoryEnvironment : IUserEnvironmentStore
    {
        public Dictionary<string, string?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? FailOnName { get; set; }
        public int WriteCount { get; private set; }
        public string? Read(string name) => Values.GetValueOrDefault(name);
        public void Write(string name, string? value)
        {
            WriteCount++;
            if (name == FailOnName) throw new IOException("injected environment write failure");
            if (value == null) Values.Remove(name);
            else Values[name] = value;
        }
        public void Broadcast() { }
    }

    private sealed class FakeMise : IMiseService
    {
        public List<IReadOnlyList<string>> Arguments { get; } = [];
        public Task<string> GetVersionAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.FromResult("test");
        public Task<IReadOnlyList<ToolInfo>> GetToolsAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ToolInfo>>([]);
        public Task<ProcessResult> RunAsync(AppSettings settings, IReadOnlyList<string> arguments, Action<string>? log = null, CancellationToken cancellationToken = default)
        {
            Arguments.Add(arguments.ToArray());
            return Task.FromResult(new ProcessResult(0, "", ""));
        }
        public Task UpgradeToolsAsync(AppSettings settings, Action<string>? log = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SelfUpdateAsync(AppSettings settings, Action<string>? log = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InstallToolAsync(AppSettings settings, string specification, Action<string>? log = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
