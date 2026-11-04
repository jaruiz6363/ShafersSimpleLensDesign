# make-icon.ps1 — draws the application icon: icon.png (256 px), icon.ico (the exe, the window, the installer and
# shortcuts) and icon.icns (a macOS app, later). As MonoSearch's.
#
# The picture is a Shafer lens: air-spaced singlets, a stop in front, and three ray bundles converging on a flat
# image. Run it again only to change the picture; the outputs are committed.
#
#   pwsh src/ShafersSimpleLensDesign/Assets/make-icon.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Draw-Icon([int]$Size) {
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $Size / 256.0

    # Rounded square background.
    $r = 48 * $s; $m = 8 * $s; $w = $Size - 2 * $m
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($m, $m, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($m + $w - 2 * $r, $m, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($m + $w - 2 * $r, $m + $w - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($m, $m + $w - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $Size), ([System.Drawing.Color]::FromArgb(255, 18, 32, 64)), ([System.Drawing.Color]::FromArgb(255, 10, 18, 38))
    $g.FillPath($bg, $path)
    $g.SetClip($path)

    $cy = 128 * $s; $fx = 222 * $s

    # Three ray bundles, from the left edge to their foci on the image plane at fx.
    $colors = @(
        [System.Drawing.Color]::FromArgb(150, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(150, 255, 196, 64),
        [System.Drawing.Color]::FromArgb(150, 255, 110, 140))
    $foci = @(0, -46, 46)
    for ($i = 0; $i -lt 3; $i++) {
        $fy = $cy + $foci[$i] * $s
        $tilt = -$foci[$i] * 0.35 * $s
        $cone = New-Object System.Drawing.Drawing2D.GraphicsPath
        $cone.AddPolygon([System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF (20 * $s), ($cy - 44 * $s - $tilt)),
            (New-Object System.Drawing.PointF (120 * $s), ($cy - 40 * $s)),
            (New-Object System.Drawing.PointF $fx, $fy),
            (New-Object System.Drawing.PointF (120 * $s), ($cy + 40 * $s)),
            (New-Object System.Drawing.PointF (20 * $s), ($cy + 44 * $s - $tilt))))
        $g.FillPath((New-Object System.Drawing.SolidBrush $colors[$i]), $cone)
    }

    # Three air-spaced singlets: biconvex, meniscus, biconvex, as lens outlines from two arcs.
    $glass = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 150, 196, 236))
    $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 230, 236, 245)), (3 * $s)
    foreach ($e in @(@(52, 16, 64), @(98, 14, 56), @(138, 12, 50))) {
        $x = $e[0] * $s; $t = $e[1] * $s; $h = $e[2] * $s
        $lens = New-Object System.Drawing.Drawing2D.GraphicsPath
        $lens.AddBezier($x, $cy - $h, $x - $t, $cy - $h / 2, $x - $t, $cy + $h / 2, $x, $cy + $h)
        $lens.AddLine($x, $cy + $h, $x + $t, $cy + $h)
        $lens.AddBezier($x + $t, $cy + $h, $x + 2 * $t, $cy + $h / 2, $x + 2 * $t, $cy - $h / 2, $x + $t, $cy - $h)
        $lens.CloseFigure()
        $g.FillPath($glass, $lens)
        $g.DrawPath($edge, $lens)
    }

    # The stop, and the flat image.
    $stop = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 230, 236, 245)), (6 * $s)
    $g.DrawLine($stop, 26 * $s, $cy - 74 * $s, 26 * $s, $cy - 50 * $s)
    $g.DrawLine($stop, 26 * $s, $cy + 50 * $s, 26 * $s, $cy + 74 * $s)
    $img = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 230, 236, 245)), (9 * $s)
    $img.StartCap = 'Round'; $img.EndCap = 'Round'
    $g.DrawLine($img, $fx, $cy - 66 * $s, $fx, $cy + 66 * $s)

    $g.Dispose()
    return $bmp
}

function Png-Bytes([int]$Size) {
    $bmp = Draw-Icon $Size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

[System.IO.File]::WriteAllBytes((Join-Path $Here 'icon.png'), (Png-Bytes 256))

# icon.ico: a directory of PNG images (Windows Vista and later read PNG entries).
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = $sizes | ForEach-Object { , (Png-Bytes $_) }
$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ico
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $d = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $Here 'icon.ico'), $ico.ToArray())

# icon.icns (macOS: the .app bundle, built by the release scripts): big-endian chunks of PNG images.
function BE32([int]$v) { $b = [BitConverter]::GetBytes([uint32]$v); [Array]::Reverse($b); return , $b }
$chunks = @(('icp4', 16), ('icp5', 32), ('icp6', 64), ('ic07', 128), ('ic08', 256), ('ic09', 512), ('ic10', 1024))
$body = New-Object System.IO.MemoryStream
foreach ($c in $chunks) {
    $png = Png-Bytes $c[1]
    $body.Write([System.Text.Encoding]::ASCII.GetBytes($c[0]), 0, 4)
    $body.Write((BE32 ($png.Length + 8)), 0, 4)
    $body.Write($png, 0, $png.Length)
}
$icns = New-Object System.IO.MemoryStream
$icns.Write([System.Text.Encoding]::ASCII.GetBytes('icns'), 0, 4)
$icns.Write((BE32 ($body.Length + 8)), 0, 4)
$body.WriteTo($icns)
[System.IO.File]::WriteAllBytes((Join-Path $Here 'icon.icns'), $icns.ToArray())

Write-Host "Wrote icon.png, icon.ico and icon.icns in $Here"
