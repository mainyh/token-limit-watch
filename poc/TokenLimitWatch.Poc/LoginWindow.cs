using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TokenLimitWatch.Poc;

/// <summary>
/// Shows the service's own login page in WebView2. Closes itself once the session cookie
/// appears. Google sign-in is out of scope (and Google blocks embedded browsers), so
/// navigation to accounts.google.com is cancelled with an explanation.
/// </summary>
public sealed class LoginWindow : Window
{
    private readonly ServiceConfig _service;
    private readonly WebView2 _web = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public bool LoggedIn { get; private set; }

    /// <summary>Optional UI translation hook (the main app plugs in its language table).</summary>
    public static Func<string, string> Translate { get; set; } = s => s;

    public LoginWindow(ServiceConfig service)
    {
        _service = service;
        Title = string.Format(Translate("{0} 로그인 — 이메일로 로그인하세요 (Google 계정 로그인 미지원)"), service.Name);
        Width = 520;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = _web;
        Loaded += async (_, _) => await InitAsync();
        Closed += (_, _) => { _timer.Stop(); _web.Dispose(); };
    }

    private async Task InitAsync()
    {
        try
        {
            var env = await Environments.GetAsync(_service);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.NavigationStarting += OnNavigationStarting;
            _web.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                // Keep popups (e.g. auth providers) inside this window so the rules above apply.
                e.Handled = true;
                if (!IsGoogle(e.Uri)) _web.CoreWebView2.Navigate(e.Uri);
                else WarnGoogle();
            };
            _web.CoreWebView2.Navigate(_service.LoginUrl);

            _timer.Tick += async (_, _) => await CheckCookieAsync();
            _timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Translate("WebView2를 시작하지 못했습니다.") + "\n" + ex.Message, Translate("로그인"), MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private static bool IsGoogle(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.Host.EndsWith("accounts.google.com", StringComparison.OrdinalIgnoreCase);

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!IsGoogle(e.Uri)) return;
        e.Cancel = true;
        WarnGoogle();
    }

    private void WarnGoogle() =>
        MessageBox.Show(this, Translate("Google 계정 로그인은 지원하지 않습니다.\n이메일 주소로 로그인해 주세요."), Translate("로그인"), MessageBoxButton.OK, MessageBoxImage.Information);

    private async Task CheckCookieAsync()
    {
        if (_web.CoreWebView2 == null || LoggedIn) return;
        var cookies = await _web.CoreWebView2.CookieManager.GetCookiesAsync(_service.Origin);
        if (!cookies.Any(c => c.Name.StartsWith(_service.SessionCookiePrefix, StringComparison.Ordinal))) return;

        LoggedIn = true;
        _timer.Stop();
        await Task.Delay(1500); // let the site finish its post-login redirects
        Close();
    }
}
