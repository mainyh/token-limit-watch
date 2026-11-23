Add-Type -AssemblyName System.Drawing
$iconRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/TokenLimitWatch/Assets'))
[IO.Directory]::CreateDirectory($iconRoot) | Out-Null
$frames = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bmp = [Drawing.Bitmap]::new($size,$size)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($size/256.0,$size/256.0)
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc(8,8,128,128,180,90); $path.AddArc(120,8,128,128,270,90)
    $path.AddArc(120,120,128,128,0,90); $path.AddArc(8,120,128,128,90,90); $path.CloseFigure()
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#5B5BD6'))
    $g.FillPath($brush,$path)
    $pen = [Drawing.Pen]::new([Drawing.Color]::White,13)
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $g.DrawLines($pen,[Drawing.PointF[]]@([Drawing.PointF]::new(47,133),[Drawing.PointF]::new(83,133),[Drawing.PointF]::new(107,77),[Drawing.PointF]::new(140,183),[Drawing.PointF]::new(166,120),[Drawing.PointF]::new(210,120)))
    $stream = [IO.MemoryStream]::new(); $bmp.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@{ Size=$size; Bytes=$stream.ToArray() }
    if ($size -eq 256) { $bmp.Save((Join-Path $iconRoot 'TokenLimitWatch.png'),[Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $g.Dispose(); $pen.Dispose(); $brush.Dispose(); $path.Dispose(); $bmp.Dispose()
}
$writer = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $iconRoot 'TokenLimitWatch.ico')))
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16*$frames.Count
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
    $offset += $frame.Bytes.Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
$writer.Dispose()
