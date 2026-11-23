using System.Text.Json;
using TokenLimitWatch.Poc;

namespace TokenLimitWatch;

public sealed record Limit(string Name, double? Used, DateTimeOffset? Reset)
{
    public double? Remaining => Used.HasValue ? Math.Clamp(100 - Used.Value, 0, 100) : null;
    public string UsedText => Used.HasValue ? $"{Used:0.#}%" : Loc.T("확인 불가");
    public string RemainingText => Remaining.HasValue ? $"{Remaining:0.#}%" : Loc.T("확인 불가");
    public string ResetText => Reset?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? Loc.T("확인 불가");
    public string Countdown(DateTimeOffset now)
    {
        if (Reset is null) return Loc.T("확인 불가");
        var left = Reset.Value - now;
        if (left <= TimeSpan.Zero) return Loc.T("초기화 확인 중");
        return Loc.Countdown(left);
    }
}

public sealed record Organization(string Id, string Name);
public sealed record UsageResult(bool Success, bool LoginRequired, List<Limit> Limits, List<Organization> Organizations, string? SelectedOrg = null);

public static class UsageProvider
{
    public static List<Limit> ParseClaude(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var limits = new List<Limit>();
        foreach (var (key, name) in new[] { ("five_hour", "5시간 세션"), ("seven_day", "주간 한도"), ("seven_day_opus", "Opus 주간"), ("seven_day_sonnet", "Sonnet 주간") })
        {
            if (!doc.RootElement.TryGetProperty(key, out var obj) || obj.ValueKind != JsonValueKind.Object) continue;
            double? used = obj.TryGetProperty("utilization", out var u) && u.TryGetDoubleSafe(out var n) && double.IsFinite(n) && n >= 0 ? n : null;
            DateTimeOffset? reset = obj.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(r.GetString(), out var dt) ? dt : null;
            limits.Add(new Limit(name, used, reset));
        }
        if (limits.Count == 0) throw new JsonException("사용량 항목이 없습니다.");
        return limits;
    }

    private static bool TryGetDoubleSafe(this JsonElement e, out double n)
    {
        n = 0;
        return e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out n);
    }

    public static async Task<UsageResult> ReadAsync(ServiceConfig service, IFetcher fetcher, string? selectedOrg)
    {
        if (service.Id == "chatgpt")
        {
            var session = await fetcher.GetAsync("/api/auth/session");
            if (!session.Ok) return new(false, session.Status == 401, [], []);
            using var doc = JsonDocument.Parse(session.Body);
            var authenticated = doc.RootElement.TryGetProperty("accessToken", out var token) && token.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(token.GetString());
            // Authentication succeeds independently of usage availability. No invented metrics.
            return new(authenticated, !authenticated, [new("사용 한도", null, null)], []);
        }
        var orgResponse = await fetcher.GetAsync("/api/organizations");
        if (!orgResponse.Ok) return new(false, orgResponse.Status == 401, [], []);
        using var orgDoc = JsonDocument.Parse(orgResponse.Body);
        var orgs = orgDoc.RootElement.EnumerateArray().Select(o => new Organization(o.GetProperty("uuid").GetString()!, o.TryGetProperty("name", out var name) ? name.GetString() ?? "조직" : "조직")).ToList();
        if (orgs.Count == 0) return new(false, false, [], []);
        var org = orgs.FirstOrDefault(o => o.Id == selectedOrg) ?? orgs[0];
        var response = await fetcher.GetAsync($"/api/organizations/{Uri.EscapeDataString(org.Id)}/usage");
        return response.Ok ? new(true, false, ParseClaude(response.Body), orgs, org.Id) : new(false, response.Status == 401, [], orgs, org.Id);
    }
}

public static class PollPolicy
{
    public static DateTimeOffset Next(DateTimeOffset now, bool success, int interval) => now.AddMinutes(success ? interval : 5);
}

public static class DisplayPolicy
{
    public static List<Limit> VisibleLimits(IEnumerable<Limit> limits,bool mini) =>
        (mini ? limits.Where(l => l.Name is "5시간 세션" or "5시간 단위 한도") : limits).ToList();
}
