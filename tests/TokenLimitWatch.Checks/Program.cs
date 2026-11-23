using TokenLimitWatch;
using TokenLimitWatch.Poc;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
var now = DateTimeOffset.Parse("2026-11-23T10:00:00+09:00");
var limits = UsageProvider.ParseClaude("""{"five_hour":{"utilization":0,"resets_at":null},"seven_day":{"utilization":25,"resets_at":"2026-11-25T08:00:00Z"}}""");
Check(limits.Count == 2, "session and weekly parsed");
Check(limits[0].Used == 0 && limits[0].Remaining == 100, "zero usage is valid");
Check(limits[0].Countdown(now) == "확인 불가", "missing reset is unknown");
Check(limits[1].Remaining == 75, "remaining calculated");
Check(limits[1].Countdown(now) == "2일 7시간 0분", "UTC reset countdown");
Check(new Limit("x",null,null).Remaining == null, "unknown usage has no remaining");
Check(new Limit("x",120,now).Remaining == 0, "over-limit remaining clamped");
Check(new Limit("x",0,now).Countdown(now) == "초기화 확인 중", "expired reset not negative");
Check(UsageProvider.ParseClaude("""{"five_hour":{"utilization":null,"resets_at":null}}""")[0].Used == null, "null usage accepted");
Check(PollPolicy.Next(now,false,30) == now.AddMinutes(5), "retry fixed five minutes");
Check(PollPolicy.Next(now,true,30) == now.AddMinutes(30), "success restores configured interval");
var chat = new ServiceConfig { Id = "chatgpt" };
var authenticated = await UsageProvider.ReadAsync(chat,new Fake(new FetchResult { Status = 200, Body = """{"accessToken":"test-only"}""" }),null);
Check(authenticated.Success && authenticated.Limits.Single().Used == null, "chat auth never fabricates usage");
var unauthenticated = await UsageProvider.ReadAsync(chat,new Fake(new FetchResult { Status = 200, Body = "{}" }),null);
Check(!unauthenticated.Success && unauthenticated.LoginRequired, "empty session needs login");
var claude = new ServiceConfig { Id = "claude" };
var selected = await UsageProvider.ReadAsync(claude,new Fake(new() { Status = 200, Body = """[{"uuid":"one","name":"First"},{"uuid":"two","name":"Second"}]""" },new() { Status = 200, Body = """{"five_hour":{"utilization":12,"resets_at":null}}""" }),"two");
Check(selected.SelectedOrg == "two" && selected.Limits[0].Remaining == 88, "selected organization used");
Check(FetchResult.LooksBlocked(403,"<title>Just a moment</title>",null), "challenge detected");
var plan = ChatGptPlanUsage.Parse("""[{"name":"5시간 단위 한도","remaining":77,"resetTitle":"2026년 11월 23일 월요일 오후 12시 29분 38초 GMT+9"},{"name":"주간 사용 한도","remaining":96,"resetTitle":"2026년 11월 26일 목요일 오후 9시 3분 1초 GMT+9"}]""");
Check(plan[0].Used == 23 && plan[0].Remaining == 77, "shared five-hour percentage");
Check(plan[1].Used == 4 && plan[1].Remaining == 96, "shared weekly percentage");
Check(plan[0].Reset == DateTimeOffset.Parse("2026-11-23T12:29:38+09:00"), "exact Korean title reset, noon");
Check(ChatGptPlanUsage.ParseReset("2026년 11월 23일 월요일 오전 12시 0분 0초 GMT+9")?.Hour == 0, "Korean midnight");
Check(ChatGptPlanUsage.ParseReset("2026년 13월 23일 월요일 오후 12시 0분 0초 GMT+9") == null, "invalid date unknown");
Check(ChatGptPlanUsage.ParseReset("2시간 1분 후 초기화됩니다") == null, "relative text never invents reset");
Check(ChatGptPlanUsage.Parse("[]").All(l => l.Used == null), "missing plan rows unknown");
Check(ChatGptPlanUsage.Parse("""[{"name":"5시간 단위 한도","remaining":-1}]""")[0].Used == null, "invalid percentage unknown");
var displayLimits = new List<Limit> { new("5시간 세션",25,null),new("주간 한도",10,null),new("5시간 단위 한도",20,null),new("주간 사용 한도",5,null),new("Opus 주간",1,null) };
Check(DisplayPolicy.VisibleLimits(displayLimits,true).Select(l=>l.Name).SequenceEqual(new[]{"5시간 세션","5시간 단위 한도"}),"mini contains five-hour only for both services");
Check(DisplayPolicy.VisibleLimits(displayLimits,false).Count==5,"dashboard retains all weekly information");
Check(displayLimits.Count==5,"mini filter preserves source usage and polling");
Loc.Lang = "en";
Check(limits[1].Countdown(now) == "2d 7h 0m", "English countdown");
Check(new Limit("x",null,null).UsedText == "Unknown" && limits[0].Countdown(now) == "Unknown", "English unknown");
Check(new Limit("x",0,now).Countdown(now) == "Checking reset", "English expired reset");
Check(Loc.T("5시간 세션") == "5-hour session" && Loc.F("{0}개 선택됨",2) == "2 selected", "English labels and formats");
Check(Loc.T("번역 없는 문장") == "번역 없는 문장", "missing translation falls back to Korean");
Loc.Lang = "ko";
Check(limits[1].Countdown(now) == "2일 7시간 0분" && Loc.Normalize("fr") is "ko" or "en", "Korean restored, unknown language normalized");
Console.WriteLine($"{passed} checks passed.");

sealed class Fake(params FetchResult[] results) : IFetcher
{
    private int index;
    public string Kind => "fake";
    public Task<FetchResult> GetAsync(string path, IReadOnlyDictionary<string,string>? headers = null) => Task.FromResult(results[index++]);
}
