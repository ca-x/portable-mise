using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using PortableMise.Localization;
using PortableMise.Models;
using PortableMise.Services;

namespace PortableMise;

public partial class ToolMaintenanceDialog : Window
{
    private const string MiseExample = """
        # [env]
        # RUSTUP_DIST_SERVER = "https://rsproxy.cn"
        # RUSTUP_UPDATE_ROOT = "https://rsproxy.cn/rustup"
        # CARGO_HOME = "{{ env.MISE_CONFIG_DIR }}/cargo"

        # [tools]
        # node = { version = "24", postinstall = "node --version" }
        # rust = { version = "stable", mr_boxington = true }
        # mr-boxington = "latest"

        # [hooks]
        # preinstall = "echo Preparing tools"
        # postinstall = "echo Tools installed"

        # [bootstrap.packages]
        # "winget:Git.Git" = "latest"
        """;

    private const string CargoExample = """
        [source.crates-io]
        replace-with = "rsproxy-sparse"

        [source.rsproxy-sparse]
        registry = "sparse+https://rsproxy.cn/index/"
        """;

    private const string Activation = "(&mise activate pwsh) | Out-String | Invoke-Expression";

    private sealed record Operation(string[] Arguments, string Hint, bool CanApply);

    private static readonly Operation DeclaredPackages = new(
        ["bootstrap", "packages", "apply", "--manager", "winget"],
        "仅处理 mise 配置中声明的 WinGet 软件包。安装会交给 Windows 软件包管理器。", true);

    private static readonly Operation UserTools = new(
        ["prune", "--tools"],
        "按 mise 已知的配置清理当前用户目录中未使用的工具版本。", true);

    private static readonly Operation SharedTools = new(
        ["prune", "--system", "--tools"],
        "共享安装可能仍被其他账户使用。这里只查看候选项。", false);

    private readonly AppPaths paths;
    private readonly AppSettings settings;
    private readonly IMiseService mise;
    private CancellationTokenSource? activeOperation;
    private Operation? previewedOperation;
    private string? configurationAtPreview;
    private bool closeRequested;

    public bool AppliedChanges { get; private set; }

    public ToolMaintenanceDialog(AppPaths paths, AppSettings settings, IMiseService mise)
    {
        this.paths = paths;
        this.settings = settings.Copy();
        this.mise = mise;
        InitializeComponent();
        OperationPicker.SelectedIndex = 0;
    }

    private Operation CurrentOperation => OperationPicker.SelectedValue switch
    {
        "UserPrune" => UserTools,
        "SystemPrune" => SharedTools,
        _ => DeclaredPackages
    };

    private string MiseConfigPath => Path.Combine(paths.ConfigDirectory, "config.toml");
    private string CargoConfigPath => Path.Combine(paths.ConfigDirectory, "cargo", "config.toml");

