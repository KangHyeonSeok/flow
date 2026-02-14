<#
.SYNOPSIS
    Unity 빌드 결과물을 실행합니다.
.DESCRIPTION
    빌드 출력 경로에서 실행 파일을 찾아 실행합니다.
    플랫폼별 실행 파일 패턴을 자동 감지합니다.
.PARAMETER ProjectPath
    Unity 프로젝트 경로
.PARAMETER Config
    설정 파일 경로 (선택사항)
.PARAMETER OutputPath
    빌드 출력 경로 (기본: build/output)
.PARAMETER Pretty
    JSON 출력을 보기 좋게 포맷
.PARAMETER Timeout
    실행 타임아웃 (초, 기본: 60)
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,

    [string]$Config = "",
    [string]$OutputPath = "build/output",
    [switch]$Pretty,
    [int]$Timeout = 60
)

$ErrorActionPreference = "Stop"
$sw = [System.Diagnostics.Stopwatch]::StartNew()

$projectFullPath = (Resolve-Path $ProjectPath).Path
$outputFullPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $projectFullPath $OutputPath
}

if (-not (Test-Path $outputFullPath)) {
    $result = @{
        success     = $false
        action      = "run"
        platform    = "unity"
        error       = "빌드 출력 경로를 찾을 수 없습니다: $outputFullPath"
        duration_ms = $sw.ElapsedMilliseconds
    }
    $result | ConvertTo-Json -Depth 5
    exit 1
}

# 실행 파일 탐색 패턴
$executablePatterns = @(
    "*.exe"      # Windows
    "*.x86_64"   # Linux
    "*.app"      # macOS
)

$executable = $null
foreach ($pattern in $executablePatterns) {
    $found = Get-ChildItem $outputFullPath -Filter $pattern -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) {
        $executable = $found.FullName
        break
    }
}

# 서브디렉토리도 탐색
if (-not $executable) {
    foreach ($pattern in $executablePatterns) {
        $found = Get-ChildItem $outputFullPath -Filter $pattern -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            $executable = $found.FullName
            break
        }
    }
}

if (-not $executable) {
    $result = @{
        success     = $false
        action      = "run"
        platform    = "unity"
        error       = "실행 파일을 찾을 수 없습니다."
        search_path = $outputFullPath
        patterns    = $executablePatterns
        duration_ms = $sw.ElapsedMilliseconds
    }
    $result | ConvertTo-Json -Depth 5
    exit 1
}

Write-Host "실행: $executable" -ForegroundColor Cyan

$process = Start-Process -FilePath $executable -NoNewWindow -PassThru -Wait:$false
$exited = $process.WaitForExit($Timeout * 1000)

if (-not $exited) {
    try { $process.Kill($true) } catch {}
    $result = @{
        success     = $false
        action      = "run"
        platform    = "unity"
        error       = "실행 타임아웃 (${Timeout}초)"
        executable  = $executable
        duration_ms = $sw.ElapsedMilliseconds
        exit_code   = -1
    }
    $result | ConvertTo-Json -Depth 5
    exit 1
}

$exitCode = $process.ExitCode
$sw.Stop()

if ($exitCode -eq 0) {
    $result = @{
        success     = $true
        action      = "run"
        platform    = "unity"
        exit_code   = $exitCode
        executable  = $executable
        duration_ms = $sw.ElapsedMilliseconds
    }
} else {
    $result = @{
        success     = $false
        action      = "run"
        platform    = "unity"
        exit_code   = $exitCode
        executable  = $executable
        error       = "실행 실패 (exit code: $exitCode)"
        duration_ms = $sw.ElapsedMilliseconds
    }
}

$result | ConvertTo-Json -Depth 5
exit $exitCode
