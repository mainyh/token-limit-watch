using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TokenLimitWatch;

public static class DesignVerification
{
    public static bool WriteReport(Application app,string destination)
    {
        var checks = new List<object>();
        var passed = true;
        foreach(var (key,family,character) in new[]{("BodyFont","Pretendard",'가'),("BodyFont","Pretendard",'0'),("BodyFont","Pretendard",'A')})
        {
            var font=(FontFamily)app.FindResource(key);
            foreach(var weight in new[]{FontWeights.Normal,FontWeights.Medium,FontWeights.SemiBold,FontWeights.Bold})
            {
                var face=new Typeface(font,FontStyles.Normal,weight,FontStretches.Normal);
                var loaded=face.TryGetGlyphTypeface(out var glyph);
                var names=loaded?string.Join(",",glyph.FamilyNames.Values.Distinct()):"unresolved";
                var ok=loaded&&names.Contains(family,StringComparison.OrdinalIgnoreCase)&&glyph.CharacterToGlyphMap.ContainsKey(character);
                passed &= ok;
                checks.Add(new {key,source=font.Source,weight=weight.ToString(),actualFamily=names,actualWeight=loaded?glyph.Weight.ToString():null,actualFile=loaded?glyph.FontUri.ToString():null,ok});
            }
        }
        foreach(var (sample,expected) in new[]{("한글주간","Pretendard"),("Claude 25% 2026-11-23","Pretendard")})
        {
            var visual=new DrawingVisual();
            using(var context=visual.RenderOpen()) context.DrawText(new FormattedText(sample,CultureInfo.GetCultureInfo("ko-KR"),FlowDirection.LeftToRight,new Typeface(Typography.Ui,FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),14,Brushes.Black,1),new Point());
            var families=new List<string>();
            void Visit(Drawing drawing)
            {
                if(drawing is DrawingGroup group) foreach(var child in group.Children) Visit(child);
                if(drawing is GlyphRunDrawing run) families.AddRange(run.GlyphRun.GlyphTypeface.FamilyNames.Values);
            }
            Visit(visual.Drawing);
            var ok=families.Count>0&&families.All(f=>f.Contains(expected,StringComparison.OrdinalIgnoreCase));
            passed &= ok;
            checks.Add(new {name="actual rendered font",sample,expected,actual=families.Distinct().ToArray(),ok});
        }
        var icon=new BitmapImage(new Uri("pack://application:,,,/Assets/TokenLimitWatch.png"));
        var bgra=new FormatConvertedBitmap(icon,PixelFormats.Bgra32,null,0);
        var pixels=new byte[bgra.PixelWidth*bgra.PixelHeight*4];bgra.CopyPixels(pixels,bgra.PixelWidth*4,0);
        int Alpha(int x,int y)=>pixels[(y*bgra.PixelWidth+x)*4+3];
        var rounded=Alpha(0,0)==0&&Alpha(16,16)==0&&Alpha(128,128)==255;
        passed &= rounded;
        checks.Add(new {name="icon rounded alpha",corner=Alpha(0,0),nearCorner=Alpha(16,16),center=Alpha(128,128),ok=rounded});
        File.WriteAllText(destination,JsonSerializer.Serialize(new {passed,checks},new JsonSerializerOptions{WriteIndented=true}));
        return passed;
    }
}
