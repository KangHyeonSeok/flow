#!/usr/bin/env pwsh
# build-flow.ps1 — flow-cli 빌드 및 .flow/bin 배포 + 빌드 모듈 패키징
[CmdletBinding()]
param(
    [switch]$Release,
    [ValidateSet("win-x64", "linux-x64", "osx-x64", "osx-arm64")]
    [string]$Runtime = "win-x64",
    [switch]$SkipModules
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

# ─── 빌드 모듈 패키징 ───
if (-not $SkipModules) {
    Write-Host ""
    Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
    Write-Host " Build Module Packaging" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan

    $buildModulesDir = Join-Path $PSScriptRoot "tools/build"
    $distDir = Join-Path $PSScriptRoot "dist"

    if (-not (Test-Path $distDir)) {
        New-Item -ItemType Directory -Path $distDir -Force | Out-Null
    }

    # VERSION 파일에서 버전 읽기
    $versionFile = Join-Path $PSScriptRoot "VERSION"
    $version = if (Test-Path $versionFile) {
        (Get-Content $versionFile -Raw).Trim()
    } else {
        "0.0.0"
    }

    # 각 빌드 모듈 디렉토리를 순회하며 ZIP 패키징
    $moduleCount = 0
    if (Test-Path $buildModulesDir) {
        $modules = Get-ChildItem $buildModulesDir -Directory
        foreach ($module in $modules) {
            $manifestPath = Join-Path $module.FullName "manifest.json"
            if (-not (Test-Path $manifestPath)) {
                Write-Host "  ⚠ $($module.Name): manifest.json 없음, 건너뜀" -ForegroundColor Yellow
                continue
            }

            $zipName = "build-module-$($module.Name).zip"
            $zipPath = Join-Path $distDir $zipName

            # 기존 ZIP 삭제
            if (Test-Path $zipPath) {
                Remove-Item $zipPath -Force
            }

            # manifest.json에 버전 업데이트
            $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
            $manifest.version = $version
            $manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath -Encoding UTF8

            # ZIP 생성 (모듈 디렉토리 전체)
            Compress-Archive -Path (Join-Path $module.FullName "*") -DestinationPath $zipPath -Force

            $zipSize = (Get-Item $zipPath).Length
            Write-Host "  📦 $zipName ($([math]::Round($zipSize / 1024, 1)) KB)" -ForegroundColor Green
            $moduleCount++
        }
    }

    if ($moduleCount -eq 0) {
        Write-Host "  ℹ 패키징할 빌드 모듈 없음" -ForegroundColor Yellow
    } else {
        Write-Host "✅ $moduleCount 개 빌드 모듈 패키징 완료 → $distDir" -ForegroundColor Green
    }
}
