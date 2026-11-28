using System.Xml.Linq;
using PortableMise.Models;
using PortableMise.Services;

namespace PortableMise.Tests;

public static class PortableEcosystemTests
{
    public static async Task<int> RunAsync(string scratchRoot)
    {
        // The suite deliberately hand-edits generated files later on; clear stale scratch so repeat runs start clean.
        if (Directory.Exists(scratchRoot)) Directory.Delete(scratchRoot, recursive: true);
        var paths = AppPaths.Resolve(new AppSettings(), scratchRoot);
        const string mirror = "https://mirror.example.test/repository/";
        int count = 0;

        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + message);
            Console.WriteLine("PASS: " + message);
            count++;
        }

        foreach (string kind in new[]
                 {
                     "maven", "nuget", "rubygems", "r", "haskell", "composer",
                     "luarocks", "clojure", "perl", "opam", "bun", "poetry", "rye"
                 })
        {
            var results = await PortableEcosystemConfiguration.ApplyAsync(paths, kind, mirror);
            string directory = Path.Combine(paths.ConfigDirectory, "mirrors", kind) + Path.DirectorySeparatorChar;
            Check(results.Count > 0 && results.All(result => result.Path.StartsWith(directory, StringComparison.OrdinalIgnoreCase)
                    && File.ReadAllText(result.Path).Contains(mirror, StringComparison.Ordinal)
                    && result.Instructions.Length > 0) && PortableEcosystemConfiguration.Read(paths, kind) == mirror,
                kind + " generates only workspace files with explicit client usage");
        }

        var mavenFiles = await PortableEcosystemConfiguration.ApplyAsync(paths, "maven", mirror);
        var settings = XDocument.Load(mavenFiles[0].Path);
        XNamespace maven = "http://maven.apache.org/SETTINGS/1.0.0";
        Check(settings.Descendants(maven + "mirrorOf").Single().Value == "central"
            && settings.Descendants(maven + "url").Single().Value == mirror,
            "Maven mirror only redirects central, leaving private repositories alone");

        var nuget = await PortableEcosystemConfiguration.ApplyAsync(paths, "nuget", "https://mirror.example.test/v3");
        var packageSource = XDocument.Load(nuget.Single().Path).Descendants("add").Single();
        Check(packageSource.Attribute("value")?.Value == "https://mirror.example.test/v3/index.json",
            "NuGet v3 source uses its index document");

        string settingsPath = mavenFiles[0].Path;
        string before = File.ReadAllText(settingsPath);
        using (new FileStream(mavenFiles[1].Path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try
            {
                await PortableEcosystemConfiguration.ApplyAsync(paths, "maven", "https://second.example.test/");
                throw new InvalidOperationException("A locked config was replaced.");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        Check(File.ReadAllText(settingsPath) == before && PortableEcosystemConfiguration.Read(paths, "maven") == mirror,
            "a later locked-file error rolls back earlier portable files and preset state");

        File.AppendAllText(settingsPath, "\n<!-- manually managed -->\n");
        string manuallyEdited = File.ReadAllText(settingsPath);
        Check(PortableEcosystemConfiguration.Read(paths, "maven") is null,
            "a manually edited mirror file is no longer reported as the selected preset");
        try
        {
            await PortableEcosystemConfiguration.ApplyAsync(paths, "maven", "https://second.example.test/");
            throw new Exception("Manual configuration was accepted for overwrite.");
        }
        catch (InvalidOperationException)
        {
            Check(File.ReadAllText(settingsPath) == manuallyEdited,
                "manual edits are preserved on preset change");
        }

        try
        {
            await PortableEcosystemConfiguration.ApplyAsync(paths, "maven", "https://user:secret@mirror.example.test/");
            throw new Exception("A credential-bearing URL was accepted.");
        }
        catch (InvalidOperationException)
        {
            Check(File.ReadAllText(settingsPath) == manuallyEdited, "mirror credentials are refused before file writes");
        }
        return count;
    }
}
