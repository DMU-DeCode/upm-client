$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$outDir = "c:\Dev\upm-client\tools\ulanzi-plugin\Images"
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

function New-RoundedRectPath {
    param(
        [float]$x,
        [float]$y,
        [float]$w,
        [float]$h,
        [float]$r
    )
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-BaseCanvas {
    param([string]$bgColorHex = "#111827")
    $bmp = New-Object System.Drawing.Bitmap(144, 144, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $bgPath = New-RoundedRectPath 0 0 144 144 28
    $bgBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($bgColorHex))
    $g.FillPath($bgBrush, $bgPath)
    $bgBrush.Dispose()
    $bgPath.Dispose()

    return @{ Bitmap = $bmp; Graphics = $g }
}

function Draw-CenteredText {
    param(
        [System.Drawing.Graphics]$g,
        [string]$text,
        [float]$fontSize,
        [string]$colorHex,
        [float]$centerY,
        [System.Drawing.FontStyle]$style = [System.Drawing.FontStyle]::Bold
    )
    $font = New-Object System.Drawing.Font("Segoe UI", $fontSize, $style, [System.Drawing.GraphicsUnit]::Pixel)
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($colorHex))
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF(0, ($centerY - 30), 144, 60)
    $g.DrawString($text, $font, $brush, $rect, $sf)
    $sf.Dispose()
    $brush.Dispose()
    $font.Dispose()
}

# 1. app.png (UPM Logo from upm_icon.ico / upm_icon.png)
$c = New-BaseCanvas "#18181B"
$g = $c.Graphics
$bmp = $c.Bitmap
$borderPath = New-RoundedRectPath 6 6 132 132 24
$borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(90, 59, 130, 246), 3)
$g.DrawPath($borderPen, $borderPath)
$borderPen.Dispose()
$borderPath.Dispose()

