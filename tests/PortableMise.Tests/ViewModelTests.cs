using System.Windows.Input;
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
        public Task<string> GetVersionAsync(AppSettings s, CancellationToken ct = default) => Task.FromResult("2026.11.0");
        public Task<IReadOnlyList<ToolInfo>> GetToolsAsync(AppSettings s, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ToolInfo>>([new("node", "22.0.0", true, true)]);
        public Task<ProcessResult> RunAsync(AppSettings s, IReadOnlyList<string> arguments, Action<string>? log = null, CancellationToken cancellationToken = default) => Task.FromResult(new ProcessResult(0, "", ""));
        public Task UpgradeToolsAsync(AppSettings s, Action<string>? log = null, CancellationToken cancellationToken = default) { ToolUpdates++; return Task.CompletedTask; }
        public Task SelfUpdateAsync(AppSettings s, Action<string>? log = null, CancellationToken cancellationToken = default) { SelfUpdates++; return Task.CompletedTask; }
        public Task InstallToolAsync(AppSettings s, string spec, Action<string>? log = null, CancellationToken cancellationToken = default) { LastSpec = spec; return Task.CompletedTask; }
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