    private void OnOperationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OperationHint is null) return;
        OperationHint.Text = UiText.T(CurrentOperation.Hint);
        OutputBox.Clear();
        OutputBox.Visibility = Visibility.Collapsed;
        StatusText.Text = "";
        ClearPreview();
    }

    private void ClearPreview()
    {
        previewedOperation = null;
        configurationAtPreview = null;
        ApplyButton.IsEnabled = false;
    }

    private async void PreviewOperation(object sender, RoutedEventArgs e) => await RunOperationAsync(preview: true);
    private async void ApplyOperation(object sender, RoutedEventArgs e) => await RunOperationAsync(preview: false);

    private async Task RunOperationAsync(bool preview)
    {
        if (activeOperation is not null) return;
        var operation = CurrentOperation;
        if (!preview && (!operation.CanApply || previewedOperation != operation)) return;

        var tokenSource = new CancellationTokenSource();
        activeOperation = tokenSource;
        string? expectedConfiguration = configurationAtPreview;
        ClearPreview();
        SetBusy(true);
        OutputBox.Clear();
        OutputBox.Visibility = Visibility.Visible;
        StatusText.Text = preview ? UiText.T("正在预览…") : UiText.T("正在执行…");
        try
        {
            string configuration = ReadConfigurationFingerprint();
            if (!preview && configuration != expectedConfiguration)
                throw new InvalidOperationException(UiText.T("mise 配置已更改，请重新预览。"));

            string[] arguments = [.. operation.Arguments, preview ? "--dry-run" : "--yes"];
            if (!preview) AppliedChanges = true;
            var result = await mise.RunAsync(settings, arguments, cancellationToken: tokenSource.Token);
            tokenSource.Token.ThrowIfCancellationRequested();
            OutputBox.Text = FormatOutput(result);
            result.EnsureSuccess();

            if (preview && operation.CanApply)
            {
                if (configuration != ReadConfigurationFingerprint())
                    throw new InvalidOperationException(UiText.T("预览期间 mise 配置已更改，请重新预览。"));
                previewedOperation = operation;
                configurationAtPreview = configuration;
            }
            StatusText.Text = preview ? UiText.T("预览完成。") : UiText.T("执行完成。");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = UiText.T("操作已取消。");
        }
        catch (Exception error)
        {
            StatusText.Text = ProxySupport.Sanitize(error.Message);
        }
        finally
        {
            EndOperation(tokenSource);
        }
    }

    private async void SaveRustMirrors(object sender, RoutedEventArgs e)
    {
        if (activeOperation is not null) return;

        string dist = RustDistServerBox.Text.Trim();
        string update = RustUpdateRootBox.Text.Trim();
        if (dist.Length == 0 && update.Length == 0)
        {
            StatusText.Text = UiText.T("请填写至少一个镜像地址。");
            return;
        }

        var arguments = new List<string> { "set", "--file", MiseConfigPath };
        try
        {
            if (dist.Length > 0) arguments.Add("RUSTUP_DIST_SERVER=" + ParseMirrorAddress(dist));
            if (update.Length > 0) arguments.Add("RUSTUP_UPDATE_ROOT=" + ParseMirrorAddress(update));
        }
        catch (InvalidOperationException error)
        {
            StatusText.Text = error.Message;
            return;
        }

        var tokenSource = new CancellationTokenSource();
        activeOperation = tokenSource;
        ClearPreview();
        SetBusy(true);
        OutputBox.Clear();
        OutputBox.Visibility = Visibility.Collapsed;
        StatusText.Text = UiText.T("正在保存 Rust 镜像…");
        try
        {
            var result = await mise.RunAsync(settings, arguments, cancellationToken: tokenSource.Token);
            tokenSource.Token.ThrowIfCancellationRequested();
            result.EnsureSuccess();
            StatusText.Text = UiText.T("Rust 镜像已保存到当前工作区。");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = UiText.T("操作已取消。");
        }
        catch (Exception error)
        {
            StatusText.Text = ProxySupport.Sanitize(error.Message);
        }
        finally
        {
            EndOperation(tokenSource);
        }
    }

    private static string ParseMirrorAddress(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") ||
            string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidOperationException(UiText.T("请输入 HTTP 或 HTTPS 镜像地址，地址不能含有账号、查询或片段。"));

        return uri.AbsoluteUri.TrimEnd('/');
    }

    private void EndOperation(CancellationTokenSource tokenSource)
    {
        activeOperation = null;
        tokenSource.Dispose();
        SetBusy(false);
        if (closeRequested) Close();
    }

    private string ReadConfigurationFingerprint()
    {
        var files = Directory.Exists(paths.ConfigDirectory)
            ? Directory.EnumerateFiles(paths.ConfigDirectory, "*.toml")
            : [];
        var fragments = Path.Combine(paths.ConfigDirectory, "conf.d");
        if (Directory.Exists(fragments)) files = files.Concat(Directory.EnumerateFiles(fragments, "*.toml"));

        return string.Join('\n', files.Order(StringComparer.OrdinalIgnoreCase).Select(file =>
        {
            using var stream = File.OpenRead(file);
            return file + " " + Convert.ToHexString(SHA256.HashData(stream));
        }));
    }

    private static string FormatOutput(ProcessResult result)
    {
        string output = string.Join(Environment.NewLine, new[] { result.StandardOutput.Trim(), result.StandardError.Trim() }
            .Where(part => part.Length > 0));
        return output.Length == 0 ? UiText.T("没有输出。") : ProxySupport.Sanitize(output);
    }

    private void SetBusy(bool busy)
    {
        OperationPicker.IsEnabled = PreviewButton.IsEnabled = !busy;
        RustMirrorFields.IsEnabled = !busy;
        ApplyButton.IsEnabled = !busy && previewedOperation == CurrentOperation;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CancelOperation(object sender, RoutedEventArgs e) => activeOperation?.Cancel();
    private void CloseDialog(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (activeOperation is null) return;
        e.Cancel = true;
        closeRequested = true;
        activeOperation.Cancel();
    }

    private void OpenMiseConfig(object sender, RoutedEventArgs e) => OpenConfig(MiseConfigPath, UiText.T("mise 全局配置"));
    private void OpenCargoConfig(object sender, RoutedEventArgs e) => OpenConfig(CargoConfigPath, UiText.T("便携 Cargo 配置"));

    private void OpenConfig(string file, string name)
    {
        if (activeOperation is not null) return;
        ClearPreview();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            try { using var unused = new FileStream(file, FileMode.CreateNew, FileAccess.Write); }
            catch (IOException) when (File.Exists(file)) { }

            var editor = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "notepad.exe")) { UseShellExecute = false };
            editor.ArgumentList.Add(file);
            using var process = Process.Start(editor) ?? throw new InvalidOperationException(UiText.T("无法启动记事本。"));
            StatusText.Text = UiText.F("已打开{0}。", name);
        }
        catch (Exception error)
        {
            StatusText.Text = ProxySupport.Sanitize(error.Message);
        }
    }

    private void CopyMiseExample(object sender, RoutedEventArgs e) => Copy(
        "# " + UiText.T("将所需示例合并到现有区段，再取消相关注释。") + Environment.NewLine + MiseExample);
    private void CopyCargoExample(object sender, RoutedEventArgs e) => Copy(CargoExample);
    private void CopyPowerShellActivation(object sender, RoutedEventArgs e) => Copy(Activation);

    private void Copy(string value)
    {
        try
        {
            Clipboard.SetText(value);
            StatusText.Text = UiText.T("已复制到剪贴板。");
        }
        catch (ExternalException)
        {
            StatusText.Text = UiText.T("剪贴板暂不可用，请重试。");
        }
    }

    private void OpenDocumentation(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try { using var browser = Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) { StatusText.Text = ProxySupport.Sanitize(error.Message); }
    }
}
