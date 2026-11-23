using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TokenLimitWatch.Poc;

/// <summary>
/// The questions the PoC asks each service. Endpoints are the ones the services' own
/// web pages call; they are unofficial and may change, which is exactly what the PoC checks.
/// </summary>
public static class Probes
{
    public static Task<ProbeOutcome> RunAsync(ServiceConfig s, IFetcher f) => s.Id switch
    {
        "claude" => ClaudeAsync(s, f),
        "chatgpt" => ChatGptAsync(s, f),
        _ => GenericAsync(s, f),
    };

    // Claude: /api/organizations -> org uuid -> /api/organizations/{uuid}/usage
    private static async Task<ProbeOutcome> ClaudeAsync(ServiceConfig s, IFetcher f)
    {
        var o = new ProbeOutcome();

        var orgs = await f.GetAsync("/api/organizations");
        o.Steps.Add(Log("/api/organizations", orgs));
        if (!orgs.Ok) { o.Summary = Fail("조직 목록", orgs); return o; }

        var orgIds = new List<string>();
        if (TryParse(orgs.Body) is JsonArray arr)
            foreach (var item in arr)
                if (item?["uuid"]?.GetValue<string>() is { } uuid) orgIds.Add(uuid);
        if (orgIds.Count == 0) { o.Summary = "조직 목록에 uuid가 없습니다 (응답 형식 변경 가능성)"; return o; }

        var usage = await f.GetAsync($"/api/organizations/{orgIds[0]}/usage");
        o.Steps.Add(Log("/api/organizations/{org}/usage", usage));
        if (!usage.Ok) { o.Summary = Fail("사용량", usage); return o; }

        var limits = new List<string>();
        if (TryParse(usage.Body) is JsonObject root)
        {
            foreach (var (name, node) in root)
            {
                if (node is not JsonObject win || win["utilization"] is not JsonValue u) continue;
                var pct = u.TryGetValue<double>(out var d) ? d.ToString("0.#") : u.ToJsonString();
                var reset = win["resets_at"]?.GetValue<string>() is { } r && DateTimeOffset.TryParse(r, out var dt)
                    ? dt.ToLocalTime().ToString("MM-dd HH:mm")
                    : "초기화 시각 없음";
                limits.Add($"{name} {pct}% (초기화 {reset})");
            }
        }

        o.Success = limits.Count > 0;
        o.Summary = o.Success
            ? $"조직 {orgIds.Count}개 · " + string.Join(" · ", limits)
            : "사용량 응답에 utilization 필드가 없습니다 (응답 형식 변경 가능성)";
        return o;
    }

    // ChatGPT: /api/auth/session -> accessToken -> authenticated backend-api calls.
    // The PoC does not know where limit info lives yet; it records keys that look like it.
    private static async Task<ProbeOutcome> ChatGptAsync(ServiceConfig s, IFetcher f)
    {
        var o = new ProbeOutcome();

        var session = await f.GetAsync("/api/auth/session");
        o.Steps.Add(Log("/api/auth/session", session));
        if (!session.Ok) { o.Summary = Fail("세션", session); return o; }

        var token = TryParse(session.Body)?["accessToken"]?.GetValue<string>();
        if (string.IsNullOrEmpty(token)) { o.Summary = "세션 응답에 accessToken이 없습니다 (로그인 필요)"; return o; }

        var auth = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token };
        var okCount = 0;
        var hits = new List<string>();
        foreach (var path in s.ExtraEndpoints)
        {
            var r = await f.GetAsync(path, auth);
            var step = Log(path, r);
            o.Steps.Add(step);
            if (r.Ok) okCount++;
            hits.AddRange(step.KeyHits.Select(h => $"{path}: {h}"));
        }

