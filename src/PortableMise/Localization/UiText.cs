using System.Globalization;
using System.Text.Json;
using System.Windows.Markup;

namespace PortableMise.Localization;

public static class UiText
{
    private static readonly Lazy<Dictionary<string, string>> English = new(LoadEnglish);
    public static bool IsEnglish { get; private set; } =
        !CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    public static void Configure(string? language)
    {
        IsEnglish = language switch
        {
            "zh-CN" => false,
            "en-US" => true,
            _ => !CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
        };
    }

    public static string T(string chinese) => IsEnglish && English.Value.TryGetValue(chinese, out string? translated)
        ? translated : chinese;

    public static string F(string format, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, T(format), arguments);

    private static Dictionary<string, string> LoadEnglish()
    {
        var translations = new Dictionary<string, string>(StringComparer.Ordinal);
        var assembly = typeof(UiText).Assembly;
        foreach (string resource in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith("PortableMise.Localization.English.", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing embedded language resource {resource}.");
            var table = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            if (table != null)
                foreach (var item in table) translations.TryAdd(item.Key, item.Value);
        }
        return translations;
    }
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension(string text) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.T(text);
}
