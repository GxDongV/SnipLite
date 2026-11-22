# Source for the plugin-owned transparent PNG. No host resources are involved.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bitmap = [Drawing.Bitmap]::new(96, 96)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$pen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(45, 125, 210), 1.7)
$path = [Drawing.Drawing2D.GraphicsPath]::new()
try {
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(4, 4)
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    # A tall captured page with content, and a downward scrolling arrow.
    $path.AddArc(3, 2, 4, 4, 180, 90)
    $path.AddArc(11, 2, 4, 4, 270, 90)
    $path.AddArc(11, 18, 4, 4, 0, 90)
    $path.AddArc(3, 18, 4, 4, 90, 90)
    $path.CloseFigure()
    $graphics.DrawPath($pen, $path)
    $graphics.DrawLine($pen, 6, 7, 12, 7)
    $graphics.DrawLine($pen, 6, 11, 12, 11)
    $graphics.DrawLine($pen, 6, 15, 10, 15)
    $graphics.DrawLine($pen, 20, 4, 20, 20)
    $graphics.DrawLines($pen, [Drawing.PointF[]]@([Drawing.PointF]::new(17, 17), [Drawing.PointF]::new(20, 20), [Drawing.PointF]::new(23, 17)))
    $bitmap.Save((Join-Path $PSScriptRoot 'scroll-capture.png'), [Drawing.Imaging.ImageFormat]::Png)
} finally {
    $path.Dispose()
    $pen.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}
