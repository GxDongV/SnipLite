$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$logoDir = Join-Path $projectRoot 'logo'
[xml]$svg = Get-Content -LiteralPath (Join-Path $logoDir 'SnipLite.svg') -Raw
$culture = [Globalization.CultureInfo]::InvariantCulture
$viewBox = @($svg.DocumentElement.GetAttribute('viewBox').Split(' ') | ForEach-Object { [float]::Parse($_, $culture) })

# Render the SVG's round polylines at 4x resolution, then downsample for small Windows icons.
function New-LogoBitmap([int]$size) {
    $large = [Drawing.Bitmap]::new($size * 4, $size * 4)
    $graphics = [Drawing.Graphics]::FromImage($large)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($large.Width / $viewBox[2], $large.Height / $viewBox[3])
        $graphics.TranslateTransform(-$viewBox[0], -$viewBox[1])
        foreach ($element in $svg.DocumentElement.ChildNodes) {
            if ($element.LocalName -in @('title', 'desc')) { continue }
            if ($element.LocalName -ne 'polyline') { throw "Unsupported logo element: $($element.LocalName)" }
            [Drawing.PointF[]]$points = @($element.GetAttribute('points').Split(' ', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object {
                $xy = $_.Split(',')
                [Drawing.PointF]::new([float]::Parse($xy[0], $culture), [float]::Parse($xy[1], $culture))
            })
            $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($element.GetAttribute('stroke')), [float]::Parse($element.GetAttribute('stroke-width'), $culture))
            try {
                $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
                $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
                $graphics.DrawLines($pen, $points)
            } finally { $pen.Dispose() }
        }
        $bitmap = [Drawing.Bitmap]::new($size, $size)
        $scaled = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $scaled.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $scaled.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $scaled.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scaled.DrawImage($large, [Drawing.Rectangle]::new(0, 0, $size, $size))
        } finally { $scaled.Dispose() }
        return ,$bitmap
    } finally { $graphics.Dispose(); $large.Dispose() }
}

$png = New-LogoBitmap 512
try { $png.Save((Join-Path $logoDir 'SnipLite.png'), [Drawing.Imaging.ImageFormat]::Png) }
finally { $png.Dispose() }

# ICO directory with PNG-compressed RGBA frames; Windows chooses the matching DPI size.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @($sizes | ForEach-Object {
    $bitmap = New-LogoBitmap $_
    $stream = [IO.MemoryStream]::new()
    try {
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        ,$stream.ToArray()
    } finally { $stream.Dispose(); $bitmap.Dispose() }
})
$file = [IO.File]::Create((Join-Path $logoDir 'SnipLite.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $file.Dispose() }
Write-Host "Generated logo/SnipLite.png (512px) and logo/SnipLite.ico ($($sizes -join ', ')px) from logo/SnipLite.svg."
