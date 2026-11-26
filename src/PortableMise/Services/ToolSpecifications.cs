using PortableMise.Models;

using PortableMise.Localization;

namespace PortableMise.Services;

public static class ToolSpecifications
{
    public const int QueueLimit = 64;

    public static IReadOnlyList<string> Parse(string input)
    {
        var specifications = new List<string>();
        int start = 0, depth = 0;
        for (int index = 0; index <= input.Length; index++)
        {
            if (index == input.Length || depth == 0 && char.IsWhiteSpace(input[index]))
            {
                if (index == start) { start++; continue; }
                string value = input[start..index];
                Validate(value);
                specifications.Add(value);
                if (specifications.Count > QueueLimit)
                    throw new InvalidOperationException(UiText.F("每批最多添加 {0} 个工具。", QueueLimit));
                start = index + 1;
                continue;
            }
            if (input[index] == '[') depth++;
            if (input[index] == ']') depth--;
        }
        return specifications;
    }

    public static void Validate(string specification)
    {
        if (string.IsNullOrWhiteSpace(specification) || specification.Length > 512 || specification.StartsWith('-'))
            throw InvalidSpecification();

        int depth = 0;
        foreach (char character in specification)
        {
            if (character == '[') depth++;
            if (character == ']' && --depth < 0) throw InvalidSpecification();
            if (char.IsControl(character) || depth == 0 && char.IsWhiteSpace(character))
                throw InvalidSpecification();
        }
        if (depth != 0) throw InvalidSpecification();
    }

    private static InvalidOperationException InvalidSpecification() =>
        new(UiText.T("工具格式无效。可输入 node@lts、cargo:ripgrep[features=pcre2] 等 mise 工具规格。"));

    public static ToolInstallRequest ToEditable(string specification)
    {
        int start = specification.IndexOf('['), end = specification.LastIndexOf(']');
        if (start < 1 || end <= start || end + 1 < specification.Length && specification[end + 1] != '@')
            return new(specification);

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string part in specification[(start + 1)..end].Split(','))
        {
            int equals = part.IndexOf('=');
            if (equals < 1) return new(specification);
            string key = part[..equals].Trim();
            string value = part[(equals + 1)..].Trim();
            if (!ValidOptionKey(key) || value.Length == 0 || value.IndexOfAny(['[', ']', '"', '\'']) >= 0 || !options.TryAdd(key, value))
                return new(specification);
        }
        return new(specification.Remove(start, end - start + 1), options);
    }

    public static string ToolName(string specification)
    {
        int bracket = specification.IndexOf('[');
        if (bracket >= 0) specification = specification[..bracket];
        int lastAt = specification.LastIndexOf('@');
        int backendNameStart = specification.IndexOf(':') + 1;
        return lastAt > backendNameStart ? specification[..lastAt] : specification;
    }

    public static IReadOnlyList<string> InstallArguments(IReadOnlyList<ToolInstallRequest> requests)
    {
        if (requests.Count is 0 or > QueueLimit)
            throw new InvalidOperationException(UiText.F("每批可安装 1 至 {0} 个工具。", QueueLimit));
        var arguments = new List<string> { "use", "--global", "--yes" };
        foreach (var request in requests)
        {
            Validate(request.Specification);
            if (request.Options != null)
                foreach (var (key, value) in request.Options)
                {
                    if (!ValidOptionKey(key) || value.Length is 0 or > 4096 || value.Any(char.IsControl))
                        throw new InvalidOperationException(UiText.T("工具选项的名称或值无效。"));
                    arguments.Add("--tool-option");
                    arguments.Add(key + "=" + value);
                }
            arguments.Add(request.Specification);
        }
        return arguments;
    }

    private static bool ValidOptionKey(string key) => key.Length > 0
        && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
}
