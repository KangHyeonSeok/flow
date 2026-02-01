#!/usr/bin/env pwsh
# 준비 단계: feature_name 기반 컨텍스트 초기화

[CmdletBinding()]
param(
    [string]$FeatureName,
    [switch]$Force,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

if ($Help) {
    Write-Output "Usage: ./prepare-context.ps1 -FeatureName <name> [-Force] [-Help]"
    Write-Output "  -FeatureName  기능 이름 (필수, 없으면 FLOW_FEATURE 사용)"
    Write-Output "  -Force        기존 context-phase.json 덮어쓰기"
    exit 0
}

. "$PSScriptRoot/common.ps1"

$resolvedFeature = $FeatureName
if (-not $resolvedFeature) {
    $resolvedFeature = $env:FLOW_FEATURE
}

if (-not $resolvedFeature) {
    Write-FlowOutput "Feature name is required. Use -FeatureName or set FLOW_FEATURE." -Level Error
    exit 1
}

$resolvedFeature = ConvertTo-FeatureName -Title $resolvedFeature

$featureDir = Get-FeatureDir -FeatureName $resolvedFeature
if (-not (Test-Path $featureDir)) {
    New-Item -ItemType Directory -Path $featureDir -Force | Out-Null
}

$contextFile = Get-FeatureContextFile -FeatureName $resolvedFeature
if ((Test-Path $contextFile) -and -not $Force) {
    Write-FlowOutput "context-phase.json already exists. Use -Force to reset: $contextFile" -Level Warning
    exit 0
}

$reason = if ($Force) { "Context reset in prepare step" } else { "Context initialized in prepare step" }
Set-CurrentPhase -Phase "IDLE" -Reason $reason -FeatureName $resolvedFeature

Write-FlowOutput "Context prepared for feature: $resolvedFeature" -Level Success
Write-Output "  File: $contextFile"
