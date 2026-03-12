Add-Type -AssemblyName System.Drawing

function New-AppIcon {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'

    $scale = $Size / 256.0

    # Colors
    $bgColor = [System.Drawing.Color]::FromArgb(27, 40, 56)        # #1B2838
    $cardColor = [System.Drawing.Color]::FromArgb(30, 42, 54)      # #1E2A36
    $accentColor = [System.Drawing.Color]::FromArgb(102, 192, 244) # #66C0F4
    $textColor = [System.Drawing.Color]::FromArgb(229, 229, 229)   # #E5E5E5
    $borderColor = [System.Drawing.Color]::FromArgb(42, 58, 74)    # #2A3A4A

    $bgBrush = New-Object System.Drawing.SolidBrush $bgColor
    $cardBrush = New-Object System.Drawing.SolidBrush $cardColor
    $accentBrush = New-Object System.Drawing.SolidBrush $accentColor
    $textBrush = New-Object System.Drawing.SolidBrush $textColor
    $borderPen = New-Object System.Drawing.Pen $borderColor, ([math]::Max(1, 2 * $scale))
    $accentPen = New-Object System.Drawing.Pen $accentColor, ([math]::Max(1, 3 * $scale))

    # Background rounded rectangle
    $radius = [int](32 * $scale)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $rect = New-Object System.Drawing.Rectangle 0, 0, ($Size - 1), ($Size - 1)
    $path.AddArc($rect.X, $rect.Y, $radius * 2, $radius * 2, 180, 90)
    $path.AddArc($rect.Right - $radius * 2, $rect.Y, $radius * 2, $radius * 2, 270, 90)
    $path.AddArc($rect.Right - $radius * 2, $rect.Bottom - $radius * 2, $radius * 2, $radius * 2, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $radius * 2, $radius * 2, $radius * 2, 90, 90)
    $path.CloseFigure()
    $g.FillPath($bgBrush, $path)

    # Terminal window body
    $termX = [int](28 * $scale)
    $termY = [int](36 * $scale)
    $termW = [int](200 * $scale)
    $termH = [int](150 * $scale)
    $termRadius = [int](12 * $scale)
    $termPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $termRect = New-Object System.Drawing.Rectangle $termX, $termY, $termW, $termH
    $termPath.AddArc($termRect.X, $termRect.Y, $termRadius * 2, $termRadius * 2, 180, 90)
    $termPath.AddArc($termRect.Right - $termRadius * 2, $termRect.Y, $termRadius * 2, $termRadius * 2, 270, 90)
    $termPath.AddArc($termRect.Right - $termRadius * 2, $termRect.Bottom - $termRadius * 2, $termRadius * 2, $termRadius * 2, 0, 90)
    $termPath.AddArc($termRect.X, $termRect.Bottom - $termRadius * 2, $termRadius * 2, $termRadius * 2, 90, 90)
    $termPath.CloseFigure()
    $g.FillPath($cardBrush, $termPath)
    $g.DrawPath($borderPen, $termPath)

    # Title bar
    $titleH = [int](24 * $scale)
    $titlePath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $titlePath.AddArc($termX, $termY, $termRadius * 2, $termRadius * 2, 180, 90)
    $titlePath.AddArc($termX + $termW - $termRadius * 2, $termY, $termRadius * 2, $termRadius * 2, 270, 90)
    $titlePath.AddLine(($termX + $termW), ($termY + $titleH), $termX, ($termY + $titleH))
    $titlePath.CloseFigure()
    $titleBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(42, 71, 94))
    $g.FillPath($titleBrush, $titlePath)

    # Title bar dots
    $dotSize = [int](6 * $scale)
    $dotY = [int]($termY + ($titleH - $dotSize) / 2)
    $dotBrush1 = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(224, 108, 117))
    $dotBrush2 = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(229, 192, 123))
    $dotBrush3 = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(152, 195, 121))
    $g.FillEllipse($dotBrush1, [int]($termX + 10 * $scale), $dotY, $dotSize, $dotSize)
    $g.FillEllipse($dotBrush2, [int]($termX + 22 * $scale), $dotY, $dotSize, $dotSize)
    $g.FillEllipse($dotBrush3, [int]($termX + 34 * $scale), $dotY, $dotSize, $dotSize)

    # Terminal prompt lines
    $lineY = [int]($termY + $titleH + 16 * $scale)
    $lineX = [int]($termX + 16 * $scale)

    # "> ssh user@server" style text
    $promptFont = New-Object System.Drawing.Font("Consolas", [float][math]::Max(6, 14 * $scale), [System.Drawing.FontStyle]::Bold)
    $g.DrawString(">_", $promptFont, $accentBrush, $lineX, $lineY)

    # Second line - dimmer
    $dimBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(143, 152, 160))
    $lineY2 = [int]($lineY + 24 * $scale)
    $smallFont = New-Object System.Drawing.Font("Consolas", [float][math]::Max(5, 10 * $scale))
    $g.DrawString("sessions loaded", $smallFont, $dimBrush, [int]($lineX + 2 * $scale), $lineY2)

    # Third line
    $lineY3 = [int]($lineY2 + 18 * $scale)
    $g.DrawString("backup complete", $smallFont, [System.Drawing.Brushes]::LightGreen, [int]($lineX + 2 * $scale), $lineY3)

    # Key/lock icon in bottom-right corner
    $keyX = [int](160 * $scale)
    $keyY = [int](195 * $scale)
    $keySize = [int](50 * $scale)

    # Shield shape
    $shieldPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shieldCX = $keyX + $keySize / 2
    $shieldTop = $keyY
    $shieldW = $keySize
    $shieldH = [int]($keySize * 1.15)
    $shieldPath.AddLine(($shieldCX - $shieldW/2), ($shieldTop + $shieldH * 0.15), $shieldCX, $shieldTop)
    $shieldPath.AddLine($shieldCX, $shieldTop, ($shieldCX + $shieldW/2), ($shieldTop + $shieldH * 0.15))
    $shieldPath.AddLine(($shieldCX + $shieldW/2), ($shieldTop + $shieldH * 0.15), ($shieldCX + $shieldW/2), ($shieldTop + $shieldH * 0.55))
    $shieldPath.AddLine(($shieldCX + $shieldW/2), ($shieldTop + $shieldH * 0.55), $shieldCX, ($shieldTop + $shieldH))
    $shieldPath.AddLine($shieldCX, ($shieldTop + $shieldH), ($shieldCX - $shieldW/2), ($shieldTop + $shieldH * 0.55))
    $shieldPath.CloseFigure()

    $shieldBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(180, 102, 192, 244))
    $g.FillPath($shieldBrush, $shieldPath)
    $shieldPen = New-Object System.Drawing.Pen $accentColor, ([math]::Max(1, 2 * $scale))
    $g.DrawPath($shieldPen, $shieldPath)

    # Checkmark inside shield
    $checkPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([math]::Max(1, 3 * $scale))
    $checkPen.StartCap = 'Round'
    $checkPen.EndCap = 'Round'
    $checkPen.LineJoin = 'Round'
    $cx = $shieldCX
    $cy = $shieldTop + $shieldH * 0.48
    $cs = $keySize * 0.15
    $g.DrawLine($checkPen, [int]($cx - $cs), [int]$cy, [int]($cx - $cs * 0.3), [int]($cy + $cs * 0.7))
    $g.DrawLine($checkPen, [int]($cx - $cs * 0.3), [int]($cy + $cs * 0.7), [int]($cx + $cs), [int]($cy - $cs * 0.5))

    $g.Dispose()
    return $bmp
}

