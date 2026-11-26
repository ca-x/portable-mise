using System.Text;
using System.Text.Json;
using PortableMise.Models;
using PortableMise.Localization;
using Tomlyn;
using Tomlyn.Model;

namespace PortableMise.Services;

public static class ToolExport
{
    public static IEnumerable<string> WorkspaceFiles(AppPaths paths, string json)
    {
        using var loaded = JsonDocument.Parse(json);
        string root = Path.GetFullPath(paths.ConfigDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var entry in loaded.RootElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("path", out var path) || path.GetString() is not string value
                || !Path.IsPathFullyQualified(value)) continue;
            string file = Path.GetFullPath(value);
            if (file.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && Path.GetExtension(file).Equals(".toml", StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                yield return file;
        }
    }

    public static string Create(IEnumerable<string> workspaceFiles, IEnumerable<ToolInfo> inventory, string cargoConfig)
    {
        ToolInfo[] installed = inventory.Where(tool => tool.Installed).OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ThenBy(tool => tool.Version, StringComparer.Ordinal).ToArray();
        var output = new TomlTable();
        var tools = new TomlTable();
        output["tools"] = tools;
        string[] workspaceKeys = CoreToolCatalog.Languages.SelectMany(tool => tool.WorkspaceOptions ?? [])
            .Select(option => option.Key).Concat(LanguageMirrorCatalog.All.SelectMany(profile => profile.Keys))
            .Where(key => key.StartsWith("env.", StringComparison.Ordinal) || key.StartsWith("settings.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).ToArray();

        foreach (string file in workspaceFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var config = ReadTable(file);
            if (config.TryGetValue("tools", out var definitions) && definitions is TomlTable table)
                foreach (var (key, value) in table) tools[key] = value;
            MergeSection(output, config, "tool_alias");
            MergeSection(output, config, "alias");
            foreach (string key in workspaceKeys)
                if (Find(config, key) is { } value) Set(output, key, value);
            if (Find(config, "env.CARGO_HOME") is string cargoHome && cargoHome == "{{ env.MISE_CONFIG_DIR }}/cargo")
                Set(output, "env.CARGO_HOME", cargoHome);
        }

        foreach (var group in installed.GroupBy(tool => tool.Name, StringComparer.Ordinal))
        {
            if (tools.ContainsKey(group.Key)) continue;
            var versions = group.Select(tool => tool.Version).Distinct(StringComparer.Ordinal).ToArray();
            if (versions.Length == 1)
                tools[group.Key] = versions[0];
            else
            {
                var array = new TomlArray(versions.Length);
                foreach (string version in versions) array.Add(version);
                tools[group.Key] = array;
            }
        }

        var text = new StringBuilder();
        text.Append("# ").AppendLine(UiText.T("Portable mise 工作区工具"));
        if (installed.Length != 0)
        {
            text.Append("# ").AppendLine(UiText.T("已安装的版本（含未选用的历史版本）"));
            foreach (var tool in installed)
                text.Append("# ").AppendLine(SingleLine(tool.Name + "@" + tool.Version));
        }
        if (File.Exists(cargoConfig))
        {
            var config = ReadTable(cargoConfig);
            if (Find(config, "source.crates-io.replace-with") is string name
                && config.TryGetValue("source", out var sources) && sources is TomlTable sourceTable
                && sourceTable.TryGetValue(name, out var mirror) && mirror is TomlTable mirrorTable
                && mirrorTable.TryGetValue("registry", out var registry) && registry is string address)
            {
                var cargoSource = new TomlTable
                {
                    ["crates-io"] = new TomlTable { ["replace-with"] = name },
                    [name] = new TomlTable { ["registry"] = address }
                };
                text.Append("# ").AppendLine(UiText.T("便携 Cargo 镜像（复制以下区段到 Cargo 的 config.toml）"));
                foreach (string line in TomlSerializer.Serialize(new TomlTable { ["source"] = cargoSource }).TrimEnd().Split('\n'))
                    text.Append("# ").AppendLine(line.TrimEnd('\r'));
            }
        }

        return text.AppendLine().Append(TomlSerializer.Serialize(output)).ToString();
    }

    private static void MergeSection(TomlTable output, TomlTable config, string name)
    {
        if (!config.TryGetValue(name, out var section) || section is not TomlTable incoming) return;
        if (!output.TryGetValue(name, out var previous) || previous is not TomlTable)
            output[name] = new TomlTable();
        MergeTables((TomlTable)output[name]!, incoming);
    }

    private static void MergeTables(TomlTable output, TomlTable incoming)
    {
        foreach (var (key, value) in incoming)
        {
            if (value is TomlTable nested && output.TryGetValue(key, out var previous) && previous is TomlTable existing)
                MergeTables(existing, nested);
            else
                output[key] = value;
        }
    }

    private static TomlTable ReadTable(string file)
    {
        try { return TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(file)) ?? new TomlTable(); }
        catch (TomlException error) { throw new InvalidDataException(UiText.F("无法读取配置 {0}。", Path.GetFileName(file)), error); }
    }

    private static object? Find(TomlTable table, string key)
    {
        object? value = table;
        foreach (string part in key.Split('.'))
            if (value is not TomlTable nested || !nested.TryGetValue(part, out value)) return null;
        return value;
    }

    private static void Set(TomlTable table, string key, object value)
    {
        string[] parts = key.Split('.');
        foreach (string part in parts[..^1])
        {
            if (!table.TryGetValue(part, out var current) || current is not TomlTable)
                table[part] = new TomlTable();
            table = (TomlTable)table[part]!;
        }
        table[parts[^1]] = value;
    }

    private static string SingleLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}
