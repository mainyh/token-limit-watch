using System.IO;
using System.Text.Json;
using TokenLimitWatch.Poc;
namespace TokenLimitWatch;

public sealed class Settings
{
    public List<string> Services { get; set; } = [];
    public int Interval { get; set; } = 10;
    public bool AlwaysOnTop { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool Warnings { get; set; } = true;
    public bool ResetNotifications { get; set; } = true;
    public double Threshold { get; set; } = 80;
    public string? ClaudeOrg { get; set; }
    public string? Language { get; set; }
    private static string FilePath => Path.Combine(Paths.Data, "settings.json");
    public static Settings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
            if (!new[] { 3, 5, 10, 30 }.Contains(s.Interval)) s.Interval = 10;
            s.Services = (s.Services ?? []).Where(x => x is "claude" or "chatgpt").Distinct().ToList();
            s.Threshold = Math.Clamp(s.Threshold, 1, 100);
            return s;
        }
        catch { return new(); }
    }
    public void Save()
    {
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
}
