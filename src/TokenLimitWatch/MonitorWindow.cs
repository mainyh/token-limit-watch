using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using TokenLimitWatch.Poc;
using Forms = System.Windows.Forms;
using static TokenLimitWatch.Loc;

namespace TokenLimitWatch;

public sealed partial class MonitorWindow : Window
{
    private sealed class Slot(ServiceConfig config)
    {
        public ServiceConfig Config = config;
        public List<Limit> Limits = config.Id == "claude" ? [new("5시간 세션", null, null), new("주간 한도", null, null)] : [new("5시간 단위 한도", null, null), new("주간 사용 한도", null, null)];
        public List<Organization> Organizations = [];
        public bool Busy, LoginRequired;
        public DateTimeOffset Due = DateTimeOffset.MinValue;
        public DateTimeOffset? LastUpdate;
        public DateTimeOffset? ResetAttempt;
        public string Message = "로그인하여 조회를 시작하세요";
        public HashSet<string> Notified = [];
    }

    private readonly Settings _settings = Settings.Load();
    private readonly List<Slot> _slots = PocConfig.LoadOrDefault().Services.Select(s => new Slot(s)).ToList();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Forms.NotifyIcon _tray = new() { Text = AppName, Visible = true };
    private readonly StackPanel _body = new();
    private readonly StackPanel _footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly List<(TextBlock Text, Limit Limit)> _countdowns = [];
    private readonly TextBlock _heading = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private string _page = "dashboard";
    private bool _mini, _exit;

    public MonitorWindow()
    {
        Title = AppName; Width = 400; Height = 720; MinWidth = 320; MinHeight = 336;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if(_preview)
        {
            _settings.Services = _slots.Select(s=>s.Config.Id).ToList();
            var reset = DateTimeOffset.Now.AddDays(2).AddHours(7);
            foreach(var slot in _slots)
            {
                slot.Limits = slot.Config.Id == "claude" ? [new("5시간 세션",0,null),new("주간 한도",25,reset)] : [new("5시간 단위 한도",23,DateTimeOffset.Now.AddHours(2)),new("주간 사용 한도",4,reset)];
                slot.LastUpdate=DateTimeOffset.Now;slot.Message="연결됨";
            }
        }
        Loc.Lang = Loc.Normalize(_settings.Language);
        // Preview screenshots: --preview --en shows the English UI without touching settings.
        if(_preview && Environment.GetCommandLineArgs().Contains("--en")) Loc.Lang = "en";
        LoginWindow.Translate = T;
        Topmost = _settings.AlwaysOnTop;
        MigrateStartup();
        BuildChrome();
        BuildTrayMenu();
        _tray.DoubleClick += (_,_) => Dispatcher.Invoke(Restore);
        _timer.Tick += (_,_) => Tick();
        _timer.Start();
        Loaded += (_,_) => { Render(_settings.Services.Count == 0 ? "services" : "dashboard"); Tick(); };
        Closing += OnClosing;
    }

