using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TokenLimitWatch.Poc;

/// <summary>
/// One WebView2 profile per service (data\webview2\&lt;id&gt;), shared by the login window
/// and the hidden browser used for polling.
/// </summary>
public static class Environments
{
    private static readonly Dictionary<string, Task<CoreWebView2Environment>> Cache = new();

    public static Task<CoreWebView2Environment> GetAsync(ServiceConfig s)
    {
        if (!Cache.TryGetValue(s.Id, out var task))
        {
            task = CoreWebView2Environment.CreateAsync(null, Paths.WebViewProfile(s));
            Cache[s.Id] = task;
        }
        return task;
    }
}

/// <summary>
/// An off-screen window holding a WebView2 that sits on the service's origin.
/// Both fetch methods use it: the browser method runs fetch() in it, the HTTP method
/// borrows its cookies and user agent.
/// </summary>
public sealed class HiddenBrowser : IDisposable
{
    private readonly ServiceConfig _service;
    private readonly Action<string> _log;
    private Window? _window;
    private WebView2? _web;
    private readonly Dictionary<string, TaskCompletionSource<string>> _pending = new();
    private bool _disposing;

    public void Dispose()
    {
        _disposing = true;
        foreach (var pending in _pending.Values) pending.TrySetCanceled();
        _pending.Clear();
        _web?.Dispose();
        _window?.Close();
        _web = null;
        _window = null;
    }

    public HiddenBrowser(ServiceConfig service, Action<string> log)
    {
        _service = service;
        _log = log;
    }

    public CoreWebView2 Core => _web?.CoreWebView2 ?? throw new InvalidOperationException("브라우저가 준비되지 않았습니다.");

    public void SetVisible(bool visible)
    {
        if (_window == null) return;
        if (visible)
        {
            _window.Left = 80; _window.Top = 80;
            _window.ShowInTaskbar = true;
        }
        else
        {
            _window.Left = -20000; _window.Top = -20000;
            _window.ShowInTaskbar = false;
        }
    }

    public async Task EnsureReadyAsync(bool visible)
    {
        if (_web?.CoreWebView2 != null)
        {
            SetVisible(visible);
            return;
        }

        _web = new WebView2();
        _window = new Window
        {
            Title = $"{_service.Name} 조회용 브라우저 (PoC)",
            Width = 900, Height = 700,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowActivated = false,
            Content = _web,
        };
        SetVisible(visible);
        _window.Closing += (_, e) => { if (!_disposing) { e.Cancel = true; SetVisible(false); } };
        _window.Show();

        var env = await Environments.GetAsync(_service);
        await _web.EnsureCoreWebView2Async(env);
        _web.CoreWebView2.WebMessageReceived += OnMessage;

        await NavigateAndSettleAsync(_service.Origin + "/");
    }

    private async Task NavigateAndSettleAsync(string url)
    {
        var done = new TaskCompletionSource<bool>();
        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult(e.IsSuccess);
        Core.NavigationCompleted += Handler;
        Core.Navigate(url);
        await Task.WhenAny(done.Task, Task.Delay(30_000));
        Core.NavigationCompleted -= Handler;

        // Cloudflare's interstitial usually solves itself in a few seconds.
        for (var i = 0; i < 15; i++)
        {
            var title = JsonSerializer.Deserialize<string>(await Core.ExecuteScriptAsync("document.title")) ?? "";
            if (!title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) && !title.Contains("잠시만", StringComparison.Ordinal))
                return;
            if (i == 0) _log($"[{_service.Name}] Cloudflare 확인 페이지 대기 중 ...");
            await Task.Delay(1000);
        }
        _log($"[{_service.Name}] 15초가 지나도 Cloudflare 확인 페이지가 남아 있습니다. '숨긴 브라우저 창 보기'로 직접 확인하세요.");
    }

    public Task NavigateUsagePageAsync() => NavigateAndSettleAsync(_service.Origin + "/settings/usage?tab=overview");

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;
        try { raw = e.TryGetWebMessageAsString(); } catch { return; }
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("__twId", out var idEl)) return;
            var id = idEl.GetString();
            if (id != null && _pending.Remove(id, out var tcs)) tcs.TrySetResult(raw);
        }
        catch (JsonException) { /* not ours */ }
    }

    /// <summary>Runs fetch() inside the page and waits for the posted result.</summary>
    public async Task<string> FetchInPageAsync(string url, IReadOnlyDictionary<string, string>? headers)
    {
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string>();
        _pending[id] = tcs;

        var script = $$"""
            (async () => {
              const id = {{JsonSerializer.Serialize(id)}};
              const t0 = performance.now();
              try {
                const r = await fetch({{JsonSerializer.Serialize(url)}}, { credentials: 'include', headers: {{JsonSerializer.Serialize(headers ?? new Dictionary<string, string>())}} });
                const body = await r.text();
                window.chrome.webview.postMessage(JSON.stringify({ __twId: id, status: r.status, body, ms: performance.now() - t0, cf: r.headers.get('cf-mitigated') }));
              } catch (e) {
                window.chrome.webview.postMessage(JSON.stringify({ __twId: id, status: 0, body: '', ms: performance.now() - t0, error: String(e) }));
              }
            })();
            """;
        await Core.ExecuteScriptAsync(script);

        var finished = await Task.WhenAny(tcs.Task, Task.Delay(30_000));
        if (finished != tcs.Task)
        {
            _pending.Remove(id);
            throw new TimeoutException("페이지 안 fetch가 30초 안에 끝나지 않았습니다.");
        }
        return tcs.Task.Result;
    }

    public async Task<string> UserAgentAsync() =>
        JsonSerializer.Deserialize<string>(await Core.ExecuteScriptAsync("navigator.userAgent")) ?? "";

    public async Task<string> CookieHeaderAsync()
    {
        var cookies = await Core.CookieManager.GetCookiesAsync(_service.Origin);
        return string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
    }

    public async Task<bool> HasSessionCookieAsync()
    {
        var cookies = await Core.CookieManager.GetCookiesAsync(_service.Origin);
        return cookies.Any(c => c.Name.StartsWith(_service.SessionCookiePrefix, StringComparison.Ordinal));
    }
}

