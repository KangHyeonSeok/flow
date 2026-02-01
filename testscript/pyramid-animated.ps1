param(
    [ValidateRange(1, 200)]
    [int]$Height = 10,

    [ValidateRange(1, 1000)]
    [int]$Frames = 30,

    [ValidateRange(10, 2000)]
    [int]$DelayMs = 80,

    [ValidateNotNullOrEmpty()]
    [string]$Char = "*"
)

$maxWidth = 2 * $Height - 1
$minHeight = $Height + 1

$useConsole = $true
try {
    $windowWidth = [Console]::WindowWidth
    $windowHeight = [Console]::WindowHeight
} catch {
    $useConsole = $false
}

if (-not $useConsole -or [Console]::IsOutputRedirected) {
    for ($row = 1; $row -le $Height; $row++) {
        $rowWidth = 2 * $row - 1
        $spaces = $Height - $row
        $line = (" " * $spaces) + ($Char * $rowWidth)
        Write-Host $line
    }
    exit 0
}

if ($windowWidth -lt $maxWidth -or $windowHeight -lt $minHeight) {
    Write-Host "Console too small. Need width >= $maxWidth and height >= $minHeight." -ForegroundColor Yellow
    exit 1
}

$pattern = @()
for ($i = 1; $i -le $Height; $i++) { $pattern += $i }
for ($i = $Height - 1; $i -ge 1; $i--) { $pattern += $i }

$originalCursorVisible = [Console]::CursorVisible
[Console]::CursorVisible = $false

try {
    for ($frame = 0; $frame -lt $Frames; $frame++) {
        $currentHeight = $pattern[$frame % $pattern.Count]
        [Console]::Clear()

        $leftPad = [Math]::Max(0, [int](([Console]::WindowWidth - $maxWidth) / 2))

        for ($row = 1; $row -le $currentHeight; $row++) {
            $rowWidth = 2 * $row - 1
            $spaces = $leftPad + ($Height - $row)
            $line = (" " * $spaces) + ($Char * $rowWidth)
            Write-Host $line
        }

        Start-Sleep -Milliseconds $DelayMs
    }
}
finally {
    [Console]::CursorVisible = $originalCursorVisible
}
