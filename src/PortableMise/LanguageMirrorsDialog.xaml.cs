using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PortableMise.Localization;
using PortableMise.Models;
using PortableMise.Services;

namespace PortableMise;

public partial class LanguageMirrorsDialog : Window
{
    private readonly LanguageMirrorWorkspace workspace;
    private readonly bool canEdit;
    private CancellationTokenSource? readCancellation;
    private CancellationTokenSource? applyCancellation;
    private bool closeRequested;

    public bool AppliedChanges { get; private set; }

    public LanguageMirrorsDialog(AppPaths paths, AppSettings settings, IMiseService mise, string? toolId = null,
        bool allowWorkspaceEdits = true)
    {
        workspace = new LanguageMirrorWorkspace(paths, settings, mise);
        canEdit = allowWorkspaceEdits && File.Exists(paths.MiseExecutable);
        InitializeComponent();
        Loaded += (_, _) => Controls.Motion.PlayFadeIn(this);
        var profiles = toolId is null ? LanguageMirrorCatalog.All : LanguageMirrorCatalog.ForTool(toolId);
        ProfileBox.ItemsSource = profiles.Select(profile => new ProfileChoice(UiText.T(profile.Label), profile)).ToArray();
        ProfileBox.SelectedIndex = 0;
        if (!canEdit) StatusText.Text = UiText.T("安装 mise 后，且没有任务运行时可修改。");
    }

    private sealed record ProfileChoice(string Label, LanguageMirrorProfile Profile);
    private sealed record PresetChoice(string Label, LanguageMirrorPreset Preset);

    private async void ProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        readCancellation?.Cancel();
        if (canEdit) StatusText.Text = "";
        CurrentText.Visibility = Visibility.Collapsed;
        var choice = ProfileBox.SelectedItem as ProfileChoice;
        DescriptionText.Text = UiText.T(choice?.Profile.Description ?? "");
        PresetBox.ItemsSource = choice?.Profile.Presets
            .Select(preset => new PresetChoice(UiText.T(preset.Label), preset)).ToArray();
        UpdatePreview();
        if (choice is null || !canEdit) return;

        var cancellation = new CancellationTokenSource();
        readCancellation = cancellation;
        try
        {
            string? id = await workspace.ReadPresetIdAsync(choice.Profile, cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(ProfileBox.SelectedItem, choice) || id is null) return;
            var match = PresetBox.Items.OfType<PresetChoice>().FirstOrDefault(item => item.Preset.Id == id);
            if (match is null) return;
            CurrentText.Text = UiText.F("当前预设：{0}", match.Label);
            CurrentText.Visibility = Visibility.Visible;
            if (PresetBox.SelectedItem is null) PresetBox.SelectedItem = match;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && ReferenceEquals(ProfileBox.SelectedItem, choice))
                StatusText.Text = ProxySupport.Sanitize(error.Message);
        }
        finally
        {
            if (readCancellation == cancellation) readCancellation = null;
            cancellation.Dispose();
        }
    }

    private void PresetChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        var profile = (ProfileBox.SelectedItem as ProfileChoice)?.Profile;
        var preset = (PresetBox.SelectedItem as PresetChoice)?.Preset;
        ApplyButton.IsEnabled = canEdit && applyCancellation is null && profile is not null && preset is not null;
        if (profile is null || preset is null)
        {
            PreviewBox.Text = UiText.T("选择一个镜像预设以查看配置。");
            return;
        }
        PreviewBox.Text = string.Join(Environment.NewLine, preset.Values.Select((value, index) =>
        {
            string display = string.IsNullOrWhiteSpace(value) ? UiText.T("恢复客户端默认源") : value;
            return index < profile.Keys.Length && !string.IsNullOrEmpty(profile.Keys[index])
                ? $"{profile.Keys[index]} = {display}"
                : display;
        }));
    }

    private async void ApplyMirror(object sender, RoutedEventArgs e)
    {
        if (!canEdit || applyCancellation is not null ||
            ProfileBox.SelectedItem is not ProfileChoice profile ||
            PresetBox.SelectedItem is not PresetChoice preset) return;

        readCancellation?.Cancel();
        UpdatePreview();
        var cancellation = new CancellationTokenSource();
        applyCancellation = cancellation;
        ProfileBox.IsEnabled = PresetBox.IsEnabled = ApplyButton.IsEnabled = false;
        StatusText.Text = UiText.T("正在应用镜像…");
        try
        {
            var result = await workspace.ApplyAsync(profile.Profile, preset.Preset, cancellation.Token);
            AppliedChanges = true;
            StatusText.Text = UiText.T("已保存到便携工作区。");
            CurrentText.Text = UiText.F("当前预设：{0}", preset.Label);
            CurrentText.Visibility = Visibility.Visible;
            if (result.Files.Count > 0)
            {
                string files = string.Join(Environment.NewLine + Environment.NewLine,
                    result.Files.Select(file => $"{file.Path}{Environment.NewLine}{UiText.T(file.Instructions)}"));
                PreviewBox.AppendText(Environment.NewLine + Environment.NewLine + files);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusText.Text = UiText.T("已取消。");
        }
        catch (Exception error)
        {
            StatusText.Text = ProxySupport.Sanitize(error.Message);
        }
        finally
        {
            applyCancellation = null;
            cancellation.Dispose();
            ProfileBox.IsEnabled = PresetBox.IsEnabled = true;
            ApplyButton.IsEnabled = canEdit;
            if (closeRequested) Close();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        readCancellation?.Cancel();
        if (applyCancellation is null) return;
        e.Cancel = true;
        closeRequested = true;
        applyCancellation.Cancel();
    }
}