/// <summary>Method A: fetch() executed inside the hidden WebView2 page.</summary>
public sealed class BrowserFetcher : IFetcher
{
    private readonly ServiceConfig _service;
    private readonly HiddenBrowser _browser;

    public BrowserFetcher(ServiceConfig service, HiddenBrowser browser)
    {
        _service = service;
        _browser = browser;
    }

    public string Kind => "browser";

    public async Task<FetchResult> GetAsync(string path, IReadOnlyDictionary<string, string>? headers = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var raw = await _browser.FetchInPageAsync(_service.Origin + path, headers);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var status = root.GetProperty("status").GetInt32();
            var body = root.GetProperty("body").GetString() ?? "";
            var cf = root.TryGetProperty("cf", out var cfEl) && cfEl.ValueKind == JsonValueKind.String ? cfEl.GetString() : null;
            var error = root.TryGetProperty("error", out var errEl) ? errEl.GetString() : null;
            return new FetchResult
            {
                Status = status,
                Body = body,
                ElapsedMs = (long)root.GetProperty("ms").GetDouble(),
                Error = error,
                Blocked = FetchResult.LooksBlocked(status, body, cf),
            };
        }
        catch (Exception ex)
        {
            return new FetchResult { Status = 0, ElapsedMs = sw.ElapsedMilliseconds, Error = ex.Message };
        }
    }
}

/// <summary>Method B: plain HttpClient carrying the WebView2 profile's cookies and user agent.</summary>
public sealed class HttpFetcher : IFetcher
{
    private static readonly HttpClient Client = new(new HttpClientHandler { UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.All })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private readonly ServiceConfig _service;
    private readonly HiddenBrowser _browser;

    public HttpFetcher(ServiceConfig service, HiddenBrowser browser)
    {
        _service = service;
        _browser = browser;
    }

    public string Kind => "http";

    public async Task<FetchResult> GetAsync(string path, IReadOnlyDictionary<string, string>? headers = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, _service.Origin + path);
            req.Headers.TryAddWithoutValidation("User-Agent", await _browser.UserAgentAsync());
            req.Headers.TryAddWithoutValidation("Cookie", await _browser.CookieHeaderAsync());
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            req.Headers.TryAddWithoutValidation("Referer", _service.Origin + "/");
            if (headers != null)
                foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);

            using var res = await Client.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            var cf = res.Headers.TryGetValues("cf-mitigated", out var vals) ? vals.FirstOrDefault() : null;
            var status = (int)res.StatusCode;
            return new FetchResult
            {
                Status = status,
                Body = body,
                ElapsedMs = sw.ElapsedMilliseconds,
                Blocked = FetchResult.LooksBlocked(status, body, cf),
            };
        }
        catch (Exception ex)
        {
            return new FetchResult { Status = 0, ElapsedMs = sw.ElapsedMilliseconds, Error = ex.Message };
        }
    }
}
