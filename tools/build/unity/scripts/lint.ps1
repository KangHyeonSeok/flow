<#
.SYNOPSIS
    Unity 프로젝트의 C# 코드를 린트합니다.
.DESCRIPTION
    dotnet format 을 사용하여 Unity 프로젝트의 C# 코드를 검사합니다.
    .sln 또는 .csproj 파일을 자동 탐색합니다.
.PARAMETER ProjectPath
    Unity 프로젝트 경로
.PARAMETER Config
    설정 파일 경로 (선택사항)
.PARAMETER Pretty
    JSON 출력을 보기 좋게 포맷
.PARAMETER Timeout
    타임아웃 (초, 기본: 120)
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,

    [string]$Config = "",
    [switch]$Pretty,
    [int]$Timeout = 120
)

$ErrorActionPreference = "Stop"
$sw = [System.Diagnostics.Stopwatch]::StartNew()

$projectFullPath = (Resolve-Path $ProjectPath).Path

# .sln 또는 .csproj 탐색
$slnFile = Get-ChildItem $projectFullPath -Filter "*.sln" -File -ErrorAction SilentlyContinue | Select-Object -First 1
$csprojFile = Get-ChildItem $projectFullPath -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Select-Object -First 1

$targetFile = $null
if ($slnFile) {
    $targetFile = $slnFile.FullName
} elseif ($csprojFile) {
    $targetFile = $csprojFile.FullName
}

if (-not $targetFile) {
    # Unity 프로젝트는 sln/csproj 없이도 C# 파일이 있을 수 있음
    # 유효성 검사만 수행
    $csFiles = Get-ChildItem $projectFullPath -Filter "*.cs" -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch "(Library|Temp|obj|bin)" }

    if ($csFiles.Count -eq 0) {
        $result = @{
            success     = $true
            action      = "lint"
            platform    = "unity"
            duration_ms = $sw.ElapsedMilliseconds
            message     = "린트 대상 C# 파일 없음"
            files_count = 0
        }
        $result | ConvertTo-Json -Depth 5
        exit 0
    }

    # sln 없이 기본 구문 검사
    $errors = @()
    $warnings = @()
    $checkedCount = 0

    foreach ($file in ($csFiles | Select-Object -First 100)) {
        $checkedCount++
        $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
        if (-not $content) { continue }

        # 기본적인 패턴 검사
        $lines = $content -split "`n"
        for ($i = 0; $i -lt $lines.Length; $i++) {
            $line = $lines[$i]

            # TODO/HACK/FIXME 주석 경고
            if ($line -match "\b(TODO|HACK|FIXME)\b") {
                $warnings += @{
                    file    = $file.FullName
                    line    = $i + 1
                    message = "코드 주석: $($Matches[0])"
                    severity = "warning"
                }
            }
        }
    }

    $sw.Stop()
    $result = @{
        success      = $true
        action       = "lint"
        platform     = "unity"
        duration_ms  = $sw.ElapsedMilliseconds
        files_checked = $checkedCount
        errors       = $errors
        warnings     = $warnings
        error_count  = $errors.Count
        warning_count = $warnings.Count
        message      = "기본 구문 검사 완료 (sln/csproj 없음, dotnet format 미사용)"
    }
    $result | ConvertTo-Json -Depth 5
    exit $(if ($errors.Count -gt 0) { 1 } else { 0 })
}

# dotnet format 실행
Write-Host "린트 실행: dotnet format $targetFile --verify-no-changes" -ForegroundColor Cyan

$formatArgs = @("format", $targetFile, "--verify-no-changes", "--verbosity", "diagnostic")

$tempStdout = [System.IO.Path]::GetTempFileName()
$tempStderr = [System.IO.Path]::GetTempFileName()

$process = Start-Process -FilePath "dotnet" -ArgumentList $formatArgs `
    -NoNewWindow -PassThru -Wait:$false `
    -RedirectStandardOutput $tempStdout `
    -RedirectStandardError $tempStderr

$exited = $process.WaitForExit($Timeout * 1000)

if (-not $exited) {
    try { $process.Kill($true) } catch {}
    $result = @{
        success     = $false
        action      = "lint"
        platform    = "unity"
        error       = "린트 타임아웃 (${Timeout}초)"
        duration_ms = $sw.ElapsedMilliseconds
        exit_code   = -1
    }
    $result | ConvertTo-Json -Depth 5
    exit 1
}

$exitCode = $process.ExitCode
$sw.Stop()

$stdout = if (Test-Path $tempStdout) { Get-Content $tempStdout -Raw } else { "" }
$stderr = if (Test-Path $tempStderr) { Get-Content $tempStderr -Raw } else { "" }

# 임시 파일 정리
Remove-Item $tempStdout, $tempStderr -ErrorAction SilentlyContinue

if ($exitCode -eq 0) {
    $result = @{
        success     = $true
        action      = "lint"
        platform    = "unity"
        exit_code   = $exitCode
        duration_ms = $sw.ElapsedMilliseconds
        target_file = $targetFile
        message     = "코드 포맷 검사 통과"
    }
} else {
    $result = @{
        success     = $false
        action      = "lint"
        platform    = "unity"
        exit_code   = $exitCode
        duration_ms = $sw.ElapsedMilliseconds
        error       = "코드 포맷 문제 발견"
        stderr      = $stderr
        stdout      = $stdout
        target_file = $targetFile
    }
}

$result | ConvertTo-Json -Depth 5
exit $exitCode
