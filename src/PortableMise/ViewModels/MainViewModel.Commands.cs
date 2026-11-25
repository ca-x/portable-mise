using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PortableMise.Models;
using PortableMise.Services;

namespace PortableMise.ViewModels;

public sealed partial class MainViewModel
{
    public ICommand InstallCommand { get; private set; } = null!;
    public ICommand RestoreCommand { get; private set; } = null!;
    public ICommand RefreshCommand { get; private set; } = null!;
    public ICommand UpgradeToolsCommand { get; private set; } = null!;
    public ICommand UpdateMiseCommand { get; private set; } = null!;
    public ICommand UpdateAllCommand { get; private set; } = null!;
    public ICommand TestProxyCommand { get; private set; } = null!;
    public ICommand SaveSettingsCommand { get; private set; } = null!;
    public ICommand BrowseMiseDirectoryCommand { get; private set; } = null!;
    public ICommand BrowseToolsDirectoryCommand { get; private set; } = null!;
    public ICommand OpenTerminalCommand { get; private set; } = null!;
    public ICommand InstallToolCommand { get; private set; } = null!;
    public ICommand RunMiseCommand { get; private set; } = null!;
    public ICommand CancelCommand { get; private set; } = null!;
    public ICommand CheckAppUpdateCommand { get; private set; } = null!;
    public ICommand ApplyAppUpdateCommand { get; private set; } = null!;
    public ICommand OpenDataFolderCommand { get; private set; } = null!;
    public ICommand OpenDocsCommand { get; private set; } = null!;
    public ICommand ClearLogsCommand { get; private set; } = null!;
    public ICommand ShowLicenseCommand { get; private set; } = null!;
    public ICommand ShowOverviewCommand { get; private set; } = null!;
    public ICommand ShowToolsCommand { get; private set; } = null!;
    public ICommand ShowTerminalCommand { get; private set; } = null!;
    public ICommand ShowUpdatesCommand { get; private set; } = null!;
    public ICommand ShowSettingsCommand { get; private set; } = null!;

