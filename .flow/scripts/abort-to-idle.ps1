#!/usr/bin/env pwsh
# 작업 중단 및 IDLE 복귀

[CmdletBinding()]
param(
    [string]$Reason = "사용자 요청으로 중단",
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

if ($Help) {
    Write-Output "Usage: ./abort-to-idle.ps1 [-Reason <reason>] [-Help]"
    exit 0
}

. "$PSScriptRoot/common.ps1"

$phase = Get-CurrentPhase
if ($null -eq $phase) {
    Write-FlowOutput "초기화가 필요합니다." -Level Error
    exit 1
}

if ($phase.phase -eq "IDLE") {
    Write-FlowOutput "이미 IDLE 상태입니다." -Level Info
    exit 0
}

$previousPhase = $phase.phase
$previousFeature = $phase.feature_name

# 상태 전이
Set-CurrentPhase -Phase "IDLE" -Reason $Reason

Write-FlowOutput "작업이 중단되었습니다." -Level Warning
Write-Output ""
Write-Output "  이전 상태: $previousPhase"
Write-Output "  이전 기능: $previousFeature"
Write-Output "  사유: $Reason"
Write-Output ""
Write-Output "  /flow로 새 작업을 시작할 수 있습니다."
Write-Output ""

# result.md가 있으면 VS Code로 열기
if ($previousFeature) {
    $resultPath = Join-Path $PSScriptRoot "..\..\docs\implements\$previousFeature\result.md"
    if (Test-Path $resultPath) {
        Write-FlowOutput "result.md를 열고 있습니다..." -Level Info
        code $resultPath
    }
}
