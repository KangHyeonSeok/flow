#!/usr/bin/env pwsh
# build-flow.ps1 — flow-cli 빌드 및 .flow/bin 배포
[CmdletBinding()]
param(
    [switch]$Release,
    [ValidateSet("win-x64", "linux-x64", "osx-x64", "osx-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = 'Stop'

$config = if ($Release) { "Release" } else { "Debug" }
$projectDir = Join-Path $PSScriptRoot "tools/flow-cli"
$outputDir = Join-Path $PSScriptRoot ".flow/bin"

Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host " Flow CLI Build ($config, $Runtime)" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan

dotnet publish $projectDir -c $config -r $Runtime --self-contained -o $outputDir

if ($LASTEXITCODE -eq 0) {
    $flowExe = Join-Path $outputDir "flow.exe"
    Write-Host "✅ Build successful: $flowExe" -ForegroundColor Green
} else {
    Write-Host "❌ Build failed" -ForegroundColor Red
    exit 1
}
