#!/usr/bin/env pwsh
# 새 플랜 시작 - IDLE → PLANNING

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Title,
    
    [string]$Description = "",
    [switch]$Force,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

if ($Help) {
    Write-Output "Usage: ./start-plan.ps1 -Title <title> [-Description <desc>] [-Force] [-Help]"
    Write-Output "  -Title        플랜 제목 (필수)"
    Write-Output "  -Description  플랜 설명"
    Write-Output "  -Force        기존 컨텍스트 덮어쓰기"
    exit 0
}

. "$PSScriptRoot/common.ps1"

# Plan ID 생성 (기능 이름 기반)
$featureName = ConvertTo-FeatureName -Title $Title
$date = Get-Date -Format "yyyyMMdd"
$planId = "$featureName"

# 기능 폴더 준비 (prepare-context 로직 통합)
$featureDir = Get-FeatureDir -FeatureName $featureName
if (-not (Test-Path $featureDir)) {
    New-Item -ItemType Directory -Path $featureDir -Force | Out-Null
}

# logs 및 backups 디렉토리 생성
$logsDir = Join-Path $featureDir "logs"
$backupsDir = Join-Path $logsDir "backups"
if (-not (Test-Path $logsDir)) {
    New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
}
if (-not (Test-Path $backupsDir)) {
    New-Item -ItemType Directory -Path $backupsDir -Force | Out-Null
}

# context-phase.json 초기화 또는 확인
$contextFile = Get-FeatureContextFile -FeatureName $featureName
if ((Test-Path $contextFile) -and -not $Force) {
    Write-FlowOutput "기능이 이미 존재합니다: $featureName" -Level Warning
    Write-FlowOutput "기존 작업을 계속하거나 -Force로 리셋하세요." -Level Warning
    exit 1
}

if (-not (Test-Path $contextFile) -or $Force) {
    $reason = if ($Force) { "Context reset in start-plan" } else { "Context initialized in start-plan" }
    Set-CurrentPhase -Phase "IDLE" -Reason $reason -FeatureName $featureName
}

# 플랜 파일 경로 (리뷰 전: need-review-plan.md)
$planPath = Join-Path $featureDir "need-review-plan.md"

# 이미 플랜이 있으면 오류 (need-review-plan.md 또는 plan.md)
$existingPlan = Join-Path $featureDir "plan.md"
if ((Test-Path $planPath) -or (Test-Path $existingPlan)) {
    $existingFile = if (Test-Path $planPath) { $planPath } else { $existingPlan }
    Write-FlowOutput "이미 플랜이 존재합니다: $existingFile" -Level Error
    Write-FlowOutput "기존 플랜을 삭제하거나 다른 제목을 사용하세요." -Level Warning
    exit 1
}

# 템플릿 복사
$templatePath = Join-Path (Get-FlowRoot) "templates/plan-template.md"

if (Test-Path $templatePath) {
    # UTF-8로 명시적 읽기 (BOM 유무 모두 처리)
    $content = [System.IO.File]::ReadAllText($templatePath, [System.Text.Encoding]::UTF8)
    $content = $content -replace '\[PLAN_TITLE\]', $Title
    $content = $content -replace '\[DATE\]', (Get-Date -Format "yyyy-MM-dd")
    $content = $content -replace 'plan-YYYYMMDD-###', $planId
    # UTF-8 without BOM으로 저장
    [System.IO.File]::WriteAllText($planPath, $content, [System.Text.UTF8Encoding]::new($false))
} else {
    # 기본 플랜 생성
    $defaultContent = @"
# Plan: $Title

**Plan ID**: ``$planId``
**Created**: $(Get-Date -Format "yyyy-MM-dd")
**Description**: $Description

## 1. 입력 (Inputs)
[TODO: 입력 정의]

## 2. 출력 (Outputs)
[TODO: 출력 정의]

## 3. 검증 방법 (Validation)
[TODO: 검증 방법 정의]

## 4. 완료 조건 (Done Criteria)
[TODO: 완료 조건 정의]
"@
    # UTF-8 without BOM으로 저장
    [System.IO.File]::WriteAllText($planPath, $defaultContent, [System.Text.UTF8Encoding]::new($false))
}

# 상태 전이
Set-CurrentPhase -Phase "PLANNING" -Reason "새 플랜 시작: $Title" -FeatureName $planId

Write-FlowOutput "플랜 생성됨: $planId" -Level Success
Write-Output ""
Write-Output "  파일: $planPath"
Write-Output "  위치: docs/flow/implements/$featureName/"
Write-Output ""
Write-Output "  다음 단계:"
Write-Output "    1. 플랜 파일을 열어 4개 섹션 작성"
Write-Output "    2. 완료되면 REVIEWING 상태로 전이"
Write-Output "    3. approve-plan.ps1으로 사용자에게 승인 요청"
Write-Output ""
