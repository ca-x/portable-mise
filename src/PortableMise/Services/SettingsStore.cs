using System.Text.Json;
using System.Text.Json.Serialization;
using PortableMise.Models;

using PortableMise.Localization;

namespace PortableMise.Services;

public sealed class SettingsStore(string appDirectory) : ISettingsStore
{
    private readonly string root = Path.GetFullPath(appDirectory);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Load()
    {
        string path = Path.Combine(root, "portable-mise.json");
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllBytes(path), JsonOptions)
                ?? throw new InvalidDataException(UiText.T("配置文件内容为空。"));
            Validate(settings);
            return settings;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(UiText.T("portable-mise.json 无法读取，原文件已保留，请修正配置后重试。"), error);
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Validate(settings);
        var portable = settings.Copy();
        portable.MiseDirectory = AppPaths.PortablePath(AppPaths.Resolve(settings, root).MiseRoot, root);
        portable.ToolsDirectory = AppPaths.PortablePath(AppPaths.Resolve(settings, root).ToolsRoot, root);
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "portable-mise.json");
        string temporary = Path.Combine(root, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(portable, JsonOptions), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        MiseEnvironment.ValidatePathEntries(AppPaths.Resolve(settings, root));
        if (settings.LastRegisteredPaths is null || settings.LastRegisteredPaths.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException(UiText.T("记录的终端目录无效，原配置已保留。"));
        if (settings.UpdateIntervalHours is < 1 or > 168)
            throw new InvalidDataException(UiText.T("自动更新间隔应为 1 至 168 小时。"));
        ProxySupport.Validate(settings);
    }
}
