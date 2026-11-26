using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using PortableMise.Localization;

namespace PortableMise;

public partial class ToolExportDialog : Window
{
    public ToolExportDialog(string content)
    {
        InitializeComponent();
        Preview.Text = content;
    }

    private async void Copy(object sender, RoutedEventArgs args)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetText(Preview.Text);
                Feedback.Text = UiText.T("已复制");
                return;
            }
            catch (ExternalException) when (attempt < 2) { await Task.Delay(60); }
            catch (ExternalException) { Feedback.Text = UiText.T("剪贴板暂不可用，请重试。"); return; }
        }
    }

    private async void SaveFile(object sender, RoutedEventArgs args)
    {
        var dialog = new SaveFileDialog { FileName = "mise-tools.toml", Filter = UiText.T("TOML 配置|*.toml"), DefaultExt = ".toml", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, Preview.Text);
            Feedback.Text = UiText.T("文件已保存");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Feedback.Text = UiText.T("无法保存，请检查目标位置。");
        }
    }
}