# Extract largest PNG frame from upm_icon.ico (or load upm_icon.png)
$icoPath = "c:\Dev\upm-client\Apps\UPM.Desktop\Assets\upm_icon.ico"
$pngFallback = "c:\Dev\upm-client\Apps\UPM.Desktop\Assets\upm_icon.png"
$logoImg = $null
if (Test-Path $pngFallback) {
    $logoImg = [System.Drawing.Image]::FromFile($pngFallback)
} elseif (Test-Path $icoPath) {
    $icoBytes = [System.IO.File]::ReadAllBytes($icoPath)
    $count = [BitConverter]::ToUInt16($icoBytes, 4)
    # Last directory entry has the largest frame (256x256)
    $entryOffset = 6 + (($count - 1) * 16)
    $dataSize = [BitConverter]::ToInt32($icoBytes, $entryOffset + 8)
    $dataOffset = [BitConverter]::ToInt32($icoBytes, $entryOffset + 12)
    $ms = New-Object System.IO.MemoryStream($icoBytes, $dataOffset, $dataSize)
    $logoImg = [System.Drawing.Image]::FromStream($ms)
}
if ($logoImg -ne $null) {
    $g.DrawImage($logoImg, 12, 12, 120, 120)
    $logoImg.Dispose()
}
$bmp.Save("$outDir\app.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

# 2. boost.png
$c = New-BaseCanvas "#2563EB"
$g = $c.Graphics
$bmp = $c.Bitmap
$pts = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF(76, 20)),
    (New-Object System.Drawing.PointF(46, 66)),
    (New-Object System.Drawing.PointF(68, 66)),
    (New-Object System.Drawing.PointF(60, 102)),
    (New-Object System.Drawing.PointF(96, 54)),
    (New-Object System.Drawing.PointF(72, 54))
)
$boltBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml("#FDE047"))
$g.FillPolygon($boltBrush, $pts)
$boltBrush.Dispose()
Draw-CenteredText $g "BOOST" 20 "#FFFFFF" 120
$bmp.Save("$outDir\boost.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

# 3. cpuicon.png
$c = New-BaseCanvas "#111827"
$g = $c.Graphics
$bmp = $c.Bitmap
$pen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml("#3B82F6"), 4)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
# Pins
foreach ($px in @(54, 72, 90)) {
    $g.DrawLine($pen, $px, 20, $px, 32)
    $g.DrawLine($pen, $px, 88, $px, 100)
}
foreach ($py in @(42, 60, 78)) {
    $g.DrawLine($pen, 32, $py, 44, $py)
    $g.DrawLine($pen, 100, $py, 112, $py)
}
$chipPath = New-RoundedRectPath 44 32 56 56 10
$chipFill = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml("#1E3A8A"))
$g.FillPath($chipFill, $chipPath)
$g.DrawPath($pen, $chipPath)
$coreFill = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml("#60A5FA"))
$g.FillRectangle($coreFill, 58, 46, 28, 28)
$coreFill.Dispose()
$chipFill.Dispose()
$chipPath.Dispose()
$pen.Dispose()
Draw-CenteredText $g "CPU" 22 "#93C5FD" 122
$bmp.Save("$outDir\cpuicon.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

# 4. ramicon.png
$c = New-BaseCanvas "#111827"
$g = $c.Graphics
$bmp = $c.Bitmap
$ramPath = New-RoundedRectPath 24 36 96 46 8
$ramFill = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml("#064E3B"))
$ramPen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml("#10B981"), 4)
$g.FillPath($ramFill, $ramPath)
$g.DrawPath($ramPen, $ramPath)
$chipBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml("#34D399"))
foreach ($rx in @(34, 54, 74, 94)) {
    $g.FillRectangle($chipBrush, $rx, 46, 12, 22)
}
$goldPen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml("#FBBF24"), 4)
$g.DrawLine($goldPen, 34, 86, 110, 86)
$goldPen.Dispose()
$chipBrush.Dispose()
$ramPen.Dispose()
$ramFill.Dispose()
$ramPath.Dispose()
Draw-CenteredText $g "RAM" 22 "#6EE7B7" 122
$bmp.Save("$outDir\ramicon.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

# 5. gpuicon.png
$c = New-BaseCanvas "#111827"
$g = $c.Graphics
$bmp = $c.Bitmap
$gpuPath = New-RoundedRectPath 24 34 96 52 10
$gpuFill = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml("#3B0764"))
$gpuPen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml("#A855F7"), 4)
$g.FillPath($gpuFill, $gpuPath)
$g.DrawPath($gpuPen, $gpuPath)
$fanPen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml("#D8B4FE"), 3)
$g.DrawEllipse($fanPen, 34, 44, 32, 32)
$g.DrawEllipse($fanPen, 78, 44, 32, 32)
$g.DrawLine($fanPen, 50, 48, 50, 72)
$g.DrawLine($fanPen, 38, 60, 62, 60)
$g.DrawLine($fanPen, 94, 48, 94, 72)
$g.DrawLine($fanPen, 82, 60, 106, 60)
$fanPen.Dispose()
$gpuPen.Dispose()
$gpuFill.Dispose()
$gpuPath.Dispose()
Draw-CenteredText $g "GPU" 22 "#D8B4FE" 122
$bmp.Save("$outDir\gpuicon.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

# 6. tempicon.png
$c = New-BaseCanvas "#111827"
$g = $c.Graphics
$bmp = $c.Bitmap
$stemPen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml("#9CA3AF"), 14)
$stemPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$stemPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($stemPen, 72, 26, 72, 76)
$stemPen.Dispose()
$innerPen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml("#EF4444"), 8)
$innerPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($innerPen, 72, 42, 72, 78)
$innerPen.Dispose()
$bulbBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml("#EF4444"))
$g.FillEllipse($bulbBrush, 54, 64, 36, 36)
$bulbBrush.Dispose()
$dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml("#FCA5A5"))
$g.FillEllipse($dotBrush, 66, 76, 12, 12)
$dotBrush.Dispose()
Draw-CenteredText $g "TEMP" 22 "#FCA5A5" 122
$bmp.Save("$outDir\tempicon.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

# 7..10. Value initial icons (cpuval.png, ramval.png, gpuval.png, tempval.png)
function Save-ValuePlaceholder {
    param([string]$fileName, [string]$label, [string]$unit, [string]$accentHex)
    $c = New-BaseCanvas "#111827"
    $g = $c.Graphics
    $bmp = $c.Bitmap
    $bPath = New-RoundedRectPath 8 8 128 128 22
    $col = [System.Drawing.ColorTranslator]::FromHtml($accentHex)
    $bPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(110, $col.R, $col.G, $col.B), 4)
    $g.DrawPath($bPen, $bPath)
    $bPen.Dispose()
    $bPath.Dispose()

    Draw-CenteredText $g $label 18 "#9CA3AF" 32
    Draw-CenteredText $g "--" 38 $accentHex 74
    Draw-CenteredText $g $unit 18 "#9CA3AF" 114

    $bmp.Save("$outDir\$fileName", [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
}

Save-ValuePlaceholder "cpuval.png" "CPU" "%" "#3B82F6"
Save-ValuePlaceholder "ramval.png" "RAM" "%" "#10B981"
Save-ValuePlaceholder "gpuval.png" "GPU" "%" "#A855F7"
Save-ValuePlaceholder "tempval.png" "GPU TEMP" "°C" "#EF4444"

Write-Output "Generated all 10 PNG icons (144x144) successfully in $outDir"
