#!/usr/bin/env pwsh
# 검증 실행 (단일 실행 - 재시도는 AI가 관리)

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Command,
    
    [string]$SuccessPattern = "",
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

if ($Help) {
    Write-Output "Usage: ./validation-runner.ps1 -Command <cmd> [-SuccessPattern <pattern>]"
    Write-Output "  -Command         실행할 검증 명령어"
    Write-Output "  -SuccessPattern  성공 판정 패턴 (정규식)"
    Write-Output ""
    Write-Output "Note: 재시도는 AI가 관리합니다. 스크립트는 1회 실행 후 결과 반환."
    exit 0
}

. "$PSScriptRoot/common.ps1"

$phase = Get-CurrentPhase

# EXECUTING 또는 RETRYING 또는 VALIDATING 상태 확인
if ($phase.phase -notin @("EXECUTING", "RETRYING", "VALIDATING")) {
    Write-FlowOutput "검증은 EXECUTING/RETRYING/VALIDATING 상태에서만 가능합니다." -Level Error
    exit 1
}

$success = $false
$errorMessage = ""
$output = ""

Write-FlowOutput "검증 실행 (retry_count: $($phase.retry_count) / max: $($phase.max_retries))" -Level Info
Write-Output "  명령어: $Command"
Write-Output ""

try {
    # LASTEXITCODE 초기화 (이전 명령 영향 제거)
    $global:LASTEXITCODE = 0
    
    # 명령 실행
    $output = Invoke-Expression $Command 2>&1
    $exitCode = $LASTEXITCODE
    
    # null인 경우 0으로 (네이티브 명령이 아닌 경우)
    if ($null -eq $exitCode) { $exitCode = 0 }
    
    Write-Output $output
    
    # 성공 판정
    if ($exitCode -eq 0) {
        if ($SuccessPattern) {
            if ($output -match $SuccessPattern) {
                $success = $true
            } else {
                $errorMessage = "성공 패턴 불일치: $SuccessPattern"
            }
        } else {
            $success = $true
        }
    } else {
        $errorMessage = "Exit code: $exitCode"
    }
} catch {
    $errorMessage = $_.Exception.Message
}

# 결과 출력
Write-Output ""
Write-Output "═══════════════════════════════════════"

if ($success) {
    # 성공 시 VALIDATING 상태로 전이 (확장 체크를 위해)
    Set-CurrentPhase -Phase "VALIDATING" -Reason "검증 통과" -FeatureName $phase.feature_name
    Write-FlowOutput "검증 성공!" -Level Success
    
    $result = @{
        success = $true
        output = $output -join "`n"
    }
} else {
    # 실패 시: retry_count 체크
    $newRetryCount = Add-RetryCount
    
    if ($newRetryCount -ge $phase.max_retries) {
        # 최대 재시도 초과 → BLOCKED
        Set-CurrentPhase -Phase "BLOCKED" -Reason "검증 실패 (최대 재시도 초과): $errorMessage" -FeatureName $phase.feature_name -RequiresHuman $true -RetryCount $newRetryCount -MaxRetries $phase.max_retries
        Write-FlowOutput "검증 실패! 최대 재시도 초과. 사람 개입이 필요합니다." -Level Error
    } else {
        # 재시도 가능 → RETRYING (AI가 오류 분석 후 재시도)
        Set-CurrentPhase -Phase "RETRYING" -Reason "검증 실패: $errorMessage" -FeatureName $phase.feature_name -RetryCount $newRetryCount -MaxRetries $phase.max_retries
        Write-FlowOutput "검증 실패. AI가 오류를 분석하고 수정 후 재시도해야 합니다." -Level Warning
    }
    
    Write-Output "  오류: $errorMessage"
    Write-Output "  재시도 횟수: $newRetryCount / $($phase.max_retries)"
    
    $result = @{
        success = $false
        error = $errorMessage
        output = $output -join "`n"
        retry_count = $newRetryCount
        max_retries = $phase.max_retries
    }
}

Write-Output "═══════════════════════════════════════"
Write-Output ""

$result | ConvertTo-Json -Depth 10
