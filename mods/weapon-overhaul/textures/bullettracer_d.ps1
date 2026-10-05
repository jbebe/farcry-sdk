<#
.SYNOPSIS
    Draws the tracer texture and builds it into the layer as graphics\gfx\weapons\bullettracer_d.xbt.

.DESCRIPTION
    The engine stretches the texture along the tracer, its leading end at the right, and across its
    width, and adds it to the scene. This draws a glowing orange streak with a hotter core that
    fades in from its tail. Uncompressed A8R8G8B8, because block compression bands a glow this
    small. Each smaller mip keeps the brightest of the rows it halves rather than their mean, so a
    tracer far off, a pixel wide, stays as bright as a near one. The .xbt header is the retail
    texture's own, from bullettracer_d.xml beside this script.

.EXAMPLE
    .\bullettracer_d.ps1
#>
$ErrorActionPreference = "Stop"

$Width = 64
$Height = 16
# Where along the streak the leading end is brightest, and where the fade from the tail ends.
$Head = 59.5
$FadeEnd = 44.0
# The streak's colour, and what its core adds on top.
$Orange = [double[]](1.0, 0.42, 0.05)
$Hot = [double[]](0.0, 0.33, 0.25)

$ProjectRoot = Split-Path $PSScriptRoot -Parent
$JackAll = Join-Path $ProjectRoot "..\..\tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe"
$Dds = Join-Path $ProjectRoot "out\bullettracer_d.dds"
$Xbt = Join-Path $ProjectRoot "layer\mods\graphics\gfx\weapons\bullettracer_d.xbt"

function Gauss([double]$x, [double]$width) {
    return [Math]::Exp(-($x / $width) * ($x / $width))
}

function SmoothStep([double]$from, [double]$to, [double]$x) {
    $t = [Math]::Min([Math]::Max(($x - $from) / ($to - $from), 0.0), 1.0)
    return $t * $t * (3.0 - 2.0 * $t)
}

# The top mip level, [row, column, channel] in nought to one.
$level = New-Object 'double[,,]' $Height, $Width, 3
for ($y = 0; $y -lt $Height; $y++) {
    $across = [Math]::Abs($y + 0.5 - $Height / 2) / ($Height / 2)
    $glow = Gauss $across 0.5
    $core = Gauss $across 0.2
    for ($x = 0; $x -lt $Width; $x++) {
        $along = 0.0
        $tip = 0.0
        if ($x -le [Math]::Ceiling($Head)) {
            $along = SmoothStep 1.0 $FadeEnd $x
            $tip = (Gauss ($x - $Head) 2.5) * $core * 0.5
        }
        for ($c = 0; $c -lt 3; $c++) {
            $level[$y, $x, $c] = [Math]::Min($along * ($Orange[$c] * $glow + $Hot[$c] * $core) + $tip, 1.0)
        }
    }
}

# Each smaller level averages the columns it halves and keeps the brightest of the rows.
$levels = New-Object System.Collections.ArrayList
[void]$levels.Add($level)
while ($level.GetLength(0) -gt 1 -or $level.GetLength(1) -gt 1) {
    $rows = $level.GetLength(0)
    $columns = $level.GetLength(1)
    $nextRows = [Math]::Max([int]($rows / 2), 1)
    $nextColumns = [Math]::Max([int]($columns / 2), 1)
    $next = New-Object 'double[,,]' $nextRows, $nextColumns, 3
    for ($y = 0; $y -lt $nextRows; $y++) {
        for ($x = 0; $x -lt $nextColumns; $x++) {
            for ($c = 0; $c -lt 3; $c++) {
                $brightest = 0.0
                foreach ($sy in @((2 * $y), (2 * $y + 1))) {
                    if ($sy -ge $rows) { continue }
                    $sum = 0.0
                    $count = 0
                    foreach ($sx in @((2 * $x), (2 * $x + 1))) {
                        if ($sx -ge $columns) { continue }
                        $sum += $level[$sy, $sx, $c]
                        $count++
                    }
                    $brightest = [Math]::Max($brightest, $sum / $count)
                }
                $next[$y, $x, $c] = $brightest
            }
        }
    }
    $level = $next
    [void]$levels.Add($level)
}

New-Item -ItemType Directory -Force -Path (Split-Path $Dds) | Out-Null
$writer = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($Dds))
$writer.Write([byte[]][char[]]"DDS ")
# DDS_HEADER: caps, height, width, pitch, pixel format and mip count given; A8R8G8B8.
$header = @(124, 0x2100F, $Height, $Width, ($Width * 4), 0, $levels.Count) + (@(0) * 11) +
    @(32, 0x41, 0, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000L) + @(0x401008, 0, 0, 0, 0)
foreach ($value in $header) {
    $writer.Write([uint32]$value)
}
foreach ($mip in $levels) {
    for ($y = 0; $y -lt $mip.GetLength(0); $y++) {
        for ($x = 0; $x -lt $mip.GetLength(1); $x++) {
            foreach ($c in 2, 1, 0) {
                $value = $mip[$y, $x, $c]
                $writer.Write([byte][Math]::Round($value * 255))
            }
            $writer.Write([byte]255)
        }
    }
}
$writer.Close()

New-Item -ItemType Directory -Force -Path (Split-Path $Xbt) | Out-Null
& $JackAll xbt build $Dds (Join-Path $PSScriptRoot "bullettracer_d.xml") --out $Xbt
if ($LASTEXITCODE -ne 0) { throw "jackall-cli xbt build failed (exit $LASTEXITCODE)." }
Write-Host "Built $Xbt" -ForegroundColor Green
