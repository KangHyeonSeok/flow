param(
    [ValidateRange(5, 60)]
    [int]$Size = 9,

    [ValidateNotNullOrEmpty()]
    [string]$Char = "*"
)

if ($Char.Length -ne 1) {
    throw "Char must be a single character."
}

$outer = [double]$Size
$ratio = [Math]::Sin(18 * [Math]::PI / 180) / [Math]::Sin(54 * [Math]::PI / 180)
$inner = $outer * $ratio

function Get-StarPolygon([double]$OuterRadius, [double]$InnerRadius) {
    $points = @()
    for ($i = 0; $i -lt 10; $i++) {
        $angleDeg = -90 + ($i * 36)
        $angleRad = $angleDeg * [Math]::PI / 180
        $radius = if ($i % 2 -eq 0) { $OuterRadius } else { $InnerRadius }
        $x = [Math]::Cos($angleRad) * $radius
        $y = [Math]::Sin($angleRad) * $radius
        $points += ,@($x, $y)
    }
    return ,$points
}

function Test-PointInPolygon([double]$X, [double]$Y, [object[]]$Polygon) {
    $inside = $false
    $j = $Polygon.Count - 1

    for ($i = 0; $i -lt $Polygon.Count; $i++) {
        $xi = [double]$Polygon[$i][0]
        $yi = [double]$Polygon[$i][1]
        $xj = [double]$Polygon[$j][0]
        $yj = [double]$Polygon[$j][1]

        $intersects = (($yi -gt $Y) -ne ($yj -gt $Y)) -and (
            $X -lt (($xj - $xi) * ($Y - $yi) / (($yj - $yi) + 1e-9) + $xi)
        )

        if ($intersects) {
            $inside = -not $inside
        }
        $j = $i
    }

    return $inside
}

$polygon = Get-StarPolygon -OuterRadius $outer -InnerRadius $inner
$max = [int](2 * $outer)

for ($row = 0; $row -le $max; $row++) {
    $y = $outer - $row
    $lineBuilder = New-Object System.Text.StringBuilder

    for ($col = 0; $col -le $max; $col++) {
        $x = $col - $outer
        if (Test-PointInPolygon -X $x -Y $y -Polygon $polygon) {
            [void]$lineBuilder.Append($Char)
        } else {
            [void]$lineBuilder.Append(" ")
        }
    }

    $line = $lineBuilder.ToString().TrimEnd()
    Write-Output $line
}
