using PortableMise.Models;
using PortableMise.Services;

namespace PortableMise.Tests;

internal static class LanguageMirrorTests
{
    public static async Task<int> RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        int count = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + label);
            count++;
            Console.WriteLine("PASS: " + label);
        }

        var catalog = LanguageMirrorCatalog.All;
        Check(catalog.Count >= 20 && catalog.Select(profile => profile.Id).Distinct(StringComparer.Ordinal).Count() == catalog.Count,
            "the mirror catalog contains distinct presets for the language ecosystems");
        Check(catalog.All(profile => profile.Presets.Count > 0 &&
            profile.Presets.Select(preset => preset.Id).Distinct(StringComparer.Ordinal).Count() == profile.Presets.Count &&
            profile.Presets.All(preset => preset.Values.Length == (profile.ConfigKind == null ? profile.Keys.Length : 1))),
            "each profile has selectable presets with matching address counts");
        Check(LanguageMirrorCatalog.ForTool("rust").Any(profile => profile.Keys.Contains("cargo-mirror")) &&
            LanguageMirrorCatalog.ForTool("go").Any(profile => profile.Keys.Contains("env.GOPROXY")),
            "Rust and Go quick access includes the applicable ecosystems");

        var settings = new AppSettings();
        var paths = AppPaths.Resolve(settings, root);
        var native = new FixtureMise();
        var workspace = new LanguageMirrorWorkspace(paths, settings, native);
        var rust = catalog.Single(profile => profile.Keys.Contains("env.RUSTUP_DIST_SERVER"));
        var rustPreset = rust.Presets.Last();
        await workspace.ApplyAsync(rust, rustPreset, CancellationToken.None);
        Check(native.Values["env.RUSTUP_DIST_SERVER"] == rustPreset.Values[0] &&
            native.Values["env.RUSTUP_UPDATE_ROOT"] == rustPreset.Values[1] &&
            await workspace.ReadPresetIdAsync(rust, CancellationToken.None) == rustPreset.Id,
            "Rustup pair is applied in the same workspace and detected when reopened");
        Check(native.Paths.All(path => path == Path.Combine(paths.ConfigDirectory, "config.toml")),
            "native configuration calls are scoped to the portable mise file");

        var cargo = catalog.Single(profile => profile.Keys.Contains("cargo-mirror"));
        var cargoPreset = cargo.Presets.First(preset => !string.IsNullOrWhiteSpace(preset.Values[0]));
        string cargoConfig = Path.Combine(paths.ConfigDirectory, "cargo", "config.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(cargoConfig)!);
        File.WriteAllText(cargoConfig, "[net]\nretry = 4\n");
        await workspace.ApplyAsync(cargo, cargoPreset, CancellationToken.None);
        Check(await workspace.ReadPresetIdAsync(cargo, CancellationToken.None) == cargoPreset.Id,
            "Cargo mirror is detected in its portable configuration");
        var cargoDefault = cargo.Presets.Single(preset => preset.Values[0] == "");
        await workspace.ApplyAsync(cargo, cargoDefault, CancellationToken.None);
        string reset = File.ReadAllText(cargoConfig);
        Check(reset.Contains("retry = 4", StringComparison.Ordinal) && !reset.Contains("replace-with", StringComparison.Ordinal)
            && native.Values["env.CARGO_HOME"] == "{{ env.MISE_CONFIG_DIR }}/cargo",
            "restoring the Cargo default removes the managed mirror and keeps other Cargo settings and home");
        Check(await workspace.ReadPresetIdAsync(cargo, CancellationToken.None) == cargoDefault.Id,
            "the restored portable Cargo default is recognized when reopening the picker");
        await workspace.ApplyAsync(cargo, cargoPreset, CancellationToken.None);
        string managedCargo = File.ReadAllText(cargoConfig);
        string legacyCargo = Path.Combine(Path.GetDirectoryName(cargoConfig)!, "config");
        const string legacyContents = "[net]\nretry = 8\n";
        File.WriteAllText(legacyCargo, legacyContents);
        await VerifyResetBlockedAsync(managedCargo, "reset preserves config.toml when Cargo prefers a legacy config");
        Check(File.ReadAllText(legacyCargo) == legacyContents &&
            await workspace.ReadPresetIdAsync(cargo, CancellationToken.None) is null,
            "legacy config is preserved and the ignored config.toml preset is not displayed as active");
        File.Delete(legacyCargo);

        string manuallyExtended = managedCargo + "replace-with = \"another-source\"\n";
        File.WriteAllText(cargoConfig, manuallyExtended);
        await VerifyResetBlockedAsync(manuallyExtended, "reset preserves additional keys in the managed Cargo source table");

        async Task VerifyResetBlockedAsync(string expected, string label)
        {
            bool blocked = false;
            try { await workspace.ApplyAsync(cargo, cargoDefault, CancellationToken.None); }
            catch (InvalidOperationException) { blocked = true; }
            Check(blocked && File.ReadAllText(cargoConfig) == expected, label);
        }

        File.WriteAllText(cargoConfig, "[source.crates-io]\nreplace-with = 'my-manual-source'\n");
        try
        {
            await workspace.ApplyAsync(cargo, cargo.Presets.Single(preset => preset.Values[0] == ""), CancellationToken.None);
            throw new Exception("Managed reset should reject a manual Cargo mirror.");
        }
        catch (InvalidOperationException)
        {
            Check(File.ReadAllText(cargoConfig).Contains("my-manual-source", StringComparison.Ordinal),
                "reset preserves manually configured Cargo mirrors");
        }

        var maven = catalog.Single(profile => profile.ConfigKind == "maven");
        var mavenPreset = maven.Presets.Last();
        var result = await workspace.ApplyAsync(maven, mavenPreset, CancellationToken.None);
        Check(result.Files.Count > 0 && result.Files.All(file =>
            File.Exists(file.Path) && Path.GetFullPath(file.Path).StartsWith(paths.ConfigDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(file.Instructions)),
            "file-based mirror generates portable configuration and consumption instructions");
        Check(await workspace.ReadPresetIdAsync(maven, CancellationToken.None) == mavenPreset.Id,
            "file-based selection can be reopened");
        return count;
    }

    private sealed class FixtureMise : IMiseService
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public List<string> Paths { get; } = [];

        public Task<ProcessResult> RunAsync(AppSettings settings, IReadOnlyList<string> arguments, Action<string>? log = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paths.Add(arguments[3]);
            if (arguments[1] == "set")
            {
                Values[arguments[^2]] = arguments[^1];
                return Task.FromResult(new ProcessResult(0, "", ""));
            }
            return Task.FromResult(Values.TryGetValue(arguments[^1], out string? value)
                ? new ProcessResult(0, value + "\n", "") : new ProcessResult(1, "", "Key not found: fixture"));
        }

        public Task<string> GetVersionAsync(AppSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ToolInfo>> GetToolsAsync(AppSettings settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpgradeToolsAsync(AppSettings settings, Action<string>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SelfUpdateAsync(AppSettings settings, Action<string>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task InstallToolAsync(AppSettings settings, string specification, Action<string>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task InstallToolsAsync(AppSettings settings, IReadOnlyList<ToolInstallRequest> requests, Action<string>? log = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
