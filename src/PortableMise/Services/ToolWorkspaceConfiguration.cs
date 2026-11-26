using System.Text;
using PortableMise.Localization;
using PortableMise.Models;
using Tomlyn;
using Tomlyn.Model;

namespace PortableMise.Services;

public sealed class ToolWorkspaceConfiguration(AppPaths paths, AppSettings settings, IMiseService mise)
{
    private const string CargoHome = "{{ env.MISE_CONFIG_DIR }}/cargo";
    private const string CargoMarker = "# Managed by Portable mise";
    private readonly AppSettings settings = settings.Copy();
    private string MiseConfig => Path.Combine(paths.ConfigDirectory, "config.toml");
    private string CargoConfig => Path.Combine(paths.ConfigDirectory, "cargo", "config.toml");
    private string LegacyCargoConfig => Path.Combine(paths.ConfigDirectory, "cargo", "config");

    public async Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        return await ReadConfigAsync(MiseConfig, key, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> ReadCargoMirrorAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(LegacyCargoConfig) ||
            await ReadAsync("env.CARGO_HOME", cancellationToken).ConfigureAwait(false) != CargoHome)
            return null;
        byte[]? original = await ReadFileAsync(CargoConfig, cancellationToken).ConfigureAwait(false);
        if (original is null) return null;
        var sources = GetTable(ReadToml(Encoding.UTF8.GetString(original)), "source");
        return IsPortableSource(sources) ? GetValue(GetTable(sources, "portable-mise"), "registry") as string : null;
    }

    public async Task<bool> HasPortableCargoDefaultAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(LegacyCargoConfig) ||
            await ReadAsync("env.CARGO_HOME", cancellationToken).ConfigureAwait(false) != CargoHome)
            return false;
        byte[]? config = await ReadFileAsync(CargoConfig, cancellationToken).ConfigureAwait(false);
        if (config is null) return false;
        var sources = GetTable(ReadToml(Encoding.UTF8.GetString(config)), "source");
        return sources is null || !sources.ContainsKey("crates-io") && !sources.ContainsKey("portable-mise");
    }

    public async Task ResetCargoMirrorAsync(CancellationToken cancellationToken)
    {
        EnsureNoLegacyCargoConfig();
        if (await ReadAsync("env.CARGO_HOME", cancellationToken).ConfigureAwait(false) != CargoHome)
        {
            if (File.Exists(CargoConfig)) throw ManualCargoConfiguration();
            return;
        }

        byte[]? original = await ReadFileAsync(CargoConfig, cancellationToken).ConfigureAwait(false);
        if (original is null) return;
        string text = Encoding.UTF8.GetString(original);
        var sources = GetTable(ReadToml(text), "source");
        if (sources is null || !sources.ContainsKey("crates-io") && !sources.ContainsKey("portable-mise"))
            return;
        var cratesIo = GetTable(sources, "crates-io");
        var portableMirror = GetTable(sources, "portable-mise");
        string? address = GetValue(portableMirror, "registry") as string;
        if (address is null || !IsPortableSource(sources) || cratesIo?.Count != 1 || portableMirror?.Count != 1)
            throw ManualCargoConfiguration();
        string block = MatchLineEndings(ComposeCargo(address), text);
        int start = text.IndexOf(block, StringComparison.Ordinal);
        if (start < 0 || start != text.LastIndexOf(block, StringComparison.Ordinal))
            throw ManualCargoConfiguration();

        string remaining = text.Remove(start, block.Length);
        _ = ReadToml(remaining);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNoLegacyCargoConfig();
        EnsureUnchanged(CargoConfig, original);
        ReplaceFile(CargoConfig, Encoding.UTF8.GetBytes(remaining));
    }

    private static InvalidOperationException ManualCargoConfiguration() =>
        new(UiText.T("现有 Cargo 镜像已手动编辑，请在「配置与维护」中编辑。"));

    private void EnsureNoLegacyCargoConfig()
    {
        if (File.Exists(LegacyCargoConfig))
            throw new InvalidOperationException(UiText.T("Cargo 会优先读取已有的 config 文件。请在「配置与维护」中检查该文件。"));
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, string> values, string? cargoMirror, CancellationToken cancellationToken)
    {
        if (values.Count == 0 && cargoMirror is null) return;

        var variables = new Dictionary<string, string>(values, StringComparer.Ordinal);
        var originalMise = await ReadFileAsync(MiseConfig, cancellationToken).ConfigureAwait(false);
        byte[]? originalCargo = null;
        byte[]? replacementCargo = null;
        if (cargoMirror is not null)
        {
            cargoMirror = ValidateMirror(cargoMirror, forCargo: true);
            EnsureNoLegacyCargoConfig();
            var currentHome = await ReadAsync("env.CARGO_HOME", cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(currentHome) && currentHome != CargoHome)
                throw new InvalidOperationException(UiText.T("已有其他 CARGO_HOME 设置，请先在 mise 配置中处理该设置。"));

            originalCargo = await ReadFileAsync(CargoConfig, cancellationToken).ConfigureAwait(false);
            replacementCargo = UpdateCargoFile(originalCargo, cargoMirror);
            variables["env.CARGO_HOME"] = CargoHome;
        }

        byte[]? lastMise = originalMise;
        bool miseWritten = false;
        try
        {
            if (originalMise is null)
            {
                Directory.CreateDirectory(paths.ConfigDirectory);
                using (new FileStream(MiseConfig, FileMode.CreateNew, FileAccess.Write)) { }
                miseWritten = true;
                lastMise = [];
            }
            foreach (var pair in variables)
            {
                EnsureUnchanged(MiseConfig, lastMise);
                string valueType = pair.Key.StartsWith("settings.", StringComparison.Ordinal) ? "infer" : "string";
                var result = await mise.RunAsync(settings,
                    ["config", "set", "--file", MiseConfig, "--type", valueType, "--", pair.Key, pair.Value],
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                EnsureSuccess(result);
                miseWritten = true;
                lastMise = await ReadFileAsync(MiseConfig, CancellationToken.None).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (replacementCargo is not null)
            {
                EnsureUnchanged(CargoConfig, originalCargo);
                ReplaceFile(CargoConfig, replacementCargo);
            }
        }
        catch
        {
            if (miseWritten)
            {
                try
                {
                    EnsureUnchanged(MiseConfig, lastMise);
                    if (originalMise is null) File.Delete(MiseConfig);
                    else ReplaceFile(MiseConfig, originalMise);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    throw new InvalidOperationException(UiText.T("未完成保存，且无法恢复原 mise 配置。请检查 config.toml。"), error);
                }
            }
            throw;
        }
    }

    public static string ValidateMirror(string value, bool forCargo = false)
    {
        value = value.Trim();
        bool sparse = forCargo && value.StartsWith("sparse+", StringComparison.Ordinal);
        string address = sparse ? value[7..] : value;
        if (address.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ||
            !Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidOperationException(UiText.T("请输入不含账号、查询参数或片段的 HTTP(S) 镜像地址。"));

        return sparse ? "sparse+" + uri.AbsoluteUri.TrimEnd('/') + "/" : uri.AbsoluteUri;
    }

    private async Task<string?> ReadConfigAsync(string file, string key, CancellationToken cancellationToken)
    {
        if (!File.Exists(file)) return null;
        var result = await mise.RunAsync(settings, ["config", "get", "--file", file, key],
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 && result.StandardError.Contains("Key not found:", StringComparison.Ordinal))
            return null;
        EnsureSuccess(result);
        return result.StandardOutput.TrimEnd('\r', '\n');
    }

    private static void EnsureSuccess(ProcessResult result)
    {
        if (result.ExitCode == 0) return;
        string error = ProxySupport.Sanitize(result.StandardError).Trim();
        throw new InvalidOperationException(error.Length > 0 ? error : UiText.F("mise 配置命令退出码为 {0}。", result.ExitCode));
    }

    private static string ComposeCargo(string address) =>
        $"{CargoMarker}\n[source.crates-io]\nreplace-with = \"portable-mise\"\n\n[source.portable-mise]\nregistry = \"{address}\"\n";

    private static byte[] UpdateCargoFile(byte[]? original, string address)
    {
        string text = original is null ? "" : Encoding.UTF8.GetString(original).TrimStart('\uFEFF');
        var sources = GetTable(ReadToml(text), "source");
        string next;
        if (sources is not null && (sources.ContainsKey("crates-io") || sources.ContainsKey("portable-mise")))
        {
            string? previousMirror = GetValue(GetTable(sources, "portable-mise"), "registry") as string;
            if (previousMirror is null || !IsPortableSource(sources))
                throw new InvalidOperationException(UiText.T("Cargo 配置已有依赖源，请在「配置与维护」中手动编辑。"));
            string previous = ComposeCargo(previousMirror);
            if (text.Contains(previous.Replace("\n", "\r\n", StringComparison.Ordinal), StringComparison.Ordinal))
                previous = previous.Replace("\n", "\r\n", StringComparison.Ordinal);
            int start = text.IndexOf(previous, StringComparison.Ordinal);
            if (start < 0 || start != text.LastIndexOf(previous, StringComparison.Ordinal))
                throw new InvalidOperationException(UiText.T("现有 Cargo 镜像已手动编辑，请在「配置与维护」中编辑。"));
            next = text[..start] + MatchLineEndings(ComposeCargo(address), previous) + text[(start + previous.Length)..];
        }
        else
        {
            string separator = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            next = text + (text.Length == 0 ? "" : separator + separator) + MatchLineEndings(ComposeCargo(address), text);
        }
        // Parsing the finished text catches inline table conflicts before either file changes.
        var updated = GetTable(ReadToml(next), "source");
        if (!IsPortableSource(updated) || GetValue(GetTable(updated, "portable-mise"), "registry") as string != address)
            throw new InvalidOperationException(UiText.T("无法在当前配置中设置 Cargo 镜像。"));
        return Encoding.UTF8.GetBytes(next);
    }

    private static TomlTable ReadToml(string text)
    {
        try { return TomlSerializer.Deserialize<TomlTable>(text.TrimStart('\uFEFF')) ?? new TomlTable(); }
        catch (TomlException error) { throw new InvalidOperationException(UiText.T("无法解析已有 Cargo 配置，原文件已保留。"), error); }
    }

    private static object? GetValue(TomlTable? table, string key) => table is not null && table.TryGetValue(key, out var value) ? value : null;

    private static TomlTable? GetTable(TomlTable? table, string key) => GetValue(table, key) as TomlTable;

    private static bool IsPortableSource(TomlTable? sources) => GetValue(GetTable(sources, "crates-io"), "replace-with") as string == "portable-mise";

    private static string MatchLineEndings(string replacement, string existing) => existing.Contains("\r\n", StringComparison.Ordinal)
        ? replacement.Replace("\n", "\r\n", StringComparison.Ordinal) : replacement;

    private static async Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken) =>
        File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false) : null;

    private static void EnsureUnchanged(string path, byte[]? expected)
    {
        if (File.Exists(path) ? expected is null || !File.ReadAllBytes(path).AsSpan().SequenceEqual(expected) : expected is not null)
            throw new InvalidOperationException(UiText.T("配置文件在编辑期间发生了变化，请重新打开设置。"));
    }

    private static void ReplaceFile(string path, byte[] contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, contents);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }
}
