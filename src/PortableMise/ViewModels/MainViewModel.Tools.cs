using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using PortableMise.Localization;
using PortableMise.Models;
using PortableMise.Services;

namespace PortableMise.ViewModels;

public sealed class QueuedTool(ToolInstallRequest request)
{
    public ToolInstallRequest Request { get; } = request;
    public string Label => Request.Options is { Count: > 0 } ? Request.Specification + "  ·  " + Request.Options.Count : Request.Specification;
}

public sealed partial class MainViewModel
{
    private readonly CoreToolDefinition languagePrompt = new("", UiText.T("添加语言"), "", []);
    private bool installingTools, toolEditorOpen;
    private string toolInstallStatus = UiText.T("等待安装");
    private string toolInstallLine = "";

    public ObservableCollection<QueuedTool> QueuedTools { get; } = [];
    public bool HasQueuedTools => QueuedTools.Count > 0;
    public IReadOnlyList<CoreToolDefinition> CoreLanguageChoices { get; private set; } = [];
    public CoreToolDefinition SelectedLanguage
    {
        get => languagePrompt;
        set
        {
            if (value == null || value.Id.Length == 0) return;
            TryAction(() => AddToQueue(value.Id));
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Notify(nameof(SelectedLanguage)));
        }
    }
    public ICommand QueueToolsCommand { get; private set; } = null!;
    public ICommand EditQueuedToolCommand { get; private set; } = null!;
    public ICommand RemoveQueuedToolCommand { get; private set; } = null!;
    public ICommand ExportToolsCommand { get; private set; } = null!;
    public ICommand ToolMaintenanceCommand { get; private set; } = null!;
    public ICommand LanguageMirrorsCommand { get; private set; } = null!;
    public bool IsInstallingTools { get => installingTools; private set => Set(ref installingTools, value); }
    public string ToolInstallStatus { get => toolInstallStatus; private set => Set(ref toolInstallStatus, value); }
    public string ToolInstallLine { get => toolInstallLine; private set => Set(ref toolInstallLine, value); }

    private void InitializeToolCommands()
    {
        CoreLanguageChoices = [languagePrompt, .. CoreToolCatalog.Languages];
        QueueToolsCommand = new RelayCommand(QueueFromEditor, () => !string.IsNullOrWhiteSpace(ToolSpecification));
        EditQueuedToolCommand = new QueueItemCommand(tool => TryAction(() => EditQueuedTool(tool)));
        RemoveQueuedToolCommand = new QueueItemCommand(tool => QueuedTools.Remove(tool));
        InstallToolCommand = new AsyncCommand(
            () => RunOperationAsync("安装开发工具", InstallQueuedToolsAsync), ReportError,
            () => CanOperate && IsInstalled && (QueuedTools.Count > 0 || !string.IsNullOrWhiteSpace(ToolSpecification)));
        ExportToolsCommand = new AsyncCommand(() => RunOperationAsync("导出工具", ExportToolsAsync), ReportError,
            () => CanOperate && IsInstalled);
        LanguageMirrorsCommand = Action("语言镜像", OpenLanguageMirrorsAsync);
        ToolMaintenanceCommand = Action("工具配置与维护", async (settings, cancellationToken) =>
        {
            var window = new ToolMaintenanceDialog(AppPaths.Resolve(settings, appDirectory), settings, mise)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
            if (window.AppliedChanges)
                await RefreshCoreAsync(settings, cancellationToken);
        });
        QueuedTools.CollectionChanged += (_, _) =>
        {
            Notify(nameof(HasQueuedTools));
            CommandManager.InvalidateRequerySuggested();
        };
    }

    private Task OpenLanguageMirrorsAsync(AppSettings snapshot, CancellationToken cancellationToken)
    {
        var window = new LanguageMirrorsDialog(AppPaths.Resolve(snapshot, appDirectory), snapshot, mise)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        using var cancel = cancellationToken.Register(() => window.Dispatcher.BeginInvoke(() =>
        {
            if (window.IsVisible) window.Close();
        }));
        window.ShowDialog();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private void QueueFromEditor()
    {
        try { QueueToolInput(); }
        catch (InvalidOperationException error)
        {
            if (!IsInstallingTools)
            {
                ToolInstallStatus = UiText.T("请检查输入");
                ToolInstallLine = error.Message;
            }
            ReportError(error);
        }
    }

    private void QueueToolInput()
    {
        AddToQueue(ToolSpecification);
        ToolSpecification = "";
    }

    private void EditQueuedTool(QueuedTool tool)
    {
        int index = QueuedTools.IndexOf(tool);
        if (index < 0) return;
        var snapshot = Settings.Copy();
        var editable = tool.Request;
        var definition = CoreToolCatalog.Find(ToolSpecifications.ToolName(editable.Specification));
        var window = new ToolOptionsDialog(editable.Specification, editable.Options ?? new Dictionary<string, string>(), definition,
            AppPaths.Resolve(snapshot, appDirectory), snapshot, mise, CanOperate)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        toolEditorOpen = true;
        try { if (window.ShowDialog() != true) return; }
        finally { toolEditorOpen = false; }
        var replacement = new ToolInstallRequest(window.Specification, new Dictionary<string, string>(window.Options, StringComparer.Ordinal));
        ToolSpecifications.InstallArguments([replacement]);
        bool needsCompanion = definition?.Id == "rust"
            && replacement.Options?.GetValueOrDefault("mr_boxington") == "true"
            && !QueuedTools.Any(item => ToolSpecifications.ToolName(item.Request.Specification) == "mr-boxington");
        if (needsCompanion && QueuedTools.Count == ToolSpecifications.QueueLimit)
            throw new InvalidOperationException(UiText.T("请先腾出一个队列位置，以便加入 mr-boxington。"));
        index = QueuedTools.IndexOf(tool);
        // The batch can finish while the editor is open. An edited request is a new pending item.
        if (index < 0)
        {
            if (QueuedTools.Count + (needsCompanion ? 2 : 1) > ToolSpecifications.QueueLimit)
                throw new InvalidOperationException(UiText.T("队列已满。"));
            QueuedTools.Add(new QueuedTool(replacement));
        }
        else
            QueuedTools[index] = new QueuedTool(replacement);
        if (needsCompanion) QueuedTools.Add(new QueuedTool(new ToolInstallRequest("mr-boxington")));
    }

    private void AddToQueue(string input)
    {
        var plain = QueuedTools.Where(item => item.Request.Options is not { Count: > 0 }).Select(item => item.Request.Specification)
            .ToHashSet(StringComparer.Ordinal);
        var additions = ToolSpecifications.Parse(input).Where(plain.Add).Select(ToolSpecifications.ToEditable).ToArray();
        if (QueuedTools.Count + additions.Length > ToolSpecifications.QueueLimit)
            throw new InvalidOperationException(UiText.F("队列最多容纳 {0} 个工具。", ToolSpecifications.QueueLimit));
        foreach (var request in additions)
            QueuedTools.Add(new QueuedTool(request));
    }

    private async Task InstallQueuedToolsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        try { QueueToolInput(); }
        catch (InvalidOperationException error)
        {
            ToolInstallStatus = UiText.T("请检查输入");
            ToolInstallLine = error.Message;
            throw;
        }
        var batch = QueuedTools.ToArray();
        if (batch.Length == 0) return;
        IsInstallingTools = true;
        ToolInstallLine = "";
        ToolInstallStatus = UiText.F("正在安装 {0} 项", batch.Length);
        try
        {
            try
            {
                await mise.InstallToolsAsync(settings, batch.Select(tool => tool.Request).ToArray(),
                    LogToolInstall, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ToolInstallStatus = UiText.T("安装已取消");
                throw;
            }
            catch (Exception)
            {
                ToolInstallStatus = UiText.T("批次未完成 · 可重试");
                // mise can install some tools before surfacing a failure.
                try { await RefreshCoreAsync(settings, cancellationToken); }
                catch (Exception error) { Log(error.Message); }
                throw;
            }
            foreach (var tool in batch)
                QueuedTools.Remove(tool);
            completionStatus = UiText.F("已安装 {0}。", SummarizeSpecs(batch.Select(tool => tool.Request.Specification)));
            ToolInstallStatus = completionStatus;
            await RefreshCoreAsync(settings, cancellationToken);
        }
        finally { IsInstallingTools = false; }
    }

    private void LogToolInstall(string line) => Ui(() =>
    {
        Log(line);
        string output = ProxySupport.Sanitize(line).Trim();
        if (output.Length > 0)
            ToolInstallLine = output;
    });

    private async Task ExportToolsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var paths = AppPaths.Resolve(settings, appDirectory);
        var configs = await mise.RunAsync(settings, ["config", "ls", "--json"], cancellationToken: cancellationToken);
        configs.EnsureSuccess();
        var installedTools = await mise.GetToolsAsync(settings, cancellationToken);
        var files = ToolExport.WorkspaceFiles(paths, configs.StandardOutput);
        string content = ToolExport.Create(files, installedTools, Path.Combine(paths.ConfigDirectory, "cargo", "config.toml"));
        var dialog = new ToolExportDialog(content)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }

    private sealed class QueueItemCommand(Action<QueuedTool> action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => parameter is QueuedTool;
        public void Execute(object? parameter) { if (parameter is QueuedTool tool) action(tool); }
    }
}
