using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using PortableMise.Models;
using PortableMise.Services;

namespace PortableMise.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly string appDirectory;
    private readonly ISettingsStore store;
    private readonly IMiseService mise;
    private readonly IMiseInstaller installer;
    private readonly ITerminalIntegration terminal;
    private readonly IApplicationUpdater updater;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim settingsGate = new(1, 1);
    private CancellationTokenSource? operation;
    private AppRelease? availableRelease;
    private DateTimeOffset? lastAutomaticAttempt;
    private AppSettings settings;
    private bool busy, installed, terminalConfigured, hasAppUpdate;
    private int page;
    private string version = "未安装", status = "就绪", detail = "";
    private string filter = "", specification = "", commandText = "doctor", logText = "";
    private string lastOperation = "尚无操作", appUpdateText = "尚未检查桌面程序更新。";

    public MainViewModel(string root, AppSettings initialSettings, ISettingsStore settingsStore, IMiseService miseService,
        IMiseInstaller miseInstaller, ITerminalIntegration terminalIntegration, IApplicationUpdater applicationUpdater)
    {
        appDirectory = Path.GetFullPath(root);
        settings = initialSettings;
        store = settingsStore; mise = miseService; installer = miseInstaller; terminal = terminalIntegration; updater = applicationUpdater;
        FilteredTools = CollectionViewSource.GetDefaultView(Tools);
        FilteredTools.Filter = item => item is ToolInfo tool && (string.IsNullOrWhiteSpace(ToolFilter) ||
            tool.Name.Contains(ToolFilter, StringComparison.OrdinalIgnoreCase) || tool.Version.Contains(ToolFilter, StringComparison.OrdinalIgnoreCase));
        InitializeCommands();
        settings.PropertyChanged += OnSettingsChanged;
    }

    public AppSettings Settings { get => settings; private set { settings.PropertyChanged -= OnSettingsChanged; settings = value; settings.PropertyChanged += OnSettingsChanged; Notify(); NotifyPaths(); } }
    public ObservableCollection<ToolInfo> Tools { get; } = [];
    public ICollectionView FilteredTools { get; }
    public int ToolCount => Tools.Count;
    public string MiseVersion { get => version; private set => Set(ref version, value); }
    public bool IsInstalled { get => installed; private set { if (Set(ref installed, value)) NotifyMode(); } }
    public bool IsTerminalConfigured { get => terminalConfigured; private set { if (Set(ref terminalConfigured, value)) NotifyMode(); } }
    public bool NeedsSetup => !IsInstalled;
    public bool NeedsRestore => IsInstalled && !IsTerminalConfigured;
    public bool IsEnvironmentReady => IsInstalled && IsTerminalConfigured;
    public string PrimaryActionText => NeedsSetup ? "安装 mise" : NeedsRestore ? "恢复环境" : "更新环境";
    public ICommand PrimaryActionCommand => NeedsSetup ? InstallCommand : NeedsRestore ? RestoreCommand : UpdateAllCommand;
    public bool IsBusy { get => busy; private set { Set(ref busy, value); Notify(nameof(CanOperate)); CommandManager.InvalidateRequerySuggested(); } }
    public bool CanOperate => !IsBusy;
    public string StatusText { get => status; private set => Set(ref status, value); }
    public string StatusDetail { get => detail; private set => Set(ref detail, value); }
    public string LastOperation { get => lastOperation; private set => Set(ref lastOperation, value); }
    public string LogText { get => logText; private set => Set(ref logText, value); }
    public string ToolFilter { get => filter; set { if (Set(ref filter, value)) FilteredTools.Refresh(); } }
    public string ToolSpecification { get => specification; set => Set(ref specification, value); }
    public string CommandText { get => commandText; set => Set(ref commandText, value); }
    public bool HasAppUpdate { get => hasAppUpdate; private set => Set(ref hasAppUpdate, value); }
    public string AppUpdateText { get => appUpdateText; private set => Set(ref appUpdateText, value); }
    public string ApplicationVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
    public string ResolvedMiseDirectory => ResolveDisplay(true);
    public string ResolvedToolsDirectory => ResolveDisplay(false);
    public string ProxyStatus => Settings.ProxyMode switch { ProxyMode.Custom => "自定义代理", ProxyMode.Disabled => "直连", _ => "系统代理" };
    public CloseBehavior CloseBehaviorChoice
    {
        get => Settings.AskBeforeClosing ? CloseBehavior.Ask : Settings.CloseToTray ? CloseBehavior.Tray : CloseBehavior.Exit;
        set
        {
            Settings.AskBeforeClosing = value == CloseBehavior.Ask;
            if (value != CloseBehavior.Ask) Settings.CloseToTray = value == CloseBehavior.Tray;
            Notify();
        }
    }
    public int CurrentPage { get => page; set { if (Set(ref page, value)) Notify(nameof(PageTitle)); } }
    public string PageTitle => CurrentPage switch { 1 => "工具", 2 => "终端", 3 => "程序更新", 4 => "设置", _ => "环境" };
    public event Action<StagedAppUpdate>? ApplicationUpdateReady;

    private string ResolveDisplay(bool binary)
    {
        try { var paths = AppPaths.Resolve(Settings, appDirectory); return binary ? paths.MiseRoot : paths.ToolsRoot; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { return "请检查路径设置"; }
    }
    private void NotifyPaths() { Notify(nameof(ResolvedMiseDirectory)); Notify(nameof(ResolvedToolsDirectory)); Notify(nameof(ProxyStatus)); }
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) { NotifyPaths(); Notify(nameof(CloseBehaviorChoice)); }
    private void NotifyMode()
    {
        Notify(nameof(NeedsSetup)); Notify(nameof(NeedsRestore)); Notify(nameof(IsEnvironmentReady));
        Notify(nameof(PrimaryActionText)); Notify(nameof(PrimaryActionCommand));
    }
    public async Task ShutdownAsync()
    {
        operation?.Cancel();
        await gate.WaitAsync();
        try { await settingsGate.WaitAsync(); settingsGate.Release(); }
        finally { gate.Release(); }
    }
    public void Dispose() { settings.PropertyChanged -= OnSettingsChanged; operation?.Cancel(); operation?.Dispose(); gate.Dispose(); settingsGate.Dispose(); }
}
