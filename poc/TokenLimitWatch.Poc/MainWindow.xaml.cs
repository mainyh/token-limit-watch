using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TokenLimitWatch.Poc;

public partial class MainWindow : Window
{
    private sealed class Stat
    {
        public int Runs, Ok, Blocked;
        public long TotalMs;
        public string Last = "";
        public override string ToString() =>
            Runs == 0 ? "아직 실행 안 함"
            : $"{Ok}/{Runs} 성공 · 차단 {Blocked} · 평균 {TotalMs / Runs}ms · 최근: {Last}";
    }

    private sealed class Slot
    {
        public required ServiceConfig Config;
        public required HiddenBrowser Browser;
        public required IFetcher A, B;
        public readonly Stat StatA = new(), StatB = new();
        public required TextBlock Status, SummaryA, SummaryB;
        public required Panel Buttons;
        public bool Busy;
    }

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly List<Slot> _slots = new();

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            foreach (var s in PocConfig.Load().Services) _slots.Add(BuildSlot(s));
        }
        catch (Exception ex)
        {
            Log("설정 파일 오류: " + ex.Message);
        }
        Log($"데이터 폴더: {Paths.Data}");
    }

    private Slot BuildSlot(ServiceConfig s)
    {
        var browser = new HiddenBrowser(s, Log);
        var status = new TextBlock
        {
            Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Text = Directory.Exists(Paths.WebViewProfile(s)) ? "이전 로그인 기록 있음 (미확인)" : "로그인 필요",
        };
        var summaryA = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "A 브라우저 fetch: 아직 실행 안 함" };
        var summaryB = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "B HttpClient: 아직 실행 안 함", Margin = new Thickness(0, 2, 0, 0) };
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 8) };

        var slot = new Slot
        {
            Config = s, Browser = browser,
            A = new BrowserFetcher(s, browser), B = new HttpFetcher(s, browser),
            Status = status, SummaryA = summaryA, SummaryB = summaryB, Buttons = buttons,
        };

        buttons.Children.Add(MakeButton("로그인", () => LoginAsync(slot)));
        buttons.Children.Add(MakeButton("A 조회", () => RunAsync(slot, useA: true, useB: false, times: 1)));
        buttons.Children.Add(MakeButton("B 조회", () => RunAsync(slot, useA: false, useB: true, times: 1)));
        buttons.Children.Add(MakeButton("A·B 반복 비교", () => RunAsync(slot, useA: true, useB: true, times: ReadInt(RepeatCount, 5, 1, 50))));

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = s.Name, FontSize = 15, FontWeight = FontWeights.Bold });
        header.Children.Add(status);

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(buttons);
        body.Children.Add(summaryA);
        body.Children.Add(summaryB);

        ServiceList.Items.Add(new Border
        {
            Background = Brushes.White, BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 10), Child = body,
        });
        return slot;
    }

    private Button MakeButton(string text, Func<Task> action)
    {
        var b = new Button { Content = text };
        b.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception ex) { Log("오류: " + ex.Message); }
        };
        return b;
    }

    private static int ReadInt(TextBox box, int fallback, int min, int max) =>
        int.TryParse(box.Text, out var n) ? Math.Clamp(n, min, max) : fallback;

    private void SetBusy(Slot slot, bool busy)
    {
        slot.Busy = busy;
        foreach (UIElement c in slot.Buttons.Children) c.IsEnabled = !busy;
    }

    private async Task LoginAsync(Slot slot)
    {
        if (slot.Busy) return;
        var w = new LoginWindow(slot.Config) { Owner = this };
        w.ShowDialog();
        slot.Status.Text = w.LoggedIn ? "로그인됨" : "로그인 안 됨";
        Log($"[{slot.Config.Name}] 로그인 창 닫힘 · 세션 쿠키 {(w.LoggedIn ? "확인" : "없음")}");
        await Task.CompletedTask;
    }

    private async Task RunAsync(Slot slot, bool useA, bool useB, int times)
    {
        if (slot.Busy) return;
        SetBusy(slot, true);
        try
        {
            await slot.Browser.EnsureReadyAsync(ShowBrowser.IsChecked == true);
            if (!await slot.Browser.HasSessionCookieAsync())
            {
                slot.Status.Text = "로그인 필요";
                Log($"[{slot.Config.Name}] 세션 쿠키가 없습니다. 먼저 로그인하세요.");
                return;
            }
            slot.Status.Text = "로그인됨";

            var gap = ReadInt(RepeatGap, 5, 1, 600);
            for (var i = 1; i <= times; i++)
            {
                if (useA) await RunOnceAsync(slot, slot.A, slot.StatA, i);
                if (useB) await RunOnceAsync(slot, slot.B, slot.StatB, i);
                if (i < times) await Task.Delay(TimeSpan.FromSeconds(gap));
            }
            if (times > 1)
                Log($"[{slot.Config.Name}] 반복 {times}회 완료 — A: {slot.StatA} | B: {slot.StatB}");
        }
        finally
        {
            SetBusy(slot, false);
        }
    }

    private async Task RunOnceAsync(Slot slot, IFetcher fetcher, Stat stat, int run)
    {
        var label = fetcher.Kind == "browser" ? "A 브라우저 fetch" : "B HttpClient";
        var sw = Stopwatch.StartNew();
        var outcome = await Probes.RunAsync(slot.Config, fetcher);
        sw.Stop();

        stat.Runs++;
        if (outcome.Success) stat.Ok++;
        if (outcome.Steps.Any(s => s.Blocked)) stat.Blocked++;
        stat.TotalMs += sw.ElapsedMilliseconds;
        stat.Last = outcome.Summary;
        (fetcher.Kind == "browser" ? slot.SummaryA : slot.SummaryB).Text = $"{label}: {stat}";

        Log($"[{slot.Config.Name}] {label} #{run} {(outcome.Success ? "성공" : "실패")} ({sw.ElapsedMilliseconds}ms) — {outcome.Summary}");
        foreach (var st in outcome.Steps)
        {
            Log($"    {st.Path} → HTTP {st.Status} · {st.Ms}ms{(st.Blocked ? " · 차단" : "")}{(st.Error != null ? " · " + st.Error : "")}");
            foreach (var h in st.KeyHits.Take(8)) Log($"        {h}");
        }

        var line = JsonSerializer.Serialize(new
        {
            ts = DateTimeOffset.Now,
            service = slot.Config.Id,
            method = fetcher.Kind,
            run,
            success = outcome.Success,
            elapsedMs = sw.ElapsedMilliseconds,
            summary = outcome.Summary,
            steps = outcome.Steps,
        }, JsonOut);
        await File.AppendAllTextAsync(Paths.ResultsFile, line + Environment.NewLine);
    }

    private void ShowBrowser_Changed(object sender, RoutedEventArgs e)
    {
        foreach (var s in _slots) s.Browser.SetVisible(ShowBrowser.IsChecked == true);
    }

    private void OpenData_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", Paths.Data) { UseShellExecute = true });

    private void Log(string message)
    {
        LogBox.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current.Shutdown();
    }
}
