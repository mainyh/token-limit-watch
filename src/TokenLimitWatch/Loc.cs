using System.Globalization;

namespace TokenLimitWatch;

// UI language. Korean source strings double as keys; English comes from the table below.
// Internal state (limit names, slot messages) stays Korean and is translated only when shown.
public static class Loc
{
    public static string Lang { get; set; } = "ko";
    public static bool English => Lang == "en";

    public static string Default() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko" ? "ko" : "en";
    public static string Normalize(string? lang) => lang is "ko" or "en" ? lang : Default();

    public static string T(string ko) => English && En.TryGetValue(ko, out var en) ? en : ko;
    public static string F(string ko, params object?[] args) => string.Format(CultureInfo.InvariantCulture, T(ko), args);

    public static string Countdown(TimeSpan left)
    {
        if (left.TotalDays >= 1)
            return English ? $"{(int)left.TotalDays}d {left.Hours}h {left.Minutes}m" : $"{(int)left.TotalDays}일 {left.Hours}시간 {left.Minutes}분";
        if (left.TotalHours >= 1)
            return English ? $"{(int)left.TotalHours}h {left.Minutes}m {left.Seconds}s" : $"{(int)left.TotalHours}시간 {left.Minutes}분 {left.Seconds}초";
        return English ? $"{left.Minutes}m {left.Seconds}s" : $"{left.Minutes}분 {left.Seconds}초";
    }

