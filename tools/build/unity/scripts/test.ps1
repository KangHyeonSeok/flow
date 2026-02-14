<#
.SYNOPSIS
    Unity 프로젝트의 EditMode/PlayMode 테스트를 실행합니다.
.DESCRIPTION
    Unity CLI를 -runTests 모드로 실행하여 테스트를 수행합니다.
    NUnit XML 결과를 파싱하여 JSON 형식으로 출력합니다.
.PARAMETER ProjectPath
    Unity 프로젝트 경로
.PARAMETER Config
    설정 파일 경로 (선택사항)
.PARAMETER TestPlatform
    테스트 플랫폼 (EditMode 또는 PlayMode, 기본: EditMode)
.PARAMETER Pretty
    JSON 출력을 보기 좋게 포맷
.PARAMETER Timeout
    타임아웃 (초, 기본: 300)
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,

    [string]$Config = "",
    [string]$TestPlatform = "EditMode",
    [switch]$Pretty,
    [int]$Timeout = 300
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sw = [System.Diagnostics.Stopwatch]::StartNew()

# Unity Editor 경로 탐색
$resolveResult = & "$scriptDir/resolve-editor.ps1" -ProjectPath $ProjectPath 2>&1
$editorInfo = $resolveResult | ConvertFrom-Json

if (-not $editorInfo.success) {
    $result = @{
        success     = $false
        action      = "test"
        platform    = "unity"
        error       = $editorInfo.error
        duration_ms = $sw.ElapsedMilliseconds
    }
    $result | ConvertTo-Json -Depth 5
    exit 1
}

$unityExe = $editorInfo.editor_path
$projectFullPath = (Resolve-Path $ProjectPath).Path

# 테스트 결과 파일 경로
$testResultsDir = Join-Path $projectFullPath "build"
if (-not (Test-Path $testResultsDir)) {
    New-Item -ItemType Directory -Path $testResultsDir -Force | Out-Null
}
$testResultsFile = Join-Path $testResultsDir "test-results-${TestPlatform}.xml"

# 로그 파일
$logFile = Join-Path $testResultsDir "unity-test-${TestPlatform}.log"

# Unity CLI 테스트 실행
$unityArgs = @(
    "-batchmode"
    "-nographics"
    "-projectPath", $projectFullPath
    "-runTests"
    "-testPlatform", $TestPlatform
    "-testResults", $testResultsFile
    "-logFile", $logFile
)

Write-Host "Unity 테스트 시작 ($TestPlatform): $unityExe" -ForegroundColor Cyan

$process = Start-Process -FilePath $unityExe -ArgumentList $unityArgs -NoNewWindow -PassThru -Wait:$false
$exited = $process.WaitForExit($Timeout * 1000)

if (-not $exited) {
    try { $process.Kill($true) } catch {}
    $result = @{
        success     = $false
        action      = "test"
        platform    = "unity"
        error       = "테스트 타임아웃 (${Timeout}초)"
        duration_ms = $sw.ElapsedMilliseconds
        exit_code   = -1
        test_platform = $TestPlatform
    }
    $result | ConvertTo-Json -Depth 5
    exit 1
}

$exitCode = $process.ExitCode
$sw.Stop()

# NUnit XML 결과 파싱
function Parse-NUnitResults {
    param([string]$XmlPath)

    if (-not (Test-Path $XmlPath)) {
        return @{
            total   = 0
            passed  = 0
            failed  = 0
            skipped = 0
            tests   = @()
        }
    }

    try {
        [xml]$xml = Get-Content $XmlPath -Raw
        $testRun = $xml.'test-run'

        $total = [int]($testRun.total ?? 0)
        $passed = [int]($testRun.passed ?? 0)
        $failed = [int]($testRun.failed ?? 0)
        $skipped = [int]($testRun.skipped ?? 0)

        # 개별 테스트 케이스 수집 (상위 20개)
        $testCases = @()
        $cases = $xml.SelectNodes("//test-case") | Select-Object -First 20
        foreach ($tc in $cases) {
            $testCases += @{
                name     = $tc.name
                fullname = $tc.fullname
                result   = $tc.result
                duration = $tc.duration
                message  = if ($tc.failure) { $tc.failure.message } else { $null }
            }
        }

        return @{
            total   = $total
            passed  = $passed
            failed  = $failed
            skipped = $skipped
            tests   = $testCases
        }
    }
    catch {
        return @{
            total   = 0
            passed  = 0
            failed  = 0
            skipped = 0
            error   = $_.Exception.Message
            tests   = @()
        }
    }
}

$testResults = Parse-NUnitResults -XmlPath $testResultsFile

if ($exitCode -eq 0 -and $testResults.failed -eq 0) {
    $result = @{
        success       = $true
        action        = "test"
        platform      = "unity"
        exit_code     = $exitCode
        duration_ms   = $sw.ElapsedMilliseconds
        test_platform = $TestPlatform
        total         = $testResults.total
        passed        = $testResults.passed
        failed        = $testResults.failed
        skipped       = $testResults.skipped
        tests         = $testResults.tests
        results_file  = $testResultsFile
        log_file      = $logFile
    }
} else {
    $failedTests = $testResults.tests | Where-Object { $_.result -eq "Failed" }
    $result = @{
        success       = $false
        action        = "test"
        platform      = "unity"
        exit_code     = $exitCode
        duration_ms   = $sw.ElapsedMilliseconds
        error         = "테스트 실패: $($testResults.failed)/$($testResults.total)"
        test_platform = $TestPlatform
        total         = $testResults.total
        passed        = $testResults.passed
        failed        = $testResults.failed
        skipped       = $testResults.skipped
        failed_tests  = $failedTests
        results_file  = $testResultsFile
        log_file      = $logFile
    }
}

$result | ConvertTo-Json -Depth 5
exit $(if ($testResults.failed -gt 0) { 1 } else { $exitCode })
