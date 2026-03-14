#!/usr/bin/env pwsh
<#
.SYNOPSIS
Capture CLI 빌드 스크립트

.DESCRIPTION
C# 캡처 CLI를 빌드하고 .flow/bin 폴더에 복사합니다.

.PARAMETER Configuration
빌드 구성 (Debug 또는 Release)

.EXAMPLE
./build-capture.ps1

.EXAMPLE
./build-capture.ps1 -Configuration Debug
#>

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$projectPath = Join-Path $scriptRoot "tools/capture-cli/capture-cli.csproj"
$outputDir = Join-Path $scriptRoot ".flow/bin"
$publishDir = Join-Path $scriptRoot ".flow/bin/.capture-publish"

if (-not (Test-Path $projectPath)) {
    Write-Error "Project not found: $projectPath"
    exit 1
}

Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Capture CLI Build" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration: $Configuration" -ForegroundColor Gray
Write-Host "Output: $outputDir" -ForegroundColor Gray
Write-Host ""

# 출력 디렉토리 생성
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

# 빌드
Write-Host "Building capture-cli..." -ForegroundColor Yellow

$publishArgs = @(
    "publish"
    $projectPath
    "-c", $Configuration
    "-r", "win-x64"
    "--self-contained"
    "-p:PublishSingleFile=true"
    "-p:EnableCompressionInSingleFile=true"
    "-p:DebugType=None"
    "-p:DebugSymbols=false"
    "-o", $publishDir
)

if ($Configuration -eq "Release") {
    $publishArgs += "-p:DebugType=none"
    $publishArgs += "-p:DebugSymbols=false"
}

try {
    $output = dotnet @publishArgs 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ❌ Build failed" -ForegroundColor Red
        $output | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        exit 1
    }

    # 단일 파일을 .flow/bin으로 복사
    $publishExe = Join-Path $publishDir "capture.exe"
    $outputFile = Join-Path $outputDir "capture.exe"

    if (Test-Path $publishExe) {
        Remove-Item -Path (Join-Path $outputDir "capture.*") -Force -ErrorAction SilentlyContinue
        Copy-Item -Path $publishExe -Destination $outputFile -Force
    }

    if (Test-Path $outputFile) {
        $size = (Get-Item $outputFile).Length / 1MB
        Write-Host "" 
        Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
        Write-Host "  Build Summary" -ForegroundColor Cyan
        Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
        Write-Host "  ✅ Success ($([math]::Round($size, 2)) MB)" -ForegroundColor Green
        Write-Host "  Path: $outputFile" -ForegroundColor Gray
        Write-Host ""
        Write-Host "Usage examples:" -ForegroundColor Gray
        Write-Host "  capture list-windows" -ForegroundColor White
        Write-Host '  capture window --name "메모장" --output screenshot.png' -ForegroundColor White
        Write-Host "  capture monitor --index 0 --output monitor.png" -ForegroundColor White
    } else {
        Write-Host "  ❌ Output file not found: $outputFile" -ForegroundColor Red
        exit 1
    }

    Remove-Item -Path $publishDir -Recurse -Force -ErrorAction SilentlyContinue
}
catch {
    Write-Host "  ❌ Build error: $_" -ForegroundColor Red
    exit 1
}
