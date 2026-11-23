using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TokenLimitWatch.Poc;

namespace TokenLimitWatch;

public static partial class ChatGptPlanUsage
{
    // Read only the two displayed plan-limit cards. No reset/credit actions are invoked.
    public const string ReadCardsScript = """
        (() => {
          const rows = [];
          for (const progress of document.querySelectorAll('progress')) {
            if (!progress.hasAttribute('value')) continue;
            let card = progress.parentElement;
            for (let depth=0; card && depth<6; depth++, card=card.parentElement) {
              if (card.querySelectorAll('progress').length !== 1) break;
              const text = card.innerText || '';
              const five = /5시간 단위 한도|5[- ]hour (?:usage )?limit/i.test(text);
              const weekly = /주간 사용 한도|weekly (?:usage )?limit/i.test(text);
              if (!five && !weekly) continue;
              const title = [...card.querySelectorAll('[title]')].map(e => e.getAttribute('title')).find(t => /GMT|\d{4}-\d\d-\d\d/.test(t || '')) || null;
              const max = Number(progress.getAttribute('max'));
              rows.push({ name: five ? '5시간 단위 한도' : '주간 사용 한도', remaining: max > 0 ? Number(progress.getAttribute('value'))*100/max : null, resetTitle: title });
              break;
            }
          }
          return JSON.stringify(rows);
        })()
        """;

    public static List<Limit> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var found = new Dictionary<string,Limit>();
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            var name = row.GetProperty("name").GetString();
            if (name is not ("5시간 단위 한도" or "주간 사용 한도")) continue;
            double? remaining = row.TryGetProperty("remaining",out var r) && r.ValueKind == JsonValueKind.Number && r.TryGetDouble(out var n) && double.IsFinite(n) && n >= 0 && n <= 100 ? n : null;
            var title = row.TryGetProperty("resetTitle",out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            found[name] = new(name, remaining.HasValue ? 100-remaining : null, ParseReset(title));
        }
        return new[] { "5시간 단위 한도", "주간 사용 한도" }.Select(name => found.GetValueOrDefault(name) ?? new Limit(name,null,null)).ToList();
    }

    public static DateTimeOffset? ParseReset(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        // Observed title: 2026년 11월 23일 월요일 오후 12시 29분 38초 GMT+9.
        var m = Regex.Match(title,@"^(\d{4})년\s*(\d{1,2})월\s*(\d{1,2})일\s*\S+요일\s*(오전|오후)\s*(\d{1,2})시\s*(\d{1,2})분(?:\s*(\d{1,2})초)?\s*GMT([+-])(\d{1,2})(?::(\d{2}))?$");
        if (m.Success)
        {
            int Num(int index) => int.TryParse(m.Groups[index].Value,out var n) ? n : 0;
            var hour = Num(5);
            if(hour is < 1 or > 12) return null;
            hour = hour % 12 + (m.Groups[4].Value == "오후" ? 12 : 0);
            try
            {
                var offset = new TimeSpan(Num(9),Num(10),0) * (m.Groups[8].Value == "-" ? -1 : 1);
                return new DateTimeOffset(Num(1),Num(2),Num(3),hour,Num(6),Num(7),offset);
            }
            catch (ArgumentException) { return null; }
        }
        return DateTimeOffset.TryParse(title,CultureInfo.InvariantCulture,DateTimeStyles.None,out var date) ? date : null;
    }

}
