using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Navigation;
using PortableMise.Localization;
using PortableMise.Models;
using PortableMise.Services;

namespace PortableMise;

public partial class ToolOptionsDialog : Window
{
    private const string CargoField = "cargo-mirror";
    private readonly string? toolName;
    private readonly bool workspaceEditable;
    private readonly ToolWorkspaceConfiguration workspace;
    private readonly Dictionary<string, Func<string>> toolValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkspaceField> workspaceFields = new(StringComparer.Ordinal);
    private CancellationTokenSource? activeOperation;
    private bool sourceLoaded, closeRequested;

    public string Specification { get; private set; }
    public IReadOnlyDictionary<string, string> Options { get; private set; }

    public ToolOptionsDialog(string specification, IReadOnlyDictionary<string, string> options, CoreToolDefinition? definition,
        AppPaths paths, AppSettings settings, IMiseService mise, bool allowWorkspaceEdits)
    {
        Specification = specification;
        Options = options;
        workspaceEditable = allowWorkspaceEdits && File.Exists(paths.MiseExecutable);
        workspace = new ToolWorkspaceConfiguration(paths, settings, mise);
        if (definition is not null && !specification.Contains('['))
            toolName = specification.Split('@', 2)[0];

        InitializeComponent();
        Title = definition is null ? UiText.T("工具选项") : UiText.F("{0} 安装选项", definition.Label);
        bool rust = definition?.Id == "rust";
        SpecificationLabel.Text = UiText.T(toolName is null ? "工具规格" : rust ? "工具链" : "版本");
        SpecificationBox.Text = toolName is null ? specification : specification.Length > toolName.Length ? specification[(toolName.Length + 1)..] : "";
        SpecificationBox.ToolTip = UiText.T(rust ? "例如 stable、beta、nightly 或 1.88.0。留空时由 mise 选择。" : "留空时由 mise 选择。");
        AutomationProperties.SetName(SpecificationBox, SpecificationLabel.Text);
        ToolDocumentation.NavigateUri = new Uri(definition?.DocumentationUrl ?? "https://mise.jdx.dev/dev-tools/tool-options.html");
        ToolHint.Text = UiText.T(definition?.WorkspaceSettings ?? "");
        ToolHint.Visibility = ToolHint.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in definition?.Options ?? []) AddOption(SpecificOptionRows, option);
        foreach (var option in CoreToolCatalog.CommonOptions) AddOption(OtherOptionRows, option);
        foreach (string key in options.Keys)
            AddOption(OtherOptionRows, new ToolOptionDefinition(key, key, "工具专属选项"));

        foreach (var option in definition?.WorkspaceOptions ?? [])
        {
            AddWorkspaceOption(option);
            if (rust && option.Key == "env.RUSTUP_UPDATE_ROOT")
                AddWorkspaceOption(new WorkspaceOptionDefinition(CargoField, "Cargo 依赖镜像",
                    "例如 sparse+https://rsproxy.cn/index/。用于当前工作区，保留手动配置。", IsUrl: true));
        }
        WorkspaceCard.Visibility = workspaceFields.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceRows.IsEnabled = workspaceEditable;
        WorkspaceHint.Text = UiText.T(workspaceEditable ? "仅保存改动的非空项，设置会应用于当前便携工作区。" : "安装 mise 后，且没有任务运行时可修改。");