    private static readonly Dictionary<string, string> En = new()
    {
        // Limits and states
        ["5시간 세션"] = "5-hour session",
        ["주간 한도"] = "Weekly limit",
        ["5시간 단위 한도"] = "5-hour limit",
        ["주간 사용 한도"] = "Weekly limit",
        ["Opus 주간"] = "Opus weekly",
        ["Sonnet 주간"] = "Sonnet weekly",
        ["사용 한도"] = "Usage limit",
        ["확인 불가"] = "Unknown",
        ["초기화 확인 중"] = "Checking reset",
        ["여유"] = "OK",
        ["주의"] = "Caution",
        ["임박"] = "Near limit",
        ["연결됨"] = "Connected",
        ["미연결"] = "Not connected",
        ["확인 중"] = "Checking",
        ["추후 지원"] = "Coming soon",

        // Slot messages
        ["로그인하여 조회를 시작하세요"] = "Sign in to start monitoring",
        ["로그인이 필요합니다"] = "Sign-in required",
        ["다시 로그인하세요"] = "Please sign in again",
        ["조회 실패 · 5분 후 재시도"] = "Update failed · retrying in 5 min",
        ["작업을 완료하지 못했습니다."] = "Could not complete the action.",

        // Tray and notifications
        ["대시보드 열기"] = "Open dashboard",
        ["지금 갱신"] = "Refresh now",
        ["종료"] = "Quit",
        ["{0} · {1} 초기화됨"] = "{0} · {1} has reset",
        ["사용량 {0} · 남은량 {1}\n초기화 {2}\n남은 시간 {3}"] = "Used {0} · Remaining {1}\nResets {2}\nTime left {3}",

        // Window chrome
        ["고정"] = "Pin",
        ["항상 위에 고정"] = "Keep on top",
        ["최소화"] = "Minimize",
        ["닫기"] = "Close",
        ["트레이로 숨기기"] = "Hide to tray",
        ["뒤로"] = "Back",
        ["설정"] = "Settings",

        // 01 Services
        ["모니터링할 AI 서비스를 선택하세요"] = "Choose the AI services to monitor",
        ["여러 개를 고를 수 있어요. 선택한 순서대로 대시보드에 표시됩니다."] = "Pick one or more. They appear on the dashboard in the order you select them.",
        ["5시간 세션 · 주간 한도"] = "5-hour session · weekly limit",
        ["공유 플랜 5시간 · 주간 한도"] = "Shared plan 5-hour · weekly limit",
        ["{0} 선택"] = "Select {0}",
        ["선택됨"] = "Selected",
        ["선택 안 됨"] = "Not selected",
        ["추후 지원 예정"] = "Coming later",
        ["월간 프리미엄 요청"] = "Monthly premium requests",
        ["월간 요청 · 사용 금액"] = "Monthly requests · spend",
        ["{0}개 선택됨"] = "{0} selected",
        ["다음"] = "Next",

        // 02 Sign-in
        ["서비스에 로그인하세요"] = "Sign in to your services",
        ["이메일 주소로 로그인합니다. 로그인 정보는 이 PC에만 암호화해 저장합니다."] = "Sign in with your email address. Session data is stored encrypted on this PC only.",
        ["로그아웃"] = "Sign out",
        ["로그인됨"] = "Signed in",
        ["로그인"] = "Sign in",
        ["버튼을 누르면 앱 안에 로그인 창이 열립니다. 평소처럼 이메일로 로그인하면 창이 자동으로 닫힙니다."] = "The button opens a sign-in window inside the app. Sign in with your email as usual and it closes by itself.",
        ["Google 계정 로그인은 지원하지 않아요. 이메일 주소로 로그인해 주세요."] = "Google sign-in isn't supported. Please sign in with your email address.",
        ["연결 확인 중…"] = "Checking connection…",
        ["{0} 로그인 창 열기"] = "Open {0} sign-in",
        ["비밀번호는 저장하지 않습니다."] = "Your password is never stored.",
        ["{0} / {1} 연결됨"] = "{0} / {1} connected",
        ["{0} / {1} 연결됨 · 미연결 서비스는 나중에 설정에서 연결할 수 있어요."] = "{0} / {1} connected · You can connect the rest later in Settings.",
        ["모니터링 시작"] = "Start monitoring",

        // 03 Dashboard
        ["공유 플랜 한도 · Chat 대화 제외"] = "Shared plan limits · excludes Chat",
        ["{0} 사용량 {1}"] = "{0} usage {1}",
        ["사용량"] = "Used",
        ["남은량"] = "Remaining",
        ["초기화 시간"] = "Resets at",
        ["초기화까지 남은 시간"] = "Resets in",
        ["사용량을 한눈에"] = "Usage at a glance",
        ["AI 서비스를 연결하면 사용량과 초기화 시간을 확인할 수 있어요."] = "Connect an AI service to see its usage and reset times.",
        ["서비스 선택"] = "Choose services",
        ["계정"] = "Account",
        ["계정 · 로그인"] = "Accounts · sign-in",
        ["미니"] = "Mini",
        ["미니 위젯으로 전환"] = "Switch to mini widget",
        ["갱신 중…"] = "Refreshing…",
        ["{0} 갱신 · {1}분마다"] = "{0} · every {1} min",

        // 05 Mini widget
        ["대시보드"] = "Dashboard",
        ["{0} · 5시간"] = "{0} · 5h",
        ["{0} 5시간 사용량 {1}"] = "{0} 5-hour usage {1}",
        ["초기화"] = "Resets",
        ["남은 시간"] = "Time left",
        ["연결된 서비스가 없어요. 대시보드에서 서비스를 선택하세요."] = "No services connected. Choose services from the dashboard.",

        // 04 Settings
        ["{0}분"] = "{0} min",
        ["갱신"] = "Updates",
        ["갱신 간격"] = "Refresh interval",
        ["기본 10분"] = "Default 10 min",
        ["정보를 가져오지 못하면 5분마다 다시 시도하고, 성공하면 원래 간격으로 돌아갑니다."] = "If an update fails, it retries every 5 minutes and goes back to the normal interval once it succeeds.",
        ["알림"] = "Notifications",
        ["사용률 경고"] = "Usage warning",
        ["끄기"] = "Off",
        ["초기화되면 알림"] = "Notify on reset",
        ["창과 실행"] = "Window & startup",
        ["항상 위에 표시"] = "Always on top",
        ["닫기 버튼을 누르면 트레이로"] = "Close button hides to tray",
        ["Windows 시작 시 실행"] = "Run at Windows startup",
        ["현재 사용자(HKCU)에만 등록 · 관리자 권한 불필요"] = "Current user only (HKCU) · no admin rights needed",
        ["등록 흔적 삭제"] = "Remove registry entries",
        ["알림·자동 실행 때문에 등록된 항목을 모두 지워요"] = "Removes every entry added for notifications or autostart",
        ["삭제"] = "Remove",
        ["연결된 서비스"] = "Connected services",
        ["선택한 서비스가 없어요."] = "No services selected.",
        ["서비스 추가"] = "Add service",
        ["앱 종료"] = "Quit app",
        ["언어"] = "Language",
        ["표시 언어"] = "Display language",

        // Sign-in window (shared PoC code)
        ["{0} 로그인 — 이메일로 로그인하세요 (Google 계정 로그인 미지원)"] = "{0} sign-in — use your email (Google sign-in not supported)",
        ["WebView2를 시작하지 못했습니다."] = "Could not start WebView2.",
        ["Google 계정 로그인은 지원하지 않습니다.\n이메일 주소로 로그인해 주세요."] = "Google sign-in isn't supported.\nPlease sign in with your email address.",
    };
}
