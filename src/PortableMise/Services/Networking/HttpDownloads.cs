using System.Net.Http;
using System.Text;

namespace PortableMise.Services.Networking;

public static class HttpDownloads
{
    public static async Task<string> GetTextAsync(HttpClient client, Uri url, int maxBytes, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long declared && declared > maxBytes)
            throw new InvalidDataException("远端元数据超过允许大小。");
        using var output = new MemoryStream();
        using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await CopyBoundedAsync(input, output, maxBytes, null, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    public static async Task DownloadAsync(HttpClient client, Uri url, string destination, long maxBytes,
        Action<long>? progress = null, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long declared && declared > maxBytes)
            throw new InvalidDataException("下载文件超过允许大小。");
        using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await CopyBoundedAsync(input, output, maxBytes, progress, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long maximum, Action<long>? progress, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int length;
        while ((length = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            total += length;
            if (total > maximum) throw new InvalidDataException("远端内容超过允许大小。");
            await output.WriteAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            progress?.Invoke(total);
        }
    }
}
