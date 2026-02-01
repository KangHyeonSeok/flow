#!/usr/bin/env pwsh
# Flow Common Functions

$ErrorActionPreference = 'Stop'

# PowerShell 버전 체크 및 경고
function Test-PowerShellVersion {
    $minVersion = [Version]"7.0.0"
    $currentVersion = $PSVersionTable.PSVersion
    
    if ($currentVersion -lt $minVersion) {
        Write-Host ""
        Write-Host "  ⚠️  경고: PowerShell $currentVersion 사용 중" -ForegroundColor Yellow
        Write-Host "  인코딩 문제가 발생할 수 있습니다." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  PowerShell 7로 업그레이드하려면:" -ForegroundColor White
        Write-Host "    cd .flow/scripts; ./ensure-pwsh7.ps1" -ForegroundColor Cyan
        Write-Host ""
        return $false
    }
    return $true
}

# 스크립트 시작 시 버전 체크 (경고만, 중단하지 않음)
$null = Test-PowerShellVersion

function Get-FlowRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Get-ContextDir {
    return Join-Path (Get-FlowRoot) "context"
}

function Get-LogsDir {
    return Join-Path (Get-FlowRoot) "logs"
}

function Get-PlansDir {
    return Join-Path (Get-FlowRoot) "plans"
}

function Get-DocsDir {
    $root = Get-FlowRoot
    return (Resolve-Path (Join-Path $root "../docs")).Path
}

function Get-FeatureDir {
    param([string]$FeatureName)
    $docsDir = Get-DocsDir
    return Join-Path $docsDir "implements/$FeatureName"
}

function ConvertTo-FeatureName {
    param([string]$Title)
    # 한글/영문 타이틀을 파일명으로 변환 (snake_case)
    $name = $Title -replace '[^\w\s\uAC00-\uD7AF]', '' # 특수문자 제거
    $name = $name -replace '\s+', '_' # 공백을 언더스코어로
    $name = $name.ToLower()
    return $name
}

function Get-CurrentPhaseFile {
    return Join-Path (Get-ContextDir) "current-phase.json"
}

function Get-DecisionLogFile {
    return Join-Path (Get-LogsDir) "decisions.jsonl"
}

