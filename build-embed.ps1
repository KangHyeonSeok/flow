#!/usr/bin/env pwsh
<#
.SYNOPSIS
Embed CLI Native AOT 빌드 스크립트

.DESCRIPTION
C# 임베딩 CLI를 Native AOT로 빌드합니다.

.PARAMETER Configuration
빌드 구성 (Debug 또는 Release)

.PARAMETER RuntimeIdentifiers
대상 플랫폼 (win-x64, linux-x64, osx-x64)

.EXAMPLE
./build-embed.ps1 -Configuration Release

.EXAMPLE
./build-embed.ps1 -RuntimeIdentifiers @("win-x64", "linux-x64")
#>

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [ValidateSet("win-x64", "linux-x64", "osx-x64")]
    [string[]]$RuntimeIdentifiers = @("win-x64")
)

$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$projectPath = Join-Path $scriptRoot "tools/embed/embed.csproj"

if (-not (Test-Path $projectPath)) {
    Write-Error "Project not found: $projectPath"
    exit 1
}

Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Embed CLI Build" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration: $Configuration" -ForegroundColor Gray
Write-Host "Targets: $($RuntimeIdentifiers -join ', ')" -ForegroundColor Gray
Write-Host ""

$results = @()

foreach ($rid in $RuntimeIdentifiers) {
    Write-Host "Building for $rid..." -ForegroundColor Yellow

    $outputRoot = Join-Path $scriptRoot ".flow/rag/bin"
    $outputDir = if ($RuntimeIdentifiers.Count -gt 1) {
        Join-Path $outputRoot $rid
    } else {
        $outputRoot
    }
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    
    $publishArgs = @(
        "publish"
        $projectPath
        "-c", $Configuration
        "-r", $rid
        "--self-contained"
        "-p:PublishAot=true"
        "-o", $outputDir
    )
    
    # Debug 모드가 아닐 때만 strip 적용
    if ($Configuration -eq "Release") {
        $publishArgs += "-p:StripSymbols=true"
        $publishArgs += "-p:OptimizationPreference=Speed"
    }
    
    try {
        $output = dotnet @publishArgs 2>&1
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ❌ Build failed" -ForegroundColor Red
            $output | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
            $results += [PSCustomObject]@{
                Platform = $rid
                Success = $false
                Size = 0
                Path = ""
            }
            continue
        }
        
        # 출력 파일 경로 확인
        $outputFile = if ($rid -eq "win-x64") { "embed.exe" } else { "embed" }
        $outputPath = Join-Path $outputDir $outputFile
        
        if (Test-Path $outputPath) {
            $size = (Get-Item $outputPath).Length / 1MB
            Write-Host "  ✅ Success ($([math]::Round($size, 2)) MB)" -ForegroundColor Green
            Write-Host "    Path: $outputPath" -ForegroundColor Gray
            
            $results += [PSCustomObject]@{
                Platform = $rid
                Success = $true
                Size = [math]::Round($size, 2)
                Path = $outputPath
            }
        } else {
            Write-Host "  ❌ Output file not found" -ForegroundColor Red
            $results += [PSCustomObject]@{
                Platform = $rid
                Success = $false
                Size = 0
                Path = ""
            }
        }
    }
    catch {
        Write-Host "  ❌ Build error: $_" -ForegroundColor Red
        $results += [PSCustomObject]@{
            Platform = $rid
            Success = $false
            Size = 0
            Path = ""
        }
    }
    
    Write-Host ""
}

# 결과 요약
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Build Summary" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan

$successCount = ($results | Where-Object { $_.Success }).Count
$totalCount = $results.Count

if ($successCount -eq $totalCount) {
    Write-Host "  ✅ All builds successful ($successCount/$totalCount)" -ForegroundColor Green
} else {
    Write-Host "  ⚠️  Some builds failed ($successCount/$totalCount successful)" -ForegroundColor Yellow
}

foreach ($result in $results) {
    $icon = if ($result.Success) { "✅" } else { "❌" }
    $color = if ($result.Success) { "Green" } else { "Red" }
    $sizeText = if ($result.Size -gt 0) { " ($($result.Size) MB)" } else { "" }
    Write-Host "  $icon $($result.Platform)$sizeText" -ForegroundColor $color
}

# 실패한 빌드가 있으면 exit code 1 반환
if ($successCount -ne $totalCount) {
    exit 1
}
