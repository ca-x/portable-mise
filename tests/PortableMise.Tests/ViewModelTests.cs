using System.Windows.Input;
using System.Windows.Threading;
using PortableMise.Localization;
using PortableMise.Models;
using PortableMise.Services;
using PortableMise.ViewModels;

namespace PortableMise.Tests;

public static class ViewModelTests
{
    public static async Task<int> RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
        var paths = AppPaths.Resolve(new AppSettings(), root);
        Directory.CreateDirectory(paths.MiseBinDirectory);
        File.WriteAllText(paths.MiseExecutable, "fixture"); File.WriteAllText(paths.MiseShimExecutable, "fixture");
        var store = new MemoryStore(); var mise = new FixtureMise(); var installer = new FixtureInstaller(); var terminal = new FixtureTerminal(); var updater = new FixtureUpdater();
        using var vm = new MainViewModel(root, new AppSettings(), store, mise, installer, terminal, updater);
        await vm.InitializeAsync();
        Check(vm.IsInstalled && vm.Tools.Count == 1 && vm.MiseVersion == "2026.11.0", "view model displays real service results");
        Check(vm.NeedsRestore && vm.PrimaryActionText == "恢复环境", "existing unregistered workspace prioritizes restoration");
        vm.Settings.AutoUpdateTools = true; vm.Settings.AutoUpdateMise = true;
        await vm.CheckAutomaticUpdatesAsync();
        Check(mise.SelfUpdates == 0 && mise.ToolUpdates == 0, "automatic updates wait while a moved workspace needs restoration");
        vm.ToolFilter = "does-not-match";
        Check(vm.FilteredTools.IsEmpty, "tool filter matches observable service data");
        Check(vm.ClearToolFilterCommand.CanExecute(null), "clear filter command is available while the filter is active");
        vm.ClearToolFilterCommand.Execute(null);
        Check(string.IsNullOrEmpty(vm.ToolFilter) && !vm.ClearToolFilterCommand.CanExecute(null), "clear filter empties the tool filter and disables itself");
        vm.ToolFilter = "node";
        Check(!vm.FilteredTools.IsEmpty, "tool filter restores matching row");
        Check(vm.CloseBehaviorChoice == CloseBehavior.Ask, "first close asks for an explicit user choice");
        await vm.RememberCloseChoiceAsync(true);
        Check(vm.CloseBehaviorChoice == CloseBehavior.Tray && store.Saved?.AskBeforeClosing == false, "remembered tray choice is persisted");
        vm.CloseBehaviorChoice = CloseBehavior.Exit;
        Check(!vm.Settings.CloseToTray && !vm.Settings.AskBeforeClosing, "settings can choose actual exit independently from minimize");
        vm.CloseBehaviorChoice = CloseBehavior.Ask;
        Check(vm.Settings.AskBeforeClosing, "settings can restore ask-before-close behavior");
        await Execute(vm.InstallCommand);
        Check(installer.InstallCalls == 1 && terminal.Repairs == 1 && store.Saved?.LastRegisteredPaths.Contains("registered-by-fixture") == true, "install registers terminal and saves the repaired snapshot");
        Check(vm.IsEnvironmentReady && vm.PrimaryActionText == "更新环境", "configured workspace prioritizes routine updates");
        store.PauseNextSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.SaveEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeSave = store.PauseNextSave;
        vm.SaveSettingsCommand.Execute(null);
        await store.SaveEntered.Task;
        var rememberDuringSave = vm.RememberCloseChoiceAsync(true);
        resumeSave.SetResult();
        await ((AsyncCommand)vm.SaveSettingsCommand).ExecutionTask!;
        await rememberDuringSave;
        Check(vm.CloseBehaviorChoice == CloseBehavior.Tray && store.Saved?.AskBeforeClosing == false, "close preference is not lost during concurrent settings save");
        await Execute(vm.UpdateAllCommand);
        Check(mise.SelfUpdates == 1 && mise.ToolUpdates == 1, "update all invokes both self-update and tool upgrade");
        vm.ToolSpecification = "python@3.13";
        await Execute(vm.InstallToolCommand);
        Check(mise.LastSpec == "python@3.13", "tool installation uses entered specification");
        Check(vm.StatusText.Contains(UiText.T("已安装")) && vm.ToolInstallStatus.Contains("python@3.13"),
            "tool install reports the specific tools that finished");
        await Task.Delay(250);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(vm.LogText.Contains(UiText.T("安装开发工具")), "buffered log lines flush to the terminal text");
        vm.ToolSpecification = "rust[mr_boxington=true]@stable mr-boxington go@1.26";
        vm.QueueToolsCommand.Execute(null);
        vm.RemoveQueuedToolCommand.Execute(vm.QueuedTools.Last());
        Check(vm.QueuedTools.Count == 2 && vm.QueuedTools[0].Request.Options?["mr_boxington"] == "true",
            "queued tool options can be edited independently after parsing and another tag can be removed");
        mise.PauseBatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeBatch = mise.PauseBatch;
        vm.InstallToolCommand.Execute(null);
        Check(vm.IsInstallingTools && vm.ToolInstallLine == "正在下载 Rust", "inline progress updates while the batch is in flight");
        vm.ToolSpecification = "go@1.26";
        vm.QueueToolsCommand.Execute(null);
        resumeBatch.SetResult();
        await ((AsyncCommand)vm.InstallToolCommand).ExecutionTask!;
        Check(mise.LastBatch.SequenceEqual(["rust@stable", "mr-boxington"]) && vm.QueuedTools.Single().Request.Specification == "go@1.26",
            "a busy installation leaves newly added tools for the next batch");
        string exportFixture = Path.Combine(root, "export-fixture.toml");
        File.WriteAllText(exportFixture, """
            [tools]
            rust = { version = "stable", mr_boxington = true, components = ["rust-src", "clippy"] }
            [tools."cargo:ripgrep"]
            version = "15"
            features = ["pcre2"]
            [tool_alias]
            custom = "github:owner/tool"
            [alias.rust]
            pinned = "1.90.1"
            [env]
            RUSTUP_DIST_SERVER = "https://mirror.example"
            UNRELATED = "do-not-export"
            """);
        var cargoFixture = Path.Combine(root, "cargo-fixture.toml");
        File.WriteAllText(cargoFixture, """
            [source.crates-io]
            replace-with = "mirror"
            [source.mirror]
            registry = "sparse+https://index.example/"
            """);
        string exported = ToolExport.Create([exportFixture],
            [new("rust", "1.90.1", true, true), new("node", "22", false, true), new("node", "24", true, true)], cargoFixture);
        var exportTree = Tomlyn.TomlSerializer.Deserialize<Tomlyn.Model.TomlTable>(exported)!;
        var exportedTools = (Tomlyn.Model.TomlTable)exportTree["tools"]!;
        var exportedRust = (Tomlyn.Model.TomlTable)exportedTools["rust"]!;
        Check(exportedRust["mr_boxington"] is true && exportedRust["components"] is Tomlyn.Model.TomlArray { Count: 2 }
            && exportedTools["node"] is Tomlyn.Model.TomlArray { Count: 2 } && exportedTools.ContainsKey("cargo:ripgrep"),
            "export TOML keeps typed nested options and all standalone installed versions");
        Check(exported.Contains("# rust@1.90.1", StringComparison.Ordinal)
            && exported.Contains("https://mirror.example", StringComparison.Ordinal)
            && exported.Contains("# [source.mirror]", StringComparison.Ordinal)
            && !exported.Contains("do-not-export", StringComparison.Ordinal), "export includes inventory and supported mirrors and excludes unrelated workspace environment");
        Check(((Tomlyn.Model.TomlTable)exportTree["tool_alias"]!)["custom"] is "github:owner/tool" &&
            ((Tomlyn.Model.TomlTable)((Tomlyn.Model.TomlTable)exportTree["alias"]!)["rust"]!)["pinned"] is "1.90.1",
            "export keeps workspace backend and version aliases");
        bool proxyNotified = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.ProxyStatus)) proxyNotified = true; };
        vm.Settings.ProxyMode = ProxyMode.Custom; vm.Settings.ProxyAddress = "http://127.0.0.1:7890";
        Check(proxyNotified && vm.ProxyStatus == "自定义代理", "editing proxy mode immediately refreshes dependent status bindings");
        await Execute(vm.TestProxyCommand);
        Check(installer.LastProxy == ProxyMode.Custom, "proxy test receives selected proxy settings");
        await Execute(vm.CheckAppUpdateCommand);
        Check(vm.HasAppUpdate && vm.AppUpdateText.Contains("0.2.0"), "application update availability is displayed");
        bool ready = false; vm.ApplicationUpdateReady += _ => ready = true;
        await Execute(vm.ApplyAppUpdateCommand);
        Check(ready && updater.Stages == 1, "verified staged update raises lifecycle event");
        vm.Settings.AutoUpdateTools = true; vm.Settings.AutoUpdateMise = true;
        vm.Settings.LastToolsUpdateUtc = vm.Settings.LastMiseUpdateUtc = DateTimeOffset.UtcNow.AddDays(-2);
        await vm.CheckAutomaticUpdatesAsync();
        Check(mise.SelfUpdates == 2 && mise.ToolUpdates == 2, "due enabled automatic updates execute");
        await vm.CheckAutomaticUpdatesAsync();
        Check(mise.SelfUpdates == 2 && mise.ToolUpdates == 2, "automatic update schedule does not repeat immediately");
        var arguments = CommandArguments.Parse("exec -- \"a b\" 'c d'");
        Check(arguments.SequenceEqual(new[] { "exec", "--", "a b", "c d" }), "command arguments preserve quoted tokens without a shell");
        try { CommandArguments.Parse("\"unclosed"); throw new Exception("Expected parser failure"); } catch (InvalidOperationException) { count++; }
        try { AppPaths.Resolve(new AppSettings { MiseDirectory = "C:relative" }, root); throw new Exception("Expected path failure"); } catch (InvalidOperationException) { count++; }
        await vm.ShutdownAsync();
        return count;
    }
    private static async Task Execute(ICommand command)
    {
        command.Execute(null);
        if (command is AsyncCommand async && async.ExecutionTask != null) await async.ExecutionTask;
    }
    private sealed class MemoryStore : ISettingsStore
    {
        public AppSettings? Saved;
        public TaskCompletionSource? PauseNextSave, SaveEntered;
        public AppSettings Load() => Saved?.Copy() ?? new();
        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            var pause = PauseNextSave; PauseNextSave = null;
            if (pause != null) { SaveEntered?.SetResult(); await pause.Task; }
            Saved = settings.Copy();
        }
    }
    private sealed class FixtureMise : IMiseService
    {
        public int SelfUpdates, ToolUpdates; public string LastSpec = "";
        public string[] LastBatch = [];
        public TaskCompletionSource? PauseBatch;
        public Task<string> GetVersionAsync(AppSettings s, CancellationToken ct = default) => Task.FromResult("2026.11.0");
        public Task<IReadOnlyList<ToolInfo>> GetToolsAsync(AppSettings s, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ToolInfo>>([new("node", "22.0.0", true, true)]);
        public Task<ProcessResult> RunAsync(AppSettings s, IReadOnlyList<string> arguments, Action<string>? log = null, CancellationToken cancellationToken = default) => Task.FromResult(new ProcessResult(0, "", ""));
        public Task UpgradeToolsAsync(AppSettings s, Action<string>? log = null, CancellationToken cancellationToken = default) { ToolUpdates++; return Task.CompletedTask; }
        public Task SelfUpdateAsync(AppSettings s, Action<string>? log = null, CancellationToken cancellationToken = default) { SelfUpdates++; return Task.CompletedTask; }
        public Task InstallToolAsync(AppSettings s, string spec, Action<string>? log = null, CancellationToken cancellationToken = default) { LastSpec = spec; return Task.CompletedTask; }
        public async Task InstallToolsAsync(AppSettings s, IReadOnlyList<ToolInstallRequest> requests, Action<string>? log = null, CancellationToken cancellationToken = default)
        {
            LastBatch = requests.Select(tool => tool.Specification).ToArray();
            LastSpec = LastBatch[0];
            if (PauseBatch == null) return;
            var pause = PauseBatch;
            PauseBatch = null;
            log?.Invoke("正在下载 Rust");
            await pause.Task.WaitAsync(cancellationToken);
        }
    }
    private sealed class FixtureInstaller : IMiseInstaller
    {
        public int InstallCalls; public ProxyMode LastProxy;
        public Task<string> InstallAsync(AppSettings s, Action<string>? log = null, CancellationToken cancellationToken = default) { InstallCalls++; return Task.FromResult("2026.11.0"); }
        public Task<string> TestProxyAsync(AppSettings s, CancellationToken ct = default) { LastProxy = s.ProxyMode; return Task.FromResult("fixture proxy connected"); }
    }
    private sealed class FixtureTerminal : ITerminalIntegration
    {
        public int Repairs;
        public bool IsConfigured(AppSettings settings) => Repairs != 0;
        public Task RepairAsync(AppSettings s, Action<string>? log = null, CancellationToken cancellationToken = default) { Repairs++; s.LastRegisteredPaths = ["registered-by-fixture"]; return Task.CompletedTask; }
        public void OpenTerminal(AppSettings settings) { }
    }
    private sealed class FixtureUpdater : IApplicationUpdater
    {
        public int Stages;
        public Task<AppRelease?> CheckAsync(AppSettings s, CancellationToken ct = default) => Task.FromResult<AppRelease?>(new("0.2.0", "fixture.exe", new Uri("https://example.invalid/fixture.exe"), new Uri("https://example.invalid/SHA256SUMS.txt")));
        public Task<StagedAppUpdate> StageAsync(AppRelease r, AppSettings s, Action<string>? log = null, CancellationToken cancellationToken = default) { Stages++; return Task.FromResult(new StagedAppUpdate("fixture.exe", new string('a', 64), r.Version)); }
        public void LaunchApply(StagedAppUpdate update) { }
    }
}
