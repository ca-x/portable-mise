using System.Text;
using PortableMise.Localization;

namespace PortableMise.ViewModels;

public static class CommandArguments
{
    public static IReadOnlyList<string> Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new InvalidOperationException(UiText.T("请输入 mise 子命令，例如 doctor。"));
        if (input.Contains('\n') || input.Contains('\r') || input.Contains('\0')) throw new InvalidOperationException(UiText.T("请每次输入一行命令。"));
        var result = new List<string>();
        var token = new StringBuilder();
        char quote = '\0';
        bool started = false;
        foreach (char character in input)
        {
            if (quote != '\0') { if (character == quote) quote = '\0'; else token.Append(character); started = true; }
            else if (character is '\'' or '"') { quote = character; started = true; }
            else if (char.IsWhiteSpace(character)) { if (started) { result.Add(token.ToString()); token.Clear(); started = false; } }
            else { token.Append(character); started = true; }
        }
        if (quote != '\0') throw new InvalidOperationException(UiText.T("命令中的引号未闭合。"));
        if (started) result.Add(token.ToString());
        if (result.Count > 0 && result[0].Equals("mise", StringComparison.OrdinalIgnoreCase)) result.RemoveAt(0);
        if (result.Count == 0) throw new InvalidOperationException(UiText.T("请输入 mise 的子命令。"));
        return result;
    }
}
