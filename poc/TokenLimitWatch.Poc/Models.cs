using System.IO;
using System.Text.Json;

namespace TokenLimitWatch.Poc;

public sealed class ServiceConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Origin { get; set; } = "";
    public string LoginUrl { get; set; } = "";
    public string SessionCookiePrefix { get; set; } = "";
    public List<string> ExtraEndpoints { get; set; } = new();
}

public sealed class PocConfig
{
    public List<ServiceConfig> Services { get; set; } = new();

    public static PocConfig Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "poc-config.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PocConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException("poc-config.json을 읽지 못했습니다.");
    }
}

public static class Paths
{
    public static string Data
    {
        get
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string WebViewProfile(ServiceConfig s) => Path.Combine(Data, "webview2", s.Id);
    public static string ResultsFile => Path.Combine(Data, "poc-results.jsonl");
}

/// <summary>One HTTP GET made by either fetch method.</summary>
public sealed class FetchResult
{
    public int Status { get; init; }
    public string Body { get; init; } = "";
    public long ElapsedMs { get; init; }
    public string? Error { get; init; }
    public bool Blocked { get; init; }

    public bool Ok => Status is >= 200 and < 300 && !Blocked;

    public static bool LooksBlocked(int status, string body, string? cfMitigated)
    {
        if (!string.IsNullOrEmpty(cfMitigated)) return true;
        if (status is 403 or 429 or 503)
        {
            return body.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)
                || body.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase)
                || body.Contains("cf-chl", StringComparison.OrdinalIgnoreCase)
                || body.Contains("Attention Required", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}

public interface IFetcher
{
    /// <summary>"browser" (fetch inside hidden WebView2) or "http" (HttpClient + exported cookies).</summary>
    string Kind { get; }
    Task<FetchResult> GetAsync(string path, IReadOnlyDictionary<string, string>? headers = null);
}

public sealed class StepLog
{
    public string Path { get; init; } = "";
    public int Status { get; init; }
    public long Ms { get; init; }
    public bool Blocked { get; init; }
    public string? Error { get; init; }
    /// <summary>JSON keys that look like limit/reset info, with sanitized values.</summary>
    public List<string> KeyHits { get; init; } = new();
    /// <summary>Response shape with personal strings redacted.</summary>
    public object? Shape { get; init; }
}

public sealed class ProbeOutcome
{
    public bool Success { get; set; }
    public string Summary { get; set; } = "";
    public List<StepLog> Steps { get; } = new();
}