function Save-Ico {
    param([System.Drawing.Bitmap[]]$Bitmaps, [string]$Path)

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms

    # ICO header
    $bw.Write([int16]0)     # reserved
    $bw.Write([int16]1)     # type: icon
    $bw.Write([int16]$Bitmaps.Count)

    # Calculate offsets
    $headerSize = 6 + ($Bitmaps.Count * 16)
    $dataOffset = $headerSize

    $pngDatas = @()
    foreach ($bmp in $Bitmaps) {
        $pngStream = New-Object System.IO.MemoryStream
        $bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngDatas += ,$pngStream.ToArray()
        $pngStream.Dispose()
    }

    # Directory entries
    for ($i = 0; $i -lt $Bitmaps.Count; $i++) {
        $w = $Bitmaps[$i].Width
        $h = $Bitmaps[$i].Height
        $bw.Write([byte]$(if ($w -ge 256) { 0 } else { $w }))
        $bw.Write([byte]$(if ($h -ge 256) { 0 } else { $h }))
        $bw.Write([byte]0)   # color palette
        $bw.Write([byte]0)   # reserved
        $bw.Write([int16]1)  # color planes
        $bw.Write([int16]32) # bits per pixel
        $bw.Write([int32]$pngDatas[$i].Length)
        $bw.Write([int32]$dataOffset)
        $dataOffset += $pngDatas[$i].Length
    }

    # Image data
    foreach ($png in $pngDatas) {
        $bw.Write($png)
    }

    [System.IO.File]::WriteAllBytes($Path, $ms.ToArray())
    $bw.Dispose()
    $ms.Dispose()
}

# Generate icons at multiple sizes
$sizes = @(256, 128, 64, 48, 32, 16)
$bitmaps = @()
foreach ($s in $sizes) {
    $bitmaps += New-AppIcon -Size $s
}

# Save ICO with all sizes
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Save-Ico -Bitmaps $bitmaps -Path (Join-Path $scriptDir "app.ico")

# Save 256px PNG
$bitmaps[0].Save((Join-Path $scriptDir "app.png"), [System.Drawing.Imaging.ImageFormat]::Png)

# Save 512px PNG for high-DPI
$large = New-AppIcon -Size 512
$large.Save((Join-Path $scriptDir "app-512.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$large.Dispose()

foreach ($b in $bitmaps) { $b.Dispose() }

Write-Host "Generated app.ico, app.png, and app-512.png"
