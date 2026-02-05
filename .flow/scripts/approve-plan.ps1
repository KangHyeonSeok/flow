#!/usr/bin/env pwsh
# 플랜 승인 - REVIEWING → READY

[CmdletBinding()]
param(
    [string]$Comment = "",
    [switch]$Force,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

if ($Help) {
    Write-Output "Usage: ./approve-plan.ps1 [-Comment <comment>] [-Force] [-Help]"
    Write-Output "  -Comment  승인 코멘트"
    Write-Output "  -Force    체크리스트 무시하고 승인"
    exit 0
}

. "$PSScriptRoot/common.ps1"

$phase = Get-CurrentPhase
if ($null -eq $phase) {
    Write-FlowOutput "초기화가 필요합니다." -Level Error
    exit 1
}

# PLANNING → REVIEWING 자동 전이 (플랜 작성 완료 시)
if ($phase.phase -eq "PLANNING") {
    Write-FlowOutput "플랜을 REVIEWING 상태로 전이합니다..." -Level Info
    $currentFeature = $phase.feature_name
    Set-CurrentPhase -Phase "REVIEWING" -Reason "플랜 작성 완료, 검토 시작" -FeatureName $currentFeature -RequiresHuman $true
    $phase = Get-CurrentPhase
}

if ($phase.phase -ne "REVIEWING") {
    Write-FlowOutput "현재 상태($($phase.phase))에서는 승인할 수 없습니다." -Level Error
    Write-FlowOutput "REVIEWING 상태에서만 가능합니다." -Level Warning
    exit 1
}

# feature_name 가져오기
$featureName = $phase.feature_name

# 플랜 파일 확인 (우선순위: need-review-plan.md > plan.md)
$featureDir = Get-FeatureDir -FeatureName $featureName
$needReviewPlanPath = Join-Path $featureDir "need-review-plan.md"
$planPath = Join-Path $featureDir "plan.md"
$isNeedReview = $false

if (Test-Path $needReviewPlanPath) {
    $planPath = $needReviewPlanPath
    $isNeedReview = $true
} elseif (-not (Test-Path $planPath)) {
    Write-FlowOutput "플랜 파일을 찾을 수 없습니다: $planPath" -Level Error
    exit 1
}

$planContent = Get-Content $planPath -Raw

# 필수 섹션 체크
$requiredSections = @(
    "## 1. 입력",
    "## 2. 실행 단계", 
    "## 3. 출력",
    "## 4. 검증 방법",
    "## 5. 완료 조건"
)

$missingSections = @()
foreach ($section in $requiredSections) {
    if ($planContent -notmatch [regex]::Escape($section)) {
        $missingSections += $section
    }
}

if ($missingSections.Count -gt 0 -and -not $Force) {
    Write-FlowOutput "다음 필수 섹션이 누락되었습니다:" -Level Error
    $missingSections | ForEach-Object { Write-Output "  - $_" }
    Write-Output ""
    Write-Output "  -Force 옵션으로 강제 승인 가능"
    exit 1
}

# 플랜 파일을 VS Code로 열기
Write-FlowOutput "플랜 파일을 VS Code에서 엽니다..." -Level Info
code $planPath

# 사용자 확인
if (-not $Force) {
    Write-Output ""
    Write-Output "═══════════════════════════════════════"
    Write-Output "  플랜 승인 확인"
    Write-Output "═══════════════════════════════════════"
    Write-Output ""
    Write-Output "  Feature: $featureName"
    Write-Output "  File: $planPath"
    Write-Output ""
    Write-Output "  플랜 내용을 검토해주세요."
    Write-Output ""
    
    $confirm = Read-Host "  계속 진행하시겠습니까? (Y/변경 사항 입력)"
    
    if ($confirm -eq 'Y' -or $confirm -eq 'y') {
        # 승인 진행
    }
    else {
        # 변경 사항이 입력된 경우
        Write-FlowOutput "변경 사항: $confirm" -Level Info
        Write-Output ""
        Write-Output "  플랜 파일을 수정한 후 다시 approve-plan을 실행해주세요."
        Write-Output "  변경 요청 사항이 플랜에 반영되어야 합니다."
        Write-Output ""
        
        # 변경 사항을 임시 파일에 기록 (AI가 참고할 수 있도록)
        $feedbackPath = Join-Path $featureDir "feedback.txt"
        $feedbackContent = @"
# 플랜 변경 요청
Date: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Feature: $featureName

## 변경 사항
$confirm
"@
        # UTF-8 without BOM으로 저장
        [System.IO.File]::WriteAllText($feedbackPath, $feedbackContent, [System.Text.UTF8Encoding]::new($false))
        Write-FlowOutput "변경 요청이 저장되었습니다: $feedbackPath" -Level Info
        
        exit 0
    }
}

# need-review-plan.md → plan.md로 이름 변경 (승인 완료)
if ($isNeedReview) {
    $expectedFeatureDir = Get-FeatureDir -FeatureName $featureName
    if (-not (Test-Path $expectedFeatureDir)) {
        Write-FlowOutput "기능 디렉토리를 찾을 수 없습니다: $expectedFeatureDir" -Level Error
        exit 1
    }

    $resolvedExpectedDir = (Resolve-Path $expectedFeatureDir).Path
    $resolvedPlanDir = (Resolve-Path (Split-Path $planPath -Parent)).Path
    if ($resolvedPlanDir -ne $resolvedExpectedDir) {
        Write-FlowOutput "플랜 경로가 올바르지 않습니다. 예상: $resolvedExpectedDir, 실제: $resolvedPlanDir" -Level Error
        exit 1
    }

    $approvedPlanPath = Join-Path $featureDir "plan.md"
    Rename-Item -Path $planPath -NewName "plan.md" -Force
    Write-FlowOutput "플랜 파일 승인됨: need-review-plan.md → plan.md" -Level Info
    $planPath = $approvedPlanPath
}

# 상태 전이
Set-CurrentPhase -Phase "EXECUTING" -Reason "플랜 승인됨. 코멘트: $Comment" -FeatureName $featureName

Write-FlowOutput "플랜이 승인되었습니다!" -Level Success
Write-Output ""
Write-Output "  상태: REVIEWING → EXECUTING"
Write-Output "  다음 단계: 플랜에 따라 구현 시작"
Write-Output ""