    private AsyncCommand Action(string name, Func<AppSettings, CancellationToken, Task> action) => new(() => RunOperationAsync(name, action), ReportError, () => CanOperate);
    private void InitializeCommands()
    {
        InstallCommand = Action("安装 mise 并启用终端", async (s, ct) => {
            await installer.InstallAsync(s, Log, ct); await terminal.RepairAsync(s, Log, ct); await CommitSettingsAsync(s, ct); await RefreshCoreAsync(s, ct);
        });
        RestoreCommand = Action("恢复终端环境", async (s, ct) => {
            await terminal.RepairAsync(s, Log, ct); await CommitSettingsAsync(s, ct); await RefreshCoreAsync(s, ct);
        });
        RefreshCommand = Action("刷新环境状态", RefreshCoreAsync);
        UpgradeToolsCommand = Action("更新开发工具", async (s, ct) => { await mise.UpgradeToolsAsync(s, Log, ct); s.LastToolsUpdateUtc = DateTimeOffset.UtcNow; await CommitSettingsAsync(s, ct); await RefreshCoreAsync(s, ct); });
        UpdateMiseCommand = Action("更新 mise", async (s, ct) => { await mise.SelfUpdateAsync(s, Log, ct); s.LastMiseUpdateUtc = DateTimeOffset.UtcNow; await CommitSettingsAsync(s, ct); await RefreshCoreAsync(s, ct); });
        UpdateAllCommand = Action("更新 mise 和工具", async (s, ct) => { await mise.SelfUpdateAsync(s, Log, ct); await mise.UpgradeToolsAsync(s, Log, ct); s.LastMiseUpdateUtc = s.LastToolsUpdateUtc = DateTimeOffset.UtcNow; await CommitSettingsAsync(s, ct); await RefreshCoreAsync(s, ct); });
        TestProxyCommand = Action("测试代理连接", async (s, ct) => Log(await installer.TestProxyAsync(s, ct)));
        SaveSettingsCommand = Action("保存设置", async (s, ct) => { await CommitSettingsAsync(s, ct); await RefreshCoreAsync(s, ct); Log("设置已保存。"); });
        InstallToolCommand = Action("安装开发工具", async (s, ct) => { await mise.InstallToolAsync(s, ToolSpecification, Log, ct); await RefreshCoreAsync(s, ct); });
        RunMiseCommand = Action("运行 mise", async (s, ct) => { var result = await mise.RunAsync(s, CommandArguments.Parse(CommandText), Log, ct); result.EnsureSuccess(); });
        CheckAppUpdateCommand = Action("检查桌面程序更新", CheckApplicationUpdateAsync);
        ApplyAppUpdateCommand = Action("更新桌面程序", StageApplicationUpdateAsync);
        BrowseMiseDirectoryCommand = new RelayCommand(() => BrowsePath(true), () => CanOperate);
        BrowseToolsDirectoryCommand = new RelayCommand(() => BrowsePath(false), () => CanOperate);
        OpenTerminalCommand = new RelayCommand(() => TryAction(() => terminal.OpenTerminal(Settings.Copy())), () => CanOperate);
        OpenDataFolderCommand = new RelayCommand(() => TryAction(() => OpenLocation(appDirectory)));
        OpenDocsCommand = new RelayCommand(() => TryAction(() => OpenLocation("https://mise.jdx.dev/installing-mise.html#windows")));
        ClearLogsCommand = new RelayCommand(() => LogText = "");
        ShowLicenseCommand = new RelayCommand(() => TryAction(ShowLicense));
        CancelCommand = new RelayCommand(() => operation?.Cancel(), () => IsBusy);
        ShowOverviewCommand = new RelayCommand(() => CurrentPage = 0);
        ShowToolsCommand = new RelayCommand(() => CurrentPage = 1);
        ShowTerminalCommand = new RelayCommand(() => CurrentPage = 2);
        ShowUpdatesCommand = new RelayCommand(() => CurrentPage = 3);
        ShowSettingsCommand = new RelayCommand(() => CurrentPage = 4);
    }
    private void TryAction(Action action) { try { action(); } catch (Exception error) { ReportError(error); } }
    private static void OpenLocation(string location) => Process.Start(new ProcessStartInfo(location) { UseShellExecute = true });
    private static void ShowLicense()
    {
        var assembly = typeof(MainViewModel).Assembly;
        string Read(string resource) { using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("未找到许可信息。"); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        string licenses = string.Join(Environment.NewLine + Environment.NewLine, assembly.GetManifestResourceNames().Where(name => name.StartsWith("PortableMise.Licenses.", StringComparison.Ordinal)).OrderBy(name => name).Select(Read));
        var text = new System.Windows.Controls.TextBox { Text = Read("PortableMise.License.txt") + Environment.NewLine + Read("PortableMise.Notices.txt") + Environment.NewLine + licenses, IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, Margin = new Thickness(16), BorderThickness = new Thickness(0), Height = double.NaN, VerticalAlignment = VerticalAlignment.Stretch };
        new Window { Title = "许可证", Width = 680, Height = 540, MinWidth = 480, MinHeight = 360, Content = text, Owner = System.Windows.Application.Current?.MainWindow,
            WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
    }
    private void BrowsePath(bool binary)
    {
        try {
            var dialog = new OpenFolderDialog { Title = binary ? "选择 mise 所在目录" : "选择工具数据目录", Multiselect = false };
            if (dialog.ShowDialog() != true) return;
            var copy = Settings.Copy();
            string selected = AppPaths.PortablePath(dialog.FolderName, appDirectory);
            if (binary) copy.MiseDirectory = selected; else copy.ToolsDirectory = selected;
            Settings = copy;
        } catch (Exception error) { ReportError(error); }
    }
}