        void AddOption(Panel panel, ToolOptionDefinition option)
        {
            if (!defined.Add(option.Key)) return;
            var editor = CreateEditor(option.IsBoolean, option.Choices);
            editor.Write(options.GetValueOrDefault(option.Key) ?? "");
            toolValues.Add(option.Key, editor.Read);
            AddRow(panel, option.Label, option.Hint, editor.Control, $"ToolOption-{option.Key}");
        }
    }

    private sealed record Editor(FrameworkElement Control, Func<string> Read, Action<string> Write);
    private sealed record Choice(string Value, string Label);
    private sealed class WorkspaceField(WorkspaceOptionDefinition option, Editor editor)
    {
        public WorkspaceOptionDefinition Option { get; } = option;
        public Editor Editor { get; } = editor;
        public string Baseline { get; set; } = "";
    }

    private static Editor CreateEditor(bool boolean, string[]? choices)
    {
        if (boolean || choices is { Length: > 0 })
        {
            Choice[] values = boolean ? [new("", UiText.T("默认")), new("true", UiText.T("启用")), new("false", UiText.T("停用"))]
                : [new("", UiText.T("默认")), .. choices!.Select(value => new Choice(value, value))];
            var box = new ComboBox { DisplayMemberPath = nameof(Choice.Label), SelectedValuePath = nameof(Choice.Value) };
            var items = values.ToList();
            box.ItemsSource = items;
            box.SelectedIndex = 0;
            return new Editor(box, () => box.SelectedValue as string ?? "",
                value =>
                {
                    if (!items.Any(item => item.Value == value))
                    {
                        items.Add(new Choice(value, value));
                        box.Items.Refresh();
                    }
                    box.SelectedValue = value;
                });
        }
        var text = new TextBox();
        return new Editor(text, () => text.Text, value => text.Text = value);
    }

    private void AddWorkspaceOption(WorkspaceOptionDefinition option)
    {
        var editor = option.Key == "settings.go.download_mirror" ? CreateGoMirrorEditor() : CreateEditor(option.IsBoolean, option.Choices);
        workspaceFields.Add(option.Key, new WorkspaceField(option, editor));
        AddRow(WorkspaceRows, option.Label, option.Hint, editor.Control, $"WorkspaceOption-{option.Key}");
    }

    private static Editor CreateGoMirrorEditor()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
        var address = new TextBox { Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(address, UiText.T("Go 下载镜像地址"));
        AutomationProperties.SetAutomationId(address, "GoDownloadMirrorAddress");
        var presets = new ComboBox
        {
            DisplayMemberPath = nameof(Choice.Label),
            ItemsSource = new Choice[] { new("", UiText.T("选择预设")), new("https://dl.google.com/go", UiText.T("官方默认")), new("https://golang.google.cn/dl", UiText.T("Google 中国")) },
            SelectedIndex = 0
        };
        AutomationProperties.SetName(presets, UiText.T("Go 下载镜像预设"));
        presets.SelectionChanged += (_, _) =>
        {
            if (presets.SelectedItem is Choice { Value.Length: > 0 } selected)
                address.Text = selected.Value;
        };
        address.TextChanged += (_, _) =>
        {
            if (presets.SelectedItem is Choice { Value.Length: > 0 } selected && selected.Value != address.Text)
                presets.SelectedIndex = 0;
        };
        Grid.SetColumn(presets, 1);
        grid.Children.Add(address);
        grid.Children.Add(presets);
        return new Editor(grid, () => address.Text, value => { presets.SelectedIndex = 0; address.Text = value; });
    }

    private static void AddRow(Panel panel, string label, string hint, FrameworkElement editor, string automationId)
    {
        label = UiText.T(label);
        hint = UiText.T(hint);
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(144) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new TextBlock
        {
            Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
            ToolTip = hint, Margin = new Thickness(0, 0, 8, 0)
        });
        AutomationProperties.SetName(editor, label);
        AutomationProperties.SetAutomationId(editor, automationId);
        editor.ToolTip = hint;
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        panel.Children.Add(row);
    }

    private async void ExpandWorkspace(object sender, RoutedEventArgs e)
    {
        if (WorkspaceRows.Children.Count > 0 && WorkspaceRows.Children[0] is FrameworkElement firstRow)
            _ = Dispatcher.BeginInvoke(() => firstRow.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
        if (!workspaceEditable || sourceLoaded || activeOperation is not null) return;
        await RunWorkspaceAsync(async token =>
        {
            foreach (var field in workspaceFields.Values)
            {
                string? value = field.Option.Key == CargoField ? await workspace.ReadCargoMirrorAsync(token) : await workspace.ReadAsync(field.Option.Key, token);
                field.Baseline = value ?? "";
                field.Editor.Write(field.Baseline);
            }
            sourceLoaded = true;
            StatusText.Text = "";
        });
    }

    private async void SaveWorkspace(object sender, RoutedEventArgs e)
    {
        if (!workspaceEditable || !sourceLoaded || activeOperation is not null) return;
        var edits = new Dictionary<string, string>(StringComparer.Ordinal);
        string? cargoMirror = null;
        try
        {
            foreach (var field in workspaceFields.Values)
            {
                string value = field.Editor.Read().Trim();
                if (value.Length == 0 || value == field.Baseline) continue;
                if (value.Any(char.IsControl)) throw new InvalidOperationException(UiText.T("工作区设置须填写在同一行。"));
                if (field.Option.IsUrl) value = ToolWorkspaceConfiguration.ValidateMirror(value, field.Option.Key == CargoField);
                if (field.Option.Key == CargoField) cargoMirror = value;
                else edits.Add(field.Option.Key, value);
            }
        }
        catch (InvalidOperationException error)
        {
            StatusText.Text = error.Message;
            return;
        }
        if (edits.Count == 0 && cargoMirror is null)
        {
            StatusText.Text = UiText.T("没有需要保存的修改。");
            return;
        }
        await RunWorkspaceAsync(async token =>
        {
            await workspace.SaveAsync(edits, cargoMirror, token);
            foreach (var pair in edits) UpdateWorkspaceBaseline(pair.Key, pair.Value);
            if (cargoMirror is not null) UpdateWorkspaceBaseline(CargoField, cargoMirror);
            StatusText.Text = UiText.T("已保存到便携工作区。");
        });
    }

    private void UpdateWorkspaceBaseline(string key, string value)
    {
        workspaceFields[key].Baseline = value;
        workspaceFields[key].Editor.Write(value);
    }

    private async Task RunWorkspaceAsync(Func<CancellationToken, Task> operation)
    {
        var cancellation = new CancellationTokenSource();
        activeOperation = cancellation;
        WorkspaceRows.IsEnabled = SaveWorkspaceButton.IsEnabled = SaveTagButton.IsEnabled = false;
        StatusText.Text = UiText.T("正在处理工作区设置…");
        try { await operation(cancellation.Token); }
        catch (OperationCanceledException) { StatusText.Text = UiText.T("已取消。"); }
        catch (Exception error) { StatusText.Text = ProxySupport.Sanitize(error.Message); }
        finally
        {
            activeOperation = null;
            cancellation.Dispose();
            WorkspaceRows.IsEnabled = SaveWorkspaceButton.IsEnabled = sourceLoaded && workspaceEditable;
            SaveTagButton.IsEnabled = true;
            if (closeRequested) Close();
        }
    }

    private void SaveTag(object sender, RoutedEventArgs e)
    {
        if (activeOperation is not null) return;
        string version = SpecificationBox.Text.Trim();
        string specification = toolName is null ? version : version.Length == 0 ? toolName : $"{toolName}@{version}";
        try
        {
            ToolSpecifications.Validate(specification);
            Specification = specification;
            Options = toolValues.Select(pair => KeyValuePair.Create(pair.Key, pair.Value()))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).ToDictionary(StringComparer.Ordinal);
            DialogResult = true;
        }
        catch (InvalidOperationException error) { StatusText.Text = error.Message; }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (activeOperation is null) return;
        e.Cancel = true;
        closeRequested = true;
        activeOperation.Cancel();
    }

    private void OpenDocumentation(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try { using var browser = Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) { StatusText.Text = ProxySupport.Sanitize(error.Message); }
    }
}
