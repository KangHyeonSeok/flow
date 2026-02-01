#!/usr/bin/env pwsh
# 실행 완료 → VALIDATING 전이

[CmdletBinding()]
param(
    [string]$Summary = "",
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

if ($Help) {
    Write-Output "Usage: ./complete-execution.ps1 [-Summary <summary>] [-Help]"
    Write-Output "  -Summary  실행 완료 요약 (선택)"
    Write-Output ""
    Write-Output "EXECUTING → VALIDATING 상태로 전이합니다."
    Write-Output "검증 프로파일을 사용하여 자동 검증을 시작할 수 있습니다."
    exit 0
}

. "$PSScriptRoot/common.ps1"

# 현재 상태 확인
$phase = Get-CurrentPhase
if ($null -eq $phase) {
    Write-FlowOutput "상태 파일이 없습니다." -Level Error
    exit 1
}

if ($phase.phase -ne "EXECUTING") {
    Write-FlowOutput "현재 상태($($phase.phase))에서는 실행 완료할 수 없습니다." -Level Error
    Write-FlowOutput "EXECUTING 상태에서만 가능합니다." -Level Warning
    exit 1
}

# 검증 프로파일 로드
$profilesPath = Join-Path (Get-FlowRoot) "validation-profiles.json"
$profiles = $null
if (Test-Path $profilesPath) {
    $profiles = Get-Content $profilesPath -Raw -Encoding UTF8 | ConvertFrom-Json
}

# VALIDATING으로 전이
$reason = if ($Summary) { "실행 완료: $Summary" } else { "실행 완료, 검증 시작" }
Set-CurrentPhase -Phase "VALIDATING" -Reason $reason -FeatureName $phase.feature_name

Write-Output ""
Write-Output "═══════════════════════════════════════"
Write-Output "  실행 완료 → 검증 단계"
Write-Output "═══════════════════════════════════════"
Write-Output ""
Write-Output "  Feature: $($phase.feature_name)"
Write-Output "  상태: EXECUTING → VALIDATING"
Write-Output ""

if ($profiles) {
    Write-Output "  사용 가능한 검증 프로파일:"
    $profiles.PSObject.Properties | ForEach-Object {
        Write-Output "    - $($_.Name): $($_.Value.description)"
    }
    Write-Output ""
    Write-Output "  사용법:"
    Write-Output "    ./validation-runner.ps1 -Command 'npm run build'"
    Write-Output "    또는 플랜의 검증 방법 섹션을 참조하세요."
} else {
    Write-Output "  검증 프로파일이 없습니다."
    Write-Output "  플랜의 검증 방법 섹션을 참조하여 검증을 진행하세요."
}

Write-Output ""
Write-FlowOutput "VALIDATING 상태로 전이 완료" -Level Success
