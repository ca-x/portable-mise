using System.Windows;
using PortableMise.Models;
using PortableMise.Localization;
using PortableMise.Services;

namespace PortableMise.ViewModels;

public sealed partial class MainViewModel
{
    public Task InitializeAsync() => RunOperationAsync("检查工作区", RefreshCoreAsync);

    private async Task RunOperationAsync(string title, Func<AppSettings, CancellationToken, Task> action)
    {
        if (!await gate.WaitAsync(0)) return;
        operation = new CancellationTokenSource();
        IsBusy = true;
        title = UiText.T(title);
        StatusText = title;
        StatusDetail = UiText.T("正在处理，可在终端页查看实时记录。");
        Log("── " + title + " ──");
        try
        {
            var snapshot = Settings.Copy();
            AppPaths.Resolve(snapshot, appDirectory);
            ProxySupport.Validate(snapshot);
            NotifyPaths();
            await action(snapshot, operation.Token);
            StatusText = UiText.T("操作完成");
            StatusDetail = UiText.F("{0}已完成。", title);
        }
        catch (OperationCanceledException) { StatusText = UiText.T("已取消"); StatusDetail = UiText.T("本次操作已停止。"); Log(UiText.T("操作已取消。")); }
        catch (Exception error) { ReportError(error); }
        finally
        {
            LastOperation = DateTimeOffset.Now.ToString("HH:mm") + " · " + title;
            IsBusy = false;
            operation.Dispose(); operation = null; gate.Release();
        }
    }

    private async Task CommitSettingsAsync(AppSettings snapshot, CancellationToken ct)
    {
        var paths = AppPaths.Resolve(snapshot, appDirectory);
        snapshot.MiseDirectory = AppPaths.PortablePath(paths.MiseRoot, appDirectory);
        snapshot.ToolsDirectory = AppPaths.PortablePath(paths.ToolsRoot, appDirectory);
        if (snapshot.UpdateIntervalHours < 1 || snapshot.UpdateIntervalHours > 168)
            throw new InvalidOperationException(UiText.T("自动更新间隔请设置为 1 到 168 小时。"));
        await settingsGate.WaitAsync(ct);
        try
        {
            snapshot.AskBeforeClosing = Settings.AskBeforeClosing;
            snapshot.CloseToTray = Settings.CloseToTray;
            await store.SaveAsync(snapshot, ct);
            Settings = snapshot;
            Notify(nameof(CloseBehaviorChoice));
        }
        finally { settingsGate.Release(); }
    }

    public async Task RememberCloseChoiceAsync(bool closeToTray)
    {
        await settingsGate.WaitAsync();
        try
        {
            var snapshot = Settings.Copy();
            snapshot.CloseToTray = closeToTray;
            snapshot.AskBeforeClosing = false;
            await store.SaveAsync(snapshot);
            Settings = snapshot;
            Notify(nameof(CloseBehaviorChoice));
        }
        finally { settingsGate.Release(); }
    }

    private async Task RefreshCoreAsync(AppSettings snapshot, CancellationToken ct)
    {
        var paths = AppPaths.Resolve(snapshot, appDirectory);
        IsInstalled = File.Exists(paths.MiseExecutable) && File.Exists(paths.MiseShimExecutable);
        string versionOutput = IsInstalled ? await mise.GetVersionAsync(snapshot, ct) : "";
        MiseVersion = IsInstalled ? versionOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? versionOutput
            : UiText.T("未安装");
        var tools = IsInstalled ? await mise.GetToolsAsync(snapshot, ct) : [];
        Tools.Clear();
        foreach (var tool in tools) Tools.Add(tool);
        Notify(nameof(ToolCount)); NotifyPaths();
        bool configured = IsInstalled && terminal.IsConfigured(snapshot);
        IsTerminalConfigured = configured;
        Log(IsInstalled ? UiText.F("mise {0}，发现 {1} 个工具版本。", MiseVersion, Tools.Count) : UiText.T("尚未安装 mise，点击一键安装开始。"));
        if (IsInstalled && !configured) Log(UiText.T("当前终端环境尚未登记或目录已移动；点击一键恢复即可重新登记。"));
    }

    private async Task CheckApplicationUpdateAsync(AppSettings snapshot, CancellationToken ct)
    {
        availableRelease = await updater.CheckAsync(snapshot, ct);
        HasAppUpdate = availableRelease != null;
        AppUpdateText = availableRelease == null ? UiText.T("暂无可用的新版本，或仓库尚未发布桌面程序。") : UiText.F("可更新到 {0}", availableRelease.Version);
        Log(AppUpdateText);
        snapshot.LastApplicationCheckUtc = DateTimeOffset.UtcNow;
        await CommitSettingsAsync(snapshot, ct);
    }

    private async Task StageApplicationUpdateAsync(AppSettings snapshot, CancellationToken ct)
    {
        if (availableRelease == null) await CheckApplicationUpdateAsync(snapshot, ct);
        if (availableRelease == null) return;
        var staged = await updater.StageAsync(availableRelease, snapshot, Log, ct);
        Log(UiText.T("新版本校验通过，正在准备重启更新。"));
        ApplicationUpdateReady?.Invoke(staged);
    }

    public async Task CheckAutomaticUpdatesAsync()
    {
        if (IsBusy || NeedsRestore || toolEditorOpen) return;
        var now = DateTimeOffset.UtcNow;
        if (lastAutomaticAttempt != null && now - lastAutomaticAttempt < TimeSpan.FromMinutes(15)) return;
        bool Due(DateTimeOffset? last) => last == null || now - last >= TimeSpan.FromHours(Math.Clamp(Settings.UpdateIntervalHours, 1, 168));
        bool updateMise = Settings.AutoUpdateMise && IsInstalled && Due(Settings.LastMiseUpdateUtc);
        bool updateTools = Settings.AutoUpdateTools && IsInstalled && Due(Settings.LastToolsUpdateUtc);
        bool updateApplication = Settings.AutoUpdateApplication && Due(Settings.LastApplicationCheckUtc);
        if (!updateMise && !updateTools && !updateApplication) return;
        lastAutomaticAttempt = now;
        await RunOperationAsync("后台自动更新", async (s, ct) => {
            if (updateMise) { await mise.SelfUpdateAsync(s, Log, ct); s.LastMiseUpdateUtc = now; }
            if (updateTools) { await mise.UpgradeToolsAsync(s, Log, ct); s.LastToolsUpdateUtc = now; }
            await CommitSettingsAsync(s, ct);
            if (updateMise || updateTools) await RefreshCoreAsync(s, ct);
            if (updateApplication) { await CheckApplicationUpdateAsync(s, ct); if (HasAppUpdate) await StageApplicationUpdateAsync(s, ct); }
        });
    }

    private static void Ui(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher?.HasShutdownStarted == true) return;
        if (dispatcher == null || dispatcher.CheckAccess()) action(); else dispatcher.BeginInvoke(action);
    }
    private void Log(string text) => Ui(() => {
        string line = DateTimeOffset.Now.ToString("HH:mm:ss") + "  " + ProxySupport.Sanitize(text) + Environment.NewLine;
        string updated = LogText + line;
        LogText = updated.Length <= 250_000 ? updated : updated[^200_000..];
    });
    private void ReportError(Exception error) => Ui(() => {
        StatusText = UiText.T("操作未完成");
        StatusDetail = ProxySupport.Sanitize(error.Message);
        Log(UiText.F("错误：{0}", error.Message));
    });
}
