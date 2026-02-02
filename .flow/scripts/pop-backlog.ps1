#!/usr/bin/env pwsh
# 백로그 큐에서 다음 작업을 가져와서 implements로 이동

[CmdletBinding()]
param(
    [switch]$Preview,  # 이동하지 않고 다음 작업만 확인
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

if ($Help) {
    Write-Output "Usage: ./pop-backlog.ps1 [-Preview] [-Help]"
    Write-Output "  -Preview  이동하지 않고 다음 작업만 확인"
    Write-Output ""
    Write-Output "Output (JSON):"
    Write-Output "  status: success | empty | error"
    Write-Output "  feature_name: 기능 이름"
    Write-Output "  plan_type: reviewed | need-review"
    exit 0
}

. "$PSScriptRoot/common.ps1"

# Get-BacklogsDir is now defined in common.ps1

function Get-QueueFile {
    return Join-Path (Get-BacklogsDir) "queue"
}

function Write-JsonOutput {
    param(
        [string]$Status,
        [string]$FeatureName = "",
        [string]$PlanType = "",
        [string]$Message = ""
    )
    
    $output = @{
        status = $Status
        feature_name = $FeatureName
        plan_type = $PlanType
        message = $Message
    }
    
    $output | ConvertTo-Json -Compress
}

# 큐 파일 확인
$queueFile = Get-QueueFile
if (-not (Test-Path $queueFile)) {
    Write-JsonOutput -Status "error" -Message "Queue file not found: $queueFile"
    exit 1
}

# 큐 내용 읽기
$queueLines = @(Get-Content $queueFile -Encoding UTF8 | Where-Object { $_ -and $_.Trim() -ne "" })

if ($queueLines.Count -eq 0) {
    Write-JsonOutput -Status "empty" -Message "Queue is empty"
    exit 0
}

# 첫 번째 항목 가져오기
$nextFeature = $queueLines[0].Trim()
$backlogsDir = Get-BacklogsDir
$featureSourceDir = Join-Path $backlogsDir $nextFeature

# 소스 폴더 존재 확인
if (-not (Test-Path $featureSourceDir)) {
    Write-JsonOutput -Status "error" -Message "Feature folder not found: $featureSourceDir"
    exit 1
}

# 플랜 타입 확인
$planPath = Join-Path $featureSourceDir "plan.md"
$needReviewPlanPath = Join-Path $featureSourceDir "need-review-plan.md"

$planType = ""
if (Test-Path $planPath) {
    $planType = "reviewed"
} elseif (Test-Path $needReviewPlanPath) {
    $planType = "need-review"
} else {
    Write-JsonOutput -Status "error" -Message "No plan file found in: $featureSourceDir"
    exit 1
}

# Preview 모드면 여기서 종료
if ($Preview) {
    Write-JsonOutput -Status "success" -FeatureName $nextFeature -PlanType $planType -Message "Preview mode"
    exit 0
}

# 대상 폴더 확인
$implementsDir = Get-FeatureDir -FeatureName $nextFeature
if (Test-Path $implementsDir) {
    Write-JsonOutput -Status "error" -Message "Target folder already exists: $implementsDir"
    exit 1
}

# 폴더 이동
try {
    Move-Item -Path $featureSourceDir -Destination $implementsDir -Force
    
    # logs 및 backups 디렉토리 생성 (없을 경우)
    $logsDir = Join-Path $implementsDir "logs"
    $backupsDir = Join-Path $logsDir "backups"
    if (-not (Test-Path $logsDir)) {
        New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
    }
    if (-not (Test-Path $backupsDir)) {
        New-Item -ItemType Directory -Path $backupsDir -Force | Out-Null
    }
    
    # need-review-plan.md → plan.md 이름 변경 (필요시)
    if ($planType -eq "need-review") {
        $srcPlan = Join-Path $implementsDir "need-review-plan.md"
        $dstPlan = Join-Path $implementsDir "plan.md"
        if (Test-Path $srcPlan) {
            Move-Item -Path $srcPlan -Destination $dstPlan -Force
        }
    }
    
    # 큐에서 첫 번째 항목 제거
    $remainingQueue = $queueLines | Select-Object -Skip 1
    if ($remainingQueue -and $remainingQueue.Count -gt 0) {
        $remainingQueue | Set-Content $queueFile -Encoding UTF8
    } else {
        # queue가 비면 queue 파일과 queue-rationale.md 삭제
        if (Test-Path $queueFile) {
            Remove-Item -Path $queueFile -Force
        }
        $rationaleFile = Join-Path (Get-BacklogsDir) "queue-rationale.md"
        if (Test-Path $rationaleFile) {
            Remove-Item -Path $rationaleFile -Force
        }
    }
    
    # 기능별 context-phase.json 업데이트 (백로그 작업 메타 포함)
    $targetPhase = if ($planType -eq "reviewed") { "EXECUTING" } else { "PLANNING" }
    $backlogInfo = @{
        is_backlog = $true
        active = $true
        source = "backlog-queue"
        started_at = (Get-Date -Format "o")
        completed_at = $null
        completed_reason = ""
    }
    Set-CurrentPhase -Phase $targetPhase -Reason "Popped from backlog: $nextFeature" -FeatureName $nextFeature -Backlog $backlogInfo
    
    Write-JsonOutput -Status "success" -FeatureName $nextFeature -PlanType $planType -Message "Moved to implements"
    
} catch {
    Write-JsonOutput -Status "error" -Message "Failed to move folder: $_"
    exit 1
}
