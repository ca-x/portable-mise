using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using PortableMise.Localization;
using PortableMise.Models;

namespace PortableMise.Services;

public sealed record PortableEcosystemFile(string Path, string Instructions);

public static class PortableEcosystemConfiguration
{
    private sealed record GeneratedFile(string Name, string Text, string Instructions);
    private sealed record ProfileState(string Source, Dictionary<string, string> Files);
    private const string StateFile = "preset.json";
    private static readonly string[] Kinds =
    [
        "maven", "nuget", "rubygems", "r", "haskell", "composer",
        "luarocks", "clojure", "perl", "opam", "bun", "poetry", "rye"
    ];

    public static bool Supports(string? configKind) => configKind != null && Kinds.Contains(configKind, StringComparer.Ordinal);

    public static string? Read(AppPaths paths, string configKind)
    {
        string directory = ProfileDirectory(paths, configKind);
        string statePath = Path.Combine(directory, StateFile);
        EnsureNotLink(statePath);
        if (!File.Exists(statePath)) return null;
        var state = ReadState(File.ReadAllBytes(statePath));
        foreach (var (name, hash) in state.Files)
        {
            if (name != Path.GetFileName(name) || string.IsNullOrWhiteSpace(name) || name is "." or "..")
                return null;
            string file = Path.Combine(directory, name);
            EnsureNotLink(file);
            if (!File.Exists(file) || Hash(File.ReadAllBytes(file)) != hash) return null;
        }
        return state.Files.Count == 0 ? null : state.Source;
    }

    public static async Task<IReadOnlyList<PortableEcosystemFile>> ApplyAsync(
        AppPaths paths, string configKind, string source, CancellationToken ct = default)
    {
        string address = NormalizeSource(source);
        string directory = ProfileDirectory(paths, configKind);
        var files = BuildFiles(configKind, address, directory);
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        string lockFile = Path.Combine(directory, ".lock");
        EnsureNotLink(lockFile);
        using var profileLock = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        return await SaveAllAsync(directory, address, files, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<PortableEcosystemFile>> SaveAllAsync(
        string directory, string address, IReadOnlyList<GeneratedFile> files, CancellationToken ct)
    {
        string statePath = Path.Combine(directory, StateFile);
        EnsureNotLink(statePath);
        byte[]? oldState = File.Exists(statePath) ? await File.ReadAllBytesAsync(statePath, ct).ConfigureAwait(false) : null;
        ProfileState? old = oldState is null ? null : ReadState(oldState);
        var previous = new Dictionary<string, byte[]?>(StringComparer.Ordinal) { [statePath] = oldState };
        var replacement = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            string path = Path.Combine(directory, file.Name);
            EnsureNotLink(path);
            byte[]? value = File.Exists(path) ? await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false) : null;
            if (value != null && (old is null || !old.Files.TryGetValue(file.Name, out string? fingerprint) || Hash(value) != fingerprint))
                throw new InvalidOperationException(UiText.F("文件 {0} 已手动编辑或不属于工具箱，未覆盖。", file.Name));
            previous[path] = value;
            replacement[path] = Encoding.UTF8.GetBytes(file.Text);
            fingerprints[file.Name] = Hash(replacement[path]);
        }
        replacement[statePath] = JsonSerializer.SerializeToUtf8Bytes(new ProfileState(address, fingerprints));
        WriteTransaction(replacement, previous, ct);
        return files.Select(file => new PortableEcosystemFile(Path.Combine(directory, file.Name), file.Instructions)).ToArray();
    }

