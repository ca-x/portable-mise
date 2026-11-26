using PortableMise.Localization;
using PortableMise.Models;

namespace PortableMise.Services;

public sealed record LanguageMirrorResult(IReadOnlyList<PortableEcosystemFile> Files);

public sealed class LanguageMirrorWorkspace(AppPaths paths, AppSettings settings, IMiseService mise)
{
    private const string CargoKey = "cargo-mirror";
    private readonly ToolWorkspaceConfiguration workspace = new(paths, settings, mise);

    public async Task<LanguageMirrorResult> ApplyAsync(
        LanguageMirrorProfile profile, LanguageMirrorPreset preset, CancellationToken cancellationToken)
    {
        var definition = LanguageMirrorCatalog.All.FirstOrDefault(item => item.Id == profile.Id);
        var source = definition?.Presets.FirstOrDefault(item => item.Id == preset.Id);
        if (definition is null || source is null)
            throw new InvalidOperationException(UiText.T("请选择目录中提供的镜像预设。"));

        cancellationToken.ThrowIfCancellationRequested();
        if (definition.ConfigKind is string kind)
        {
            if (source.Values.Length != 1 || definition.Keys.Length != 0)
                throw InvalidPreset();
            return new(await PortableEcosystemConfiguration.ApplyAsync(paths, kind, source.Values[0], cancellationToken));
        }

        if (source.Values.Length != definition.Keys.Length || definition.Keys.Length == 0)
            throw InvalidPreset();

        var changes = new Dictionary<string, string>(StringComparer.Ordinal);
        string? cargoMirror = null;
        bool resetCargo = false;
        foreach (var (key, value) in definition.Keys.Zip(source.Values))
        {
            if (key == CargoKey)
            {
                if (value.Length == 0) resetCargo = true;
                else cargoMirror = ToolWorkspaceConfiguration.ValidateMirror(value, forCargo: true);
            }
            else
            {
                if (!key.StartsWith("env.", StringComparison.Ordinal) && !key.StartsWith("settings.", StringComparison.Ordinal))
                    throw InvalidPreset();
                ValidateSource(key, value);
                changes.Add(key, value);
            }
        }

        if (resetCargo)
        {
            if (changes.Count != 0) throw InvalidPreset();
            await workspace.ResetCargoMirrorAsync(cancellationToken);
        }
        else await workspace.SaveAsync(changes, cargoMirror, cancellationToken);
        return new([]);
    }

    public async Task<string?> ReadPresetIdAsync(LanguageMirrorProfile profile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (profile.ConfigKind is string kind)
        {
            string? source = PortableEcosystemConfiguration.Read(paths, kind);
            return profile.Presets.FirstOrDefault(
                preset => preset.Values.Length == 1 && SourcesEqual(source, preset.Values[0]))?.Id;
        }

        var values = new List<string?>();
        foreach (string key in profile.Keys)
        {
            if (key != CargoKey)
            {
                values.Add(await workspace.ReadAsync(key, cancellationToken));
                continue;
            }
            string? cargo = await workspace.ReadCargoMirrorAsync(cancellationToken);
            if (cargo is null && await workspace.HasPortableCargoDefaultAsync(cancellationToken))
                cargo = "";
            values.Add(cargo);
        }
        return profile.Presets.FirstOrDefault(preset =>
            preset.Values.Length == values.Count
            && preset.Values.Zip(values).All(item => SourcesEqual(item.Second, item.First)))?.Id;
    }

    private static bool SourcesEqual(string? current, string preset) =>
        current is not null && string.Equals(current.TrimEnd('/'), preset.TrimEnd('/'), StringComparison.Ordinal);

    private static void ValidateSource(string key, string value)
    {
        if (key == "env.GOPROXY")
        {
            foreach (string source in value.Split(',', '|'))
                if (source is not ("direct" or "off")) ToolWorkspaceConfiguration.ValidateMirror(source);
        }
        else ToolWorkspaceConfiguration.ValidateMirror(value);
    }

    private static InvalidOperationException InvalidPreset() =>
        new(UiText.T("镜像预设与工作区设置不匹配。"));
}