    private Button Button(string text, Action action)
    {
        var b = new Button { Content = text };
        b.Click += (_,_) => { try { action(); } catch (Exception ex) { Report(ex); } };
        return b;
    }
    private void BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(T("대시보드 열기"), null, (_,_) => Dispatcher.Invoke(Restore));
        menu.Items.Add(T("지금 갱신"), null, (_,_) => Dispatcher.Invoke(() => RefreshAll()));
        menu.Items.Add(T("종료"), null, (_,_) => Dispatcher.Invoke(Exit));
        var old = _tray.ContextMenuStrip; _tray.ContextMenuStrip = menu; old?.Dispose();
    }
    private void SetLanguage(string lang)
    {
        Loc.Lang = lang; _settings.Language = lang; Save();
        BuildTrayMenu(); Render();
    }
    private void Save() { if(_preview)return; try { _settings.Save(); } catch (Exception ex) { Report(ex); } }
    private void Report(Exception ex) => MessageBox.Show(this, T("작업을 완료하지 못했습니다.") + "\n" + ex.Message, AppName);
    // Dashboard order follows the order the user selected services in.
    private IEnumerable<Slot> Selected => _settings.Services.Select(id => _slots.FirstOrDefault(s => s.Config.Id == id)).OfType<Slot>();

    private void Render(string? page = null)
    {
        var changed = page != null && page != _page;
        if (page != null) _page = page;
        if (_mini && _page != "dashboard") SetMini(false);
        _body.Children.Clear(); _footer.Children.Clear(); _miniBody.Children.Clear(); _countdowns.Clear(); _refreshLabel = null;
        if (_mini) { DesignMini(); return; }
        ApplyChrome(changed);
        if (_page == "services") DesignServices();
        else if (_page == "accounts") DesignAccounts();
        else if (_page == "settings") DesignSettings();
        else DesignDashboard();
    }

    private async void Login(Slot slot)
    {
        if (slot.Busy) return;
        slot.Busy = true; Render();
        try
        {
            var login = new LoginWindow(slot.Config) { Owner = this }; login.ShowDialog();
            slot.LoginRequired = !login.LoggedIn;
            slot.Message = login.LoggedIn ? "연결됨" : "로그인이 필요합니다";
            slot.Due = DateTimeOffset.MinValue;
        }
        catch (Exception ex) { Report(ex); }
        finally { slot.Busy = false; Render(); }
        await Refresh(slot);
    }

    private async void Logout(Slot slot)
    {
        if (slot.Busy) return;
        slot.Busy = true; Render();
        try
        {
            using var browser = new HiddenBrowser(slot.Config, _ => { });
            await browser.EnsureReadyAsync(false);
            await browser.Core.Profile.ClearBrowsingDataAsync();
            slot.Limits = slot.Config.Id == "claude" ? [new("5시간 세션", null, null), new("주간 한도", null, null)] : [new("5시간 단위 한도", null, null), new("주간 사용 한도", null, null)];
            slot.Organizations.Clear(); slot.Notified.Clear(); slot.LastUpdate = null; slot.ResetAttempt = null;
            slot.Message = "로그인이 필요합니다"; slot.LoginRequired = true; slot.Due = DateTimeOffset.MaxValue;
        }
        catch (Exception ex) { Report(ex); }
        finally { slot.Busy = false; Render(); }
    }

    private void ToggleMini() { SetMini(!_mini); Render("dashboard"); }

    public const string AppName = "Token Limit Watch";
    private const string RunValue = "TokenLimitWatch", LegacyRunValue = "TokenWatch";

    // Earlier builds registered autostart as "TokenWatch"; move that entry to the new name once.
    private void MigrateStartup()
    {
        if(_preview) return;
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if(run?.GetValue(LegacyRunValue) == null) return;
            run.DeleteValue(LegacyRunValue,false);
            run.SetValue(RunValue, "\"" + Environment.ProcessPath + "\"");
        }
        catch { }
    }

    private void Startup(bool enabled)
    {
        if(_preview) return;
        using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        run.DeleteValue(LegacyRunValue,false);
        if (enabled) run.SetValue(RunValue, "\"" + Environment.ProcessPath + "\""); else run.DeleteValue(RunValue,false);
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        foreach(var (text, limit) in _countdowns) text.Text = limit.Countdown(now);
        UpdateRefreshLabel();
        if(_preview) return;
        foreach(var slot in Selected.ToList())
        {
            if(slot.Busy || slot.LoginRequired) continue;
            // Each reset instant triggers at most one immediate query; server values can stay stale.
            var reset = slot.Limits.Where(l => l.Reset.HasValue && l.Reset <= now && (!slot.ResetAttempt.HasValue || l.Reset > slot.ResetAttempt)).Select(l => l.Reset).Max();
            if(reset.HasValue && slot.Due > now) { slot.ResetAttempt = reset; slot.Due = now; }
            if(slot.Due <= now) _ = Refresh(slot);
        }
    }
    private void RefreshAll() { if(_preview)return; foreach(var slot in Selected.ToList()) if(!slot.Busy) _ = Refresh(slot); }

    private async Task Refresh(Slot slot)
    {
        if(slot.Busy) return; slot.Busy = true;
        var success = false;
        try
        {
            using var browser = new HiddenBrowser(slot.Config,_ => {});
            await browser.EnsureReadyAsync(false);
            if(!await browser.HasSessionCookieAsync()) { slot.LoginRequired = true; slot.Message = "로그인이 필요합니다"; return; }
            UsageResult result;
            if(slot.Config.Id == "claude") result = await UsageProvider.ReadAsync(slot.Config,new BrowserFetcher(slot.Config,browser),_settings.ClaudeOrg);
            else
            {
                result = await ChatGptPlanUsage.ReadAsync(browser);
            }
            success = result.Success; slot.LoginRequired = result.LoginRequired;
            if(success)
            {
                NotifyChanges(slot, result.Limits);
                slot.Limits = result.Limits; slot.Organizations = result.Organizations;
                if(result.SelectedOrg != null && _settings.ClaudeOrg != result.SelectedOrg) { _settings.ClaudeOrg = result.SelectedOrg; Save(); }
                slot.LastUpdate = DateTimeOffset.Now; slot.Message = "연결됨";
            }
            else slot.Message = result.LoginRequired ? "다시 로그인하세요" : "조회 실패 · 5분 후 재시도";
            Log(slot,success,success ? "조회 완료" : "조회 실패");
        }
        catch(Exception ex) { slot.Message = "조회 실패 · 5분 후 재시도"; Log(slot,false,ex.GetType().Name); }
        finally
        {
            // Failed values are unknown, never silently shown as current.
            if(!success) slot.Limits = slot.Limits.Select(l => new Limit(l.Name,null,null)).ToList();
            slot.Due = PollPolicy.Next(DateTimeOffset.Now,success,_settings.Interval); slot.Busy = false;
            Render();
        }
    }
    private void NotifyChanges(Slot slot, List<Limit> limits)
    {
        foreach(var limit in limits)
        {
            var old = slot.Limits.FirstOrDefault(l => l.Name == limit.Name);
            // API timestamps can jitter around a minute boundary between requests.
            var key = limit.Name + ":" + (limit.Reset.HasValue ? (long)Math.Round(limit.Reset.Value.ToUnixTimeSeconds() / 60d) : 0);
            if(_settings.ResetNotifications && old?.Reset is { } previous && previous <= DateTimeOffset.Now && limit.Reset != old.Reset)
                _tray.ShowBalloonTip(5000,AppName,F("{0} · {1} 초기화됨",slot.Config.Name,T(limit.Name)),Forms.ToolTipIcon.Info);
            if(_settings.Warnings && limit.Used >= _settings.Threshold && slot.Notified.Add(key))
                _tray.ShowBalloonTip(5000,slot.Config.Name + " · " + T(limit.Name),F("사용량 {0} · 남은량 {1}\n초기화 {2}\n남은 시간 {3}",limit.UsedText,limit.RemainingText,limit.ResetText,limit.Countdown(DateTimeOffset.Now)),Forms.ToolTipIcon.Warning);
        }
    }
    private static void Log(Slot slot, bool success, string status)
    {
        try { File.AppendAllText(Path.Combine(Paths.Data,"monitor.jsonl"),JsonSerializer.Serialize(new { at = DateTimeOffset.Now, service = slot.Config.Id, success, status }) + Environment.NewLine); } catch { }
    }
    internal void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void Exit() { _exit = true; Close(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if(!_exit && _settings.CloseToTray) { e.Cancel = true; Hide(); return; }
        _timer.Stop(); _tray.Dispose(); System.Windows.Application.Current.Shutdown();
    }
}
