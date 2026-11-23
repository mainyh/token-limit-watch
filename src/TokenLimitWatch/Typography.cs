using System.Windows.Media;

namespace TokenLimitWatch;

public static class Typography
{
    // Pretendard covers Hangul and Latin (Inter-based) in one family, so mixed text shares one baseline and weight scale.
    // Relative font names with one base URI preserve the commas inside the pack URI.
    public static FontFamily Ui { get; } = new(new Uri("pack://application:,,,/"),"./Assets/Fonts/#Pretendard");
}
