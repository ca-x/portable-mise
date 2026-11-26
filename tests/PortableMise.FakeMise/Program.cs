using System.Text.Json;

if (args.Contains("--probe-environment"))
{
    Console.WriteLine(JsonSerializer.Serialize(new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "MISE_DATA_DIR", "MISE_CONFIG_DIR", "MISE_SHIMS_DIR" }
        .ToDictionary(name => name, Environment.GetEnvironmentVariable)));
    return 0;
}
if (args.Contains("--fail"))
{
    Console.Error.WriteLine("intentional fixture failure");
    return 7;
}
if (args.Contains("--wait"))
{
    Console.WriteLine("fixture-pid=" + Environment.ProcessId);
    Console.Out.Flush();
    await Task.Delay(TimeSpan.FromMinutes(5));
    return 0;
}
if (args.FirstOrDefault() == "use")
    Console.WriteLine(JsonSerializer.Serialize(args));
else if (args.Contains("ls"))
    Console.WriteLine("{\"node\":[{\"version\":\"22.12.0\",\"active\":true,\"installed\":true}]}");
else if (args.Contains("--version")) Console.WriteLine("mise test-fixture");
else Console.WriteLine("fixture operation completed");
return 0;
