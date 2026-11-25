using System.IO.Compression;
using System.Security.Cryptography;

namespace PortableMise.Services.Networking;

public static class VerifiedMiseArchive
{
    public static async Task VerifyAsync(string archive, string checksums, string assetName, CancellationToken cancellationToken = default)
    {
        string? expected = null;
        foreach (string line in checksums.Split('\n'))
        {
            string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 2) continue;
            string name = fields[1].TrimStart('*');
            if (name == assetName || name == "./" + assetName)
            {
                if (fields[0].Length != 64 || !fields[0].All(Uri.IsHexDigit)) throw new InvalidDataException("mise 校验文件的 SHA256 格式无效。");
                if (expected != null && !expected.Equals(fields[0], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("mise 校验记录存在冲突。");
                expected = fields[0];
            }
        }
        if (expected == null) throw new InvalidDataException("官方校验文件没有此安装包的记录，已停止安装。");
        await using var input = File.OpenRead(archive);
        string actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("mise 安装包 SHA256 校验失败，已停止安装。");
    }

    public static async Task ExtractAsync(string archive, string outputDirectory, CancellationToken cancellationToken = default)
    {
        using var zip = ZipFile.OpenRead(archive);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            string path = entry.FullName.Replace('\\', '/');
            if (path.StartsWith('/') || path.Contains(':') || path.Split('/').Any(part => part is ".." or "."))
                throw new InvalidDataException("mise 安装包包含不安全的路径。");
            total = checked(total + entry.Length);
            if (total > 512L * 1024 * 1024) throw new InvalidDataException("mise 安装包解压大小超限。");
        }
        Directory.CreateDirectory(outputDirectory);
        foreach (string name in new[] { "mise.exe", "mise-shim.exe" })
        {
            var entries = zip.Entries.Where(e => e.FullName.Replace('\\', '/') == "mise/bin/" + name).ToArray();
            if (entries.Length != 1 || entries[0].Length < 2 || entries[0].Length > 256L * 1024 * 1024)
                throw new InvalidDataException("mise 安装包缺少唯一且有效的 " + name + "。");
            await using var input = entries[0].Open();
            await using var output = new FileStream(Path.Combine(outputDirectory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
        foreach (string name in new[] { "mise.exe", "mise-shim.exe" })
        {
            using var file = File.OpenRead(Path.Combine(outputDirectory, name));
            if (file.ReadByte() != 'M' || file.ReadByte() != 'Z') throw new InvalidDataException("mise 安装包中的程序格式无效。");
        }
    }
}
