using System.Diagnostics;
using PortableMise.Models;
using PortableMise.Services;

namespace PortableMise.Tests;

public static class RealIntegrationTests
{
    public static async Task<int> RunAsync(string scratchRoot)
    {
        string root = Path.Combine(Path.GetFullPath(scratchRoot), "original");
        Directory.CreateDirectory(root);
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); count++; Console.WriteLine("PASS: " + message); }
        var settings = new AppSettings();
        var installer = new MiseInstaller(root);
        string version = await installer.InstallAsync(settings, Console.WriteLine);
        var paths = AppPaths.Resolve(settings, root);
        Check(File.Exists(paths.MiseExecutable) && File.Exists(paths.MiseShimExecutable), "official mise and shim are installed together in isolated workspace");
        var mise = new MiseService(root);
        Console.WriteLine("REAL_MISE_VERSION: " + await mise.GetVersionAsync(settings));
        Check(!string.IsNullOrWhiteSpace(version), "official release version is detected");
        await mise.InstallToolAsync(settings, "jq@latest", Console.WriteLine);
        var tools = await mise.GetToolsAsync(settings);
        Check(tools.Any(tool => tool.Name == "jq" && tool.Installed), "real tool installation is reported by mise JSON");
        var env = new MemoryEnvironment();
        var terminal = new TerminalIntegration(root, mise, env);
        await terminal.RepairAsync(settings, Console.WriteLine);
        Check(terminal.IsConfigured(settings), "real mise reshims and registers isolated environment");
        var executable = await mise.RunAsync(settings, new[] { "which", "jq" }); executable.EnsureSuccess();
        Check(Path.GetFullPath(executable.StandardOutput.Trim()).StartsWith(paths.ToolsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "tool executable stays inside configured tool directory");
        await new SettingsStore(root).SaveAsync(settings);
        string moved = Path.Combine(Path.GetFullPath(scratchRoot), "relocated");
        string allowed = Path.GetFullPath(scratchRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(root).StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !Path.GetFullPath(moved).StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Relocation fixture must remain inside its scratch directory.");
        Directory.Move(root, moved);
        var restored = new SettingsStore(moved).Load();
        var relocatedMise = new MiseService(moved);
        var relocatedTerminal = new TerminalIntegration(moved, relocatedMise, env);
        await relocatedTerminal.RepairAsync(restored, Console.WriteLine);
        var result = await relocatedMise.RunAsync(restored, new[] { "exec", "--", "jq", "--version" }, Console.WriteLine);
        result.EnsureSuccess();
        Check(result.StandardOutput.Contains("jq-"), "installed tool still runs after moving whole workspace and repairing");
        Check(!(env.Read("Path") ?? "").Contains(root, StringComparison.OrdinalIgnoreCase), "restoration removes only old tracked terminal paths");
        Console.WriteLine("REAL_INTEGRATION_WORKSPACE=" + moved);
        return count;
    }
    private sealed class MemoryEnvironment : IUserEnvironmentStore
    {
        private readonly Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase) { ["Path"] = @"C:\Windows\System32;%USERPROFILE%\bin" };
        public string? Read(string name) => values.GetValueOrDefault(name);
        public void Write(string name, string? value) => values[name] = value;
        public void Broadcast() { }
    }
}
