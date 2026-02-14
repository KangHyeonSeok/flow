<#
.SYNOPSIS
    Unity 프로젝트를 빌드합니다.
.DESCRIPTION
    Unity CLI를 -batchmode -nographics -quit 모드로 실행하여 프로젝트를 빌드합니다.
    빌드 결과는 JSON 형식으로 출력합니다.
.PARAMETER ProjectPath
    Unity 프로젝트 경로
.PARAMETER Config
    빌드 설정 파일 경로 (선택사항)
.PARAMETER BuildTarget
    빌드 타겟 플랫폼 (기본: Win64)
.PARAMETER OutputPath
    빌드 결과 출력 경로 (기본: build/output)
.PARAMETER Pretty
    JSON 출력을 보기 좋게 포맷
.PARAMETER Timeout
    타임아웃 (초, 기본: 600)
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,

    [string]$Config = "",
    [string]$BuildTarget = "Win64",
    [string]$OutputPath = "build/output",
    [switch]$Pretty,
    [int]$Timeout = 600
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
        action      = "build"
        platform    = "unity"
        error       = $editorInfo.error
        duration_ms = $sw.ElapsedMilliseconds
    }
    $result | ConvertTo-Json -Depth 5
    exit 1
}

$unityExe = $editorInfo.editor_path
$projectFullPath = (Resolve-Path $ProjectPath).Path
$outputFullPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $projectFullPath $OutputPath
}

# 출력 디렉토리 확보
$outputDir = Split-Path $outputFullPath -Parent
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# 로그 파일 경로
$logFile = Join-Path $projectFullPath "build/unity-build.log"
$logDir = Split-Path $logFile -Parent
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

# buildTarget 매핑
$targetMap = @{
    "Win64"        = "StandaloneWindows64"
    "Win"          = "StandaloneWindows"
    "Linux64"      = "StandaloneLinux64"
    "OSX"          = "StandaloneOSX"
    "OSXUniversal" = "StandaloneOSX"
    "Android"      = "Android"
    "iOS"          = "iOS"
    "WebGL"        = "WebGL"
}

$unityTarget = if ($targetMap.ContainsKey($BuildTarget)) {
    $targetMap[$BuildTarget]
} else {
    $BuildTarget
}

# Unity CLI 실행
$unityArgs = @(
    "-batchmode"
    "-nographics"
    "-quit"
    "-projectPath", $projectFullPath
    "-buildTarget", $unityTarget
    "-logFile", $logFile
)

# executeMethod로 빌드 호출 (표준 빌드 메서드)
$unityArgs += @("-executeMethod", "UnityEditor.BuildPlayerWindow.CallBuildMethods")

Write-Host "Unity 빌드 시작: $unityExe $($unityArgs -join ' ')" -ForegroundColor Cyan

$process = Start-Process -FilePath $unityExe -ArgumentList $unityArgs -NoNewWindow -PassThru -Wait:$false
$exited = $process.WaitForExit($Timeout * 1000)

if (-not $exited) {
    try { $process.Kill($true) } catch {}
    $result = @{
        success     = $false
        action      = "build"
        platform    = "unity"
        error       = "빌드 타임아웃 (${Timeout}초)"
        duration_ms = $sw.ElapsedMilliseconds
        exit_code   = -1
        log_file    = $logFile
    }
    $result | ConvertTo-Json -Depth 5
    exit 1
}

$exitCode = $process.ExitCode
$sw.Stop()

# 로그 파일 마지막 50줄 캡처
$logTail = ""
if (Test-Path $logFile) {
    $logTail = Get-Content $logFile -Tail 50 -ErrorAction SilentlyContinue | Out-String
}

# 빌드 출력 탐색
$outputFiles = @()
if (Test-Path $outputFullPath) {
    $outputFiles = Get-ChildItem $outputFullPath -Recurse -File |
        Select-Object -First 20 |
        ForEach-Object { $_.FullName }
}

if ($exitCode -eq 0) {
    $result = @{
        success     = $true
        action      = "build"
        platform    = "unity"
        exit_code   = $exitCode
        duration_ms = $sw.ElapsedMilliseconds
        output_path = $outputFullPath
        output_files = $outputFiles
        log_file    = $logFile
        build_target = $BuildTarget
    }
} else {
    $result = @{
        success     = $false
        action      = "build"
        platform    = "unity"
        exit_code   = $exitCode
        duration_ms = $sw.ElapsedMilliseconds
        error       = "Unity 빌드 실패 (exit code: $exitCode)"
        stderr      = $logTail
        log_file    = $logFile
        build_target = $BuildTarget
    }
}

$result | ConvertTo-Json -Depth 5
exit $exitCode