function Get-CurrentPhase {
    $phaseFile = Get-CurrentPhaseFile
    if (Test-Path $phaseFile) {
        return Get-Content $phaseFile -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    return $null
}

function Set-CurrentPhase {
    param(
        [Parameter(Mandatory)]
        [string]$Phase,
        
        [string]$Reason = "",
        [string]$FeatureName = $null,
        [bool]$RequiresHuman = $false,
        [Nullable[int]]$RetryCount = $null,
        [Nullable[int]]$MaxRetries = $null
    )
    
    $phaseFile = Get-CurrentPhaseFile
    $contextDir = Get-ContextDir
    
    if (-not (Test-Path $contextDir)) {
        New-Item -ItemType Directory -Path $contextDir -Force | Out-Null
    }
    
    # Backup previous state
    if (Test-Path $phaseFile) {
        $backupDir = Join-Path (Get-LogsDir) "backups"
        if (-not (Test-Path $backupDir)) {
            New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        }
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        Copy-Item $phaseFile (Join-Path $backupDir "phase-$timestamp.json") -Force
    }
    
    $resolvedRetryCount = if ($null -ne $RetryCount) { $RetryCount } else { 0 }
    $resolvedMaxRetries = if ($null -ne $MaxRetries) { $MaxRetries } else { 5 }

    $newPhase = @{
        phase = $Phase
        started_at = (Get-Date -Format "o")
        feature_name = $FeatureName
        pending_questions = @()
        last_decision = @{
            action = "phase_transition"
            reason = $Reason
            timestamp = (Get-Date -Format "o")
        }
        requires_human = $RequiresHuman
        retry_count = $resolvedRetryCount
        max_retries = $resolvedMaxRetries
    }
    
    $newPhase | ConvertTo-Json -Depth 10 | Out-File $phaseFile -Encoding UTF8
    
    Add-DecisionLog -PhaseFrom "" -PhaseTo $Phase -Reason $Reason
    
    return $newPhase
}

function Add-DecisionLog {
    param(
        [string]$PhaseFrom,
        [string]$PhaseTo,
        [string]$Reason,
        [string]$Trigger = "manual"
    )
    
    $logFile = Get-DecisionLogFile
    $logsDir = Get-LogsDir
    
    if (-not (Test-Path $logsDir)) {
        New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
    }
    
    $logEntry = @{
        timestamp = (Get-Date -Format "o")
        phase_from = $PhaseFrom
        phase_to = $PhaseTo
        reason = $Reason
        trigger = $Trigger
    }
    
    # UTF-8 without BOM으로 저장 (한글 인코딩 문제 해결)
    $json = $logEntry | ConvertTo-Json -Compress
    [System.IO.File]::AppendAllText($logFile, "$json`n", [System.Text.UTF8Encoding]::new($false))
}

function Test-ValidTransition {
    param(
        [string]$From,
        [string]$To
    )
    
    $validTransitions = @{
        "IDLE" = @("PLANNING")
        "PLANNING" = @("REVIEWING", "IDLE")
        "REVIEWING" = @("READY", "PLANNING", "IDLE")
        "READY" = @("EXECUTING", "REVIEWING", "IDLE")
        "EXECUTING" = @("VALIDATING", "BLOCKED")
        "VALIDATING" = @("COMPLETED", "RETRYING", "BLOCKED")
        "RETRYING" = @("EXECUTING", "BLOCKED")
        "BLOCKED" = @("IDLE", "PLANNING")
        "COMPLETED" = @("IDLE")
    }
    
    if ($validTransitions.ContainsKey($From)) {
        return $validTransitions[$From] -contains $To
    }
    return $false
}

function Add-RetryCount {
    $phase = Get-CurrentPhase
    if ($null -eq $phase) { return $null }
    
    $phase.retry_count++
    $phaseFile = Get-CurrentPhaseFile
    $phase | ConvertTo-Json -Depth 10 | Out-File $phaseFile -Encoding UTF8
    
    return $phase.retry_count
}

function Test-CanRetry {
    $phase = Get-CurrentPhase
    if ($null -eq $phase) { return $false }
    return $phase.retry_count -lt $phase.max_retries
}

function Write-FlowOutput {
    param(
        [Parameter(Mandatory)]
        [string]$Message,
        
        [ValidateSet("Info", "Success", "Warning", "Error")]
        [string]$Level = "Info"
    )
    
    $prefix = switch ($Level) {
        "Info"    { "[FLOW]" }
        "Success" { "[FLOW OK]" }
        "Warning" { "[FLOW WARN]" }
        "Error"   { "[FLOW ERR]" }
    }
    
    $color = switch ($Level) {
        "Info"    { "Cyan" }
        "Success" { "Green" }
        "Warning" { "Yellow" }
        "Error"   { "Red" }
    }
    
    Write-Host "$prefix $Message" -ForegroundColor $color
}

function Test-FlowInitialized {
    $phaseFile = Get-CurrentPhaseFile
    return Test-Path $phaseFile
}

function Initialize-Flow {
    $contextDir = Get-ContextDir
    $logsDir = Get-LogsDir
    $plansDir = Get-PlansDir
    $docsDir = Get-DocsDir
    
    @($contextDir, $logsDir, $plansDir, $docsDir) | ForEach-Object {
        if (-not (Test-Path $_)) {
            New-Item -ItemType Directory -Path $_ -Force | Out-Null
        }
    }
    
    Set-CurrentPhase -Phase "IDLE" -Reason "System initialized"
    
    Write-FlowOutput "Flow initialized" -Level Success
}