        o.Success = s.ExtraEndpoints.Count == 0 || okCount > 0;
        o.Summary = $"세션 확인 · 인증 API {okCount}/{s.ExtraEndpoints.Count} 성공"
                    + (hits.Count > 0 ? $" · 한도 관련 키 {hits.Count}개 발견" : " · 한도 관련 키 없음");
        return o;
    }

    private static async Task<ProbeOutcome> GenericAsync(ServiceConfig s, IFetcher f)
    {
        var o = new ProbeOutcome();
        foreach (var path in s.ExtraEndpoints)
        {
            var r = await f.GetAsync(path);
            o.Steps.Add(Log(path, r));
        }
        o.Success = o.Steps.Count > 0 && o.Steps.All(x => x.Status is >= 200 and < 300 && !x.Blocked);
        o.Summary = $"{o.Steps.Count(x => x.Status is >= 200 and < 300 && !x.Blocked)}/{o.Steps.Count} 성공";
        return o;
    }

    private static string Fail(string what, FetchResult r) =>
        r.Blocked ? $"{what} 요청이 Cloudflare에 차단됨 (HTTP {r.Status})"
        : r.Error != null ? $"{what} 요청 실패: {r.Error}"
        : r.Status is 401 or 403 ? $"{what} 요청 인증 실패 (HTTP {r.Status}) — 로그인 필요"
        : $"{what} 요청 실패 (HTTP {r.Status})";

    // ---------- logging helpers: never store tokens, emails or names ----------

    private static readonly Regex LimitKey = new("(limit|reset|cap|remaining|quota|utiliz|usage|window|throttl)", RegexOptions.IgnoreCase);
    private static readonly Regex KeepKey = new("(limit|reset|cap|remaining|quota|utiliz|usage|window|plan|tier|type|status|model|slug|percent|throttl)", RegexOptions.IgnoreCase);
    private static readonly Regex Secret = new("(token|secret|key|email|name|phone|cookie|auth|session|id$|_id|uuid)", RegexOptions.IgnoreCase);

    private static StepLog Log(string path, FetchResult r)
    {
        var node = TryParse(r.Body);
        var hits = new List<string>();
        if (node != null) CollectHits(node, "$", hits);
        return new StepLog
        {
            Path = path,
            Status = r.Status,
            Ms = r.ElapsedMs,
            Blocked = r.Blocked,
            Error = r.Error,
            KeyHits = hits.Take(40).ToList(),
            Shape = node != null ? Sanitize(node, "") : NonJsonSummary(r.Body),
        };
    }

    private static JsonNode? TryParse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var t = body.TrimStart();
        if (t[0] != '{' && t[0] != '[') return null;
        try { return JsonNode.Parse(body); } catch (JsonException) { return null; }
    }

    private static string NonJsonSummary(string body)
    {
        var m = Regex.Match(body, "<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? $"<html title=\"{m.Groups[1].Value.Trim()}\" length={body.Length}>" : $"<non-json length={body.Length}>";
    }

    private static void CollectHits(JsonNode node, string path, List<string> hits)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (k, v) in obj)
                {
                    var p = $"{path}.{k}";
                    if (LimitKey.IsMatch(k) && !Secret.IsMatch(k))
                        hits.Add($"{p} = {Short(v)}");
                    if (v != null) CollectHits(v, p, hits);
                }
                break;
            case JsonArray arr:
                for (var i = 0; i < Math.Min(arr.Count, 5); i++)
                    if (arr[i] != null) CollectHits(arr[i]!, $"{path}[{i}]", hits);
                break;
        }
    }

    private static string Short(JsonNode? v)
    {
        if (v == null) return "null";
        var s = v is JsonValue jv && jv.TryGetValue<string>(out var str) ? $"\"{str}\"" : v.ToJsonString();
        return s.Length > 120 ? s[..120] + "…" : s;
    }

    private static readonly Regex DateLike = new(@"^\d{4}-\d{2}-\d{2}([T ][\d:.]+)?(Z|[+-]\d{2}:?\d{2})?$");

    /// <summary>Keeps structure, numbers and limit-ish strings; masks every other string.</summary>
    private static JsonNode? Sanitize(JsonNode? node, string key)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
            {
                var copy = new JsonObject();
                foreach (var (k, v) in obj) copy[k] = Sanitize(v, k);
                return copy;
            }
            case JsonArray arr:
            {
                var copy = new JsonArray();
                foreach (var item in arr.Take(3)) copy.Add(Sanitize(item, key));
                if (arr.Count > 3) copy.Add($"… +{arr.Count - 3}");
                return copy;
            }
            case JsonValue v when v.TryGetValue<string>(out var s):
                if (!Secret.IsMatch(key) && (KeepKey.IsMatch(key) || DateLike.IsMatch(s)))
                    return s.Length > 80 ? s[..80] + "…" : s;
                return $"<string:{s.Length}>";
            default:
                return node.DeepClone();
        }
    }
}
