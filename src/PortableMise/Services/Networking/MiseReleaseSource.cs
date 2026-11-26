using System.Globalization;
using System.Net.Http;
using System.Text.Json;

using PortableMise.Localization;

namespace PortableMise.Services.Networking;

public static class MiseReleaseSource
{
    public static async Task<string> ResolveAsync(HttpClient client, CancellationToken cancellationToken = default)
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        try
        {
            string text = await HttpDownloads.GetTextAsync(client, new Uri("https://mise.jdx.dev/releases.tsv"), 2 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
            var versions = new List<Version>();
            foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.Trim().Split('\t');
                if (fields.Length != 2 || !Version.TryParse(fields[0].TrimStart('v'), out var version) || version.Build < 0 || version.Revision >= 0 ||
                    !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out long timestamp))
                    throw new InvalidDataException(UiText.T("mise 版本索引格式不受支持。"));
                if (timestamp <= cutoff.ToUnixTimeSeconds()) versions.Add(version);
            }
            if (versions.Count != 0) return versions.Max()!.ToString(3);
            throw new InvalidDataException(UiText.T("尚无满足 24 小时发布间隔的稳定 mise 版本。"));
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException)
        {
            string json = await HttpDownloads.GetTextAsync(client, new Uri("https://api.github.com/repos/jdx/mise/releases?per_page=30"), 4 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var versions = new List<Version>();
            foreach (var release in document.RootElement.EnumerateArray())
            {
                if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean() ||
                    !release.TryGetProperty("published_at", out var date) || !date.TryGetDateTimeOffset(out var published) || published > cutoff ||
                    !Version.TryParse(release.GetProperty("tag_name").GetString()?.TrimStart('v'), out var version) || version.Build < 0 || version.Revision >= 0) continue;
                versions.Add(version);
            }
            if (versions.Count == 0) throw new InvalidOperationException(UiText.T("未找到发布满 24 小时的稳定 mise 版本。"));
            return versions.Max()!.ToString(3);
        }
    }
}
