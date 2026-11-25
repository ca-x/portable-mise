using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using PortableMise.Models;

namespace PortableMise.Services;

public static partial class ProxySupport
{
    private static readonly string[] ProxyVariables =
    ["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "npm_config_proxy", "npm_config_https_proxy"];

    public static void Validate(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.ProxyMode)) throw new InvalidOperationException("代理模式不受支持。");
        if (Uri.TryCreate(settings.ProxyAddress?.Trim(), UriKind.Absolute, out var address) && address.UserInfo.Length != 0)
            throw new InvalidOperationException("代理地址请勿包含用户名或密码；可使用本机代理客户端提供的无认证转发地址。");
        if (settings.ProxyMode == ProxyMode.Custom) _ = CustomProxy(settings);
    }

    private static Uri CustomProxy(AppSettings settings)
    {
        if (!Uri.TryCreate(settings.ProxyAddress?.Trim(), UriKind.Absolute, out var address) ||
            address.Scheme is not ("http" or "https" or "socks5") || string.IsNullOrWhiteSpace(address.Host) ||
            address.Port is <= 0 or > 65535 || address.AbsolutePath != "/" || address.Query.Length != 0 || address.Fragment.Length != 0)
            throw new InvalidOperationException("请输入有效的 HTTP、HTTPS 或 SOCKS5 代理，例如 http://127.0.0.1:7890。");
        if (address.UserInfo.Length != 0)
            throw new InvalidOperationException("代理地址请勿包含用户名或密码；可使用本机代理客户端提供的无认证转发地址。");
        return address;
    }

    public static HttpClient CreateClient(AppSettings settings)
    {
        Validate(settings);
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        if (settings.ProxyMode == ProxyMode.Disabled) handler.UseProxy = false;
        else if (settings.ProxyMode == ProxyMode.Custom)
            handler.Proxy = new WebProxy(CustomProxy(settings)) { BypassProxyOnLocal = false };
        else handler.DefaultProxyCredentials = CredentialCache.DefaultNetworkCredentials;
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("portable-mise/0.1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static IReadOnlyDictionary<string, string?> GetProcessEnvironment(AppSettings settings)
    {
        Validate(settings);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (settings.ProxyMode == ProxyMode.Disabled)
        {
            foreach (string variable in ProxyVariables) values[variable] = null;
            return values;
        }
        if (settings.ProxyMode == ProxyMode.Custom)
        {
            string proxy = CustomProxy(settings).GetLeftPart(UriPartial.Authority);
            foreach (string variable in ProxyVariables) values[variable] = variable == "NO_PROXY" ? "localhost,127.0.0.1,::1" : proxy;
            return values;
        }
        SetSystemProxy(values, "HTTP_PROXY", "http://github.com");
        SetSystemProxy(values, "HTTPS_PROXY", "https://github.com");
        if (values.TryGetValue("HTTP_PROXY", out string? http)) values["npm_config_proxy"] = http;
        if (values.TryGetValue("HTTPS_PROXY", out string? https)) values["npm_config_https_proxy"] = https;
        return values;
    }

    public static IReadOnlyDictionary<string, string?> BuildEnvironment(AppSettings settings) => GetProcessEnvironment(settings);

    private static void SetSystemProxy(Dictionary<string, string?> values, string variable, string target)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable))) return;
        var destination = new Uri(target);
        Uri? proxy = HttpClient.DefaultProxy.GetProxy(destination);
        if (proxy != null && proxy != destination && !HttpClient.DefaultProxy.IsBypassed(destination))
            values[variable] = proxy.GetLeftPart(UriPartial.Authority);
    }

    public static string Sanitize(string text)
    {
        string clean = AnsiSequences().Replace(text ?? string.Empty, string.Empty);
        clean = UrlCredentials().Replace(clean, "${scheme}[redacted]@");
        return AuthorizationHeaders().Replace(clean, "$1: [redacted]");
    }

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex AnsiSequences();
    [GeneratedRegex(@"(?<scheme>(?:https?|socks5)://)[^\s/]*@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlCredentials();
    [GeneratedRegex(@"(?im)(authorization|proxy-authorization)\s*[:=]\s*[^\r\n]+")]
    private static partial Regex AuthorizationHeaders();
}
