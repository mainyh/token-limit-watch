using System.Text.Json;
using TokenLimitWatch.Poc;
namespace TokenLimitWatch;
public static partial class ChatGptPlanUsage
{
    public static async Task<UsageResult> ReadAsync(HiddenBrowser browser)
    {
        await browser.NavigateUsagePageAsync();
        for(var attempt=0; attempt<20; attempt++)
        {
            var encoded = await browser.Core.ExecuteScriptAsync(ReadCardsScript);
            var json = JsonSerializer.Deserialize<string>(encoded) ?? "[]";
            var limits = Parse(json);
            if(limits.All(l => l.Used.HasValue)) return new(true,false,limits,[]);
            if(attempt == 19) return new(false,!await browser.HasSessionCookieAsync(),limits,[]);
            await Task.Delay(500);
        }
        throw new InvalidOperationException();
    }
}
