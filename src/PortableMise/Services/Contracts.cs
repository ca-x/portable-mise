using PortableMise.Models;

namespace PortableMise.Services;

public interface ISettingsStore
{
    AppSettings Load();
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
public interface IMiseService
{
    Task<string> GetVersionAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ToolInfo>> GetToolsAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<ProcessResult> RunAsync(AppSettings settings, IReadOnlyList<string> arguments, Action<string>? log = null, CancellationToken cancellationToken = default);
    Task UpgradeToolsAsync(AppSettings settings, Action<string>? log = null, CancellationToken cancellationToken = default);
    Task SelfUpdateAsync(AppSettings settings, Action<string>? log = null, CancellationToken cancellationToken = default);
    Task InstallToolAsync(AppSettings settings, string specification, Action<string>? log = null, CancellationToken cancellationToken = default);
}
public interface IMiseInstaller
{
    Task<string> InstallAsync(AppSettings settings, Action<string>? log = null, CancellationToken cancellationToken = default);
    Task<string> TestProxyAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
public interface ITerminalIntegration
{
    bool IsConfigured(AppSettings settings);
    Task RepairAsync(AppSettings settings, Action<string>? log = null, CancellationToken cancellationToken = default);
    void OpenTerminal(AppSettings settings);
}
public interface IApplicationUpdater
{
    Task<AppRelease?> CheckAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<StagedAppUpdate> StageAsync(AppRelease release, AppSettings settings, Action<string>? log = null, CancellationToken cancellationToken = default);
    void LaunchApply(StagedAppUpdate update);
}