    private static void WriteTransaction(Dictionary<string, byte[]> replacement, Dictionary<string, byte[]?> previous, CancellationToken ct)
    {
        var writeOrder = new List<string>();
        try
        {
            foreach (var (path, value) in replacement)
            {
                ct.ThrowIfCancellationRequested();
                byte[]? current = File.Exists(path) ? File.ReadAllBytes(path) : null;
                if (current is null ? previous[path] is not null : previous[path] is null || !current.AsSpan().SequenceEqual(previous[path]))
                    throw new InvalidOperationException(UiText.F("文件 {0} 在保存期间被修改，请重试。", Path.GetFileName(path)));
                if (current != null && current.AsSpan().SequenceEqual(value)) continue;
                AtomicWrite(path, value);
                writeOrder.Add(path);
            }
        }
        catch
        {
            foreach (string path in Enumerable.Reverse(writeOrder))
            {
                if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(replacement[path])) continue;
                if (previous[path] is { } bytes) AtomicWrite(path, bytes);
                else File.Delete(path);
            }
            throw;
        }
    }

    private static ProfileState ReadState(byte[] bytes)
    {
        try
        {
            var state = JsonSerializer.Deserialize<ProfileState>(bytes);
            if (state?.Files is null || string.IsNullOrWhiteSpace(state.Source)) throw new JsonException();
            return state;
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException(UiText.T("镜像配置的记录无法读取，现有文件已保留。"), error);
        }
    }

    private static string NormalizeSource(string value)
    {
        value = value.Trim();
        if (value.Length > 2048 || value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var source) || source.Scheme is not ("https" or "http") ||
            string.IsNullOrEmpty(source.Host) || source.UserInfo.Length != 0 ||
            source.Query.Length != 0 || source.Fragment.Length != 0)
            throw new InvalidOperationException(UiText.T("请输入不含账号、查询参数或片段的 HTTP(S) 镜像地址。"));
        return source.AbsoluteUri;
    }

    private static string ProfileDirectory(AppPaths paths, string configKind)
    {
        if (!Supports(configKind)) throw new InvalidOperationException(UiText.T("此镜像不支持生成便携配置。"));
        string mirrors = Path.Combine(paths.ConfigDirectory, "mirrors");
        string profile = Path.Combine(mirrors, configKind);
        EnsureNotLink(mirrors);
        EnsureNotLink(profile);
        return profile;
    }

    private static void EnsureNotLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException(UiText.F("镜像配置位置 {0} 是文件链接，未使用。", Path.GetFileName(path)));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void AtomicWrite(string path, byte[] value)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, value);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static IReadOnlyList<GeneratedFile> BuildFiles(string kind, string source, string directory)
    {
        string PathFor(string name) => PowerShellQuote(Path.Combine(directory, name));
        string SetEnv(string name, string path) => "$env:" + name + " = " + path;
        GeneratedFile CommandFile(string content) => new("commands.txt", content + "\n", UiText.T("在对应项目或工具环境中按需运行文件里的命令。"));

        return kind switch
        {
            "maven" =>
            [
                new("settings.xml", MavenSettings(source),
                    $"mvn --settings {PathFor("settings.xml")} verify\nmvnd --settings {PathFor("settings.xml")} verify"),
                new("repositories.gradle", "allprojects { repositories { maven { url = uri(" + DoubleQuote(source) + ") } } }\n",
                    $"gradle --init-script {PathFor("repositories.gradle")} build\n" +
                    UiText.T("Gradle 片段添加项目仓库，已有仓库优先级或 settings.gradle 的策略仍可能覆盖它。"))
            ],
            "nuget" =>
            [
                new("NuGet.Config", NuGetSettings(source), $"dotnet restore --configfile {PathFor("NuGet.Config")}")
            ],
            "rubygems" =>
            [
                new("gemrc.yml", "---\n:sources:\n  - " + DoubleQuote(source) + "\n", $"gem --config-file {PathFor("gemrc.yml")} install rake"),
                new("bundle-config.yml", "---\nBUNDLE_MIRROR__HTTPS://RUBYGEMS__ORG/: " + DoubleQuote(source) + "\n",
                    SetEnv("BUNDLE_USER_CONFIG", PathFor("bundle-config.yml")) + "\nbundle install")
            ],
            "r" =>
            [
                new(".Rprofile", "options(repos = c(CRAN = " + DoubleQuote(source) + "))\n",
                    SetEnv("R_PROFILE_USER", PathFor(".Rprofile")) + "\nRscript example.R")
            ],
            "haskell" =>
            [
                new("cabal.config", "repository hackage.haskell.org\n  url: " + source + "\n  secure: True\n",
                    $"cabal --config-file={PathFor("cabal.config")} update"),
                new("stack-package-index.yaml", "package-index:\n  download-prefix: " + DoubleQuote(source) + "\n",
                    UiText.T("将此片段合并到 Stack 配置的 package-index，保留已有 Hackage 签名校验。"))
            ],
            "composer" =>
            [
                new("config.json", ComposerSettings(source), SetEnv("COMPOSER_HOME", PowerShellQuote(directory)) + "\ncomposer install")
            ],
            "luarocks" =>
            [
                new("config.lua", "rocks_servers = { " + DoubleQuote(source) + " }\n",
                    SetEnv("LUAROCKS_CONFIG", PathFor("config.lua")) + "\nluarocks install luafilesystem")
            ],
            "clojure" =>
            [
                new("clojars.edn", "{:mvn/repos {\"clojars\" {:url " + DoubleQuote(source) + "}}}\n",
                    $"clojure -Sdeps (Get-Content -Raw {PathFor("clojars.edn")}) -P"),
                new("lein-profile.clj", "{:user {:mirrors {\"clojars\" {:name \"portable-mise\" :url " + DoubleQuote(source) + "}}}}\n",
                    UiText.T("在 Leiningen 项目的 profiles.clj 中合并此片段。"))
            ],
            "perl" => [CommandFile($"cpanm --mirror {PowerShellQuote(source)} --mirror-only JSON")],
            "opam" => [CommandFile($"opam init --root {PathFor("opam-root")} --bare --no-setup default {PowerShellQuote(source)}\n"
                + $"opam repository --root {PathFor("opam-root")} set-url default {PowerShellQuote(source)}")],
            "bun" =>
            [
                new("bunfig.toml", "[install]\nregistry = " + DoubleQuote(source) + "\n", $"bun --config={PathFor("bunfig.toml")} install")
            ],
            "poetry" => [CommandFile($"poetry source add --priority=primary portable-mise {PowerShellQuote(source)}")],
            "rye" =>
            [
                new("config.toml", "[[sources]]\nname = \"default\"\nurl = " + DoubleQuote(source) + "\ntype = \"index\"\n",
                    SetEnv("RYE_HOME", PowerShellQuote(directory)) + "\nrye sync")
            ],
            _ => throw new InvalidOperationException(UiText.T("此镜像不支持生成便携配置。"))
        };
    }

    private static string MavenSettings(string source)
    {
        XNamespace ns = "http://maven.apache.org/SETTINGS/1.0.0";
        return new XElement(ns + "settings", new XElement(ns + "mirrors",
            new XElement(ns + "mirror", new XElement(ns + "id", "portable-mise"),
                new XElement(ns + "mirrorOf", "central"), new XElement(ns + "url", source)))).ToString() + "\n";
    }

    private static string NuGetSettings(string source)
    {
        string index = source.TrimEnd('/');
        if (!index.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) index += "/index.json";
        return new XElement("configuration", new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "portable-mise"), new XAttribute("value", index)))).ToString() + "\n";
    }

    private static string ComposerSettings(string source) =>
        "{\n  \"repositories\": {\n    \"packagist.org\": { \"type\": \"composer\", \"url\": " + DoubleQuote(source) + " }\n  }\n}\n";

    private static string DoubleQuote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string PowerShellQuote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
