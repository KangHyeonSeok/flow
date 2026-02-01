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

function Get-ProjectRoot {
    $flowRoot = Get-FlowRoot
    return (Resolve-Path (Join-Path $flowRoot "..")).Path
}

function Get-FlowSettingsPath {
    return Join-Path (Get-FlowRoot) "settings.json"
}

function Get-FlowSettings {
    $default = [ordered]@{
        logging = @{ enabled = $false }
    }

    $settingsPath = Get-FlowSettingsPath
    if (-not (Test-Path $settingsPath)) {
        return $default
    }

    try {
        $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    } catch {
        return $default
    }

    if (-not $settings) {
        return $default
    }

    if (-not ($settings.PSObject.Properties.Name -contains "logging")) {
        $settings | Add-Member -NotePropertyName logging -NotePropertyValue @{ enabled = $false }
    }

    if ($null -eq $settings.logging.enabled) {
        $settings.logging.enabled = $false
    }

    return $settings
}

function Set-FlowSettings {
    param(
        [Parameter(Mandatory)]
        $Settings
    )

    $settingsPath = Get-FlowSettingsPath
    $Settings | ConvertTo-Json -Depth 10 | Out-File $settingsPath -Encoding UTF8
}

function Get-ContextDir {
    return Join-Path (Get-FlowRoot) "context"
}

function Get-LogsDir {
    param(
        [string]$FeatureName
    )

    if (-not $FeatureName) {
        $FeatureName = Resolve-ActiveFeatureName -Silent
    }

    if (-not $FeatureName) {
        return $null
    }

    $featureDir = Get-FeatureDir -FeatureName $FeatureName
    return Join-Path $featureDir "logs"
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

function Get-LegacyPhaseFile {
    return Join-Path (Get-ContextDir) "current-phase.json"
}

function Get-FeatureContextFile {
    param([string]$FeatureName)
    if (-not $FeatureName) { return $null }
    $featureDir = Get-FeatureDir -FeatureName $FeatureName
    return Join-Path $featureDir "context-phase.json"
}

function Resolve-ActiveFeatureName {
    param(
        [switch]$Silent
    )

    $preferred = $env:FLOW_FEATURE
    if ($preferred) {
        $preferredDir = Get-FeatureDir -FeatureName $preferred
        if (Test-Path $preferredDir) {
            return $preferred
        }
    }

    $implementsDir = Join-Path (Get-DocsDir) "implements"
    if (-not (Test-Path $implementsDir)) {
        return $null
    }

    $contextFiles = Get-ChildItem -Path $implementsDir -Filter "context-phase.json" -Recurse -File -ErrorAction SilentlyContinue
    if (-not $contextFiles -or $contextFiles.Count -eq 0) {
        return $null
    }

    $selected = $contextFiles | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $featureName = Split-Path $selected.DirectoryName -Leaf

    if ($contextFiles.Count -gt 1 -and -not $Silent) {
        Write-FlowOutput "Multiple context-phase.json found. Using latest: $featureName" -Level Warning
    }

    return $featureName
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
    param(
        [string]$FeatureName,
        [switch]$EnsureFeatureDir
    )

    if (-not $FeatureName) {
        $FeatureName = Resolve-ActiveFeatureName -Silent
    }

    if (-not $FeatureName) {
        return $null
    }

    $featureDir = Get-FeatureDir -FeatureName $FeatureName
    if ($EnsureFeatureDir -and -not (Test-Path $featureDir)) {
        New-Item -ItemType Directory -Path $featureDir -Force | Out-Null
    }

    return Join-Path $featureDir "context-phase.json"
}

function Get-DecisionLogFile {
    param(
        [string]$FeatureName
    )

    $logsDir = Get-LogsDir -FeatureName $FeatureName
    if (-not $logsDir) {
        return $null
    }

    return Join-Path $logsDir "decisions.jsonl"
}

function Get-CurrentPhase {
    param(
        [string]$FeatureName
    )

    $phaseFile = Get-CurrentPhaseFile -FeatureName $FeatureName
    if ($phaseFile -and (Test-Path $phaseFile)) {
        return Get-Content $phaseFile -Raw -Encoding UTF8 | ConvertFrom-Json
    }

    $legacyFile = Get-LegacyPhaseFile
    if (Test-Path $legacyFile) {
        $legacyPhase = Get-Content $legacyFile -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($legacyPhase -and $legacyPhase.feature_name) {
            $migratedFile = Get-CurrentPhaseFile -FeatureName $legacyPhase.feature_name -EnsureFeatureDir
            $legacyPhase | ConvertTo-Json -Depth 10 | Out-File $migratedFile -Encoding UTF8
            Write-FlowOutput "Legacy phase migrated to feature context: $($legacyPhase.feature_name)" -Level Warning
            return $legacyPhase
        }
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
        [Nullable[int]]$MaxRetries = $null,
        [hashtable]$Backlog = $null
    )
    
    $resolvedFeatureName = $FeatureName
    if (-not $resolvedFeatureName) {
        $existingPhase = Get-CurrentPhase
        if ($existingPhase -and $existingPhase.feature_name) {
            $resolvedFeatureName = $existingPhase.feature_name
        } else {
            $resolvedFeatureName = Resolve-ActiveFeatureName -Silent
        }
    }

    if (-not $resolvedFeatureName) {
        Write-FlowOutput "Feature name is required to set phase." -Level Error
        throw "Missing feature name"
    }

    $phaseFile = Get-CurrentPhaseFile -FeatureName $resolvedFeatureName -EnsureFeatureDir
    $existingPhase = Get-CurrentPhase -FeatureName $resolvedFeatureName
    
    # Backup previous state
    if (Test-Path $phaseFile) {
        $backupDir = Join-Path (Get-LogsDir -FeatureName $resolvedFeatureName) "backups"
        if (-not (Test-Path $backupDir)) {
            New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        }
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        Copy-Item $phaseFile (Join-Path $backupDir "phase-$timestamp.json") -Force
    }
    
    $resolvedRetryCount = if ($null -ne $RetryCount) { $RetryCount } else { 0 }
    $resolvedMaxRetries = if ($null -ne $MaxRetries) { $MaxRetries } else { 5 }

    $resolvedBacklog = $null
    if ($PSBoundParameters.ContainsKey("Backlog")) {
        $resolvedBacklog = $Backlog
    } elseif ($existingPhase -and ($existingPhase.PSObject.Properties.Name -contains "backlog")) {
        $resolvedBacklog = $existingPhase.backlog
    }

    $newPhase = @{
        phase = $Phase
        started_at = (Get-Date -Format "o")
        feature_name = $resolvedFeatureName
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

    if ($resolvedBacklog) {
        $newPhase.backlog = $resolvedBacklog
    }
    
    $newPhase | ConvertTo-Json -Depth 10 | Out-File $phaseFile -Encoding UTF8
    
    Add-DecisionLog -PhaseFrom "" -PhaseTo $Phase -Reason $Reason -FeatureName $resolvedFeatureName
    
    return $newPhase
}

function Add-DecisionLog {
    param(
        [string]$PhaseFrom,
        [string]$PhaseTo,
        [string]$Reason,
        [string]$Trigger = "manual",
        [string]$FeatureName
    )

    $settings = Get-FlowSettings
    if (-not $settings.logging.enabled) {
        return
    }

    if (-not $FeatureName) {
        $FeatureName = Resolve-ActiveFeatureName -Silent
    }

    $logFile = Get-DecisionLogFile -FeatureName $FeatureName
    $logsDir = Get-LogsDir -FeatureName $FeatureName

    if (-not $logFile -or -not $logsDir) {
        return
    }

    if (-not (Test-Path $logsDir)) {
        New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
    }
    
    $logEntry = @{
        timestamp = (Get-Date -Format "o")
        phase_from = $PhaseFrom
        phase_to = $PhaseTo
        reason = $Reason
        trigger = $Trigger
        feature_name = $FeatureName
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
    $phaseFile = Get-CurrentPhaseFile -FeatureName $phase.feature_name
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
    $flowRoot = Get-FlowRoot
    return Test-Path $flowRoot
}

function Initialize-Flow {
    $plansDir = Get-PlansDir
    $docsDir = Get-DocsDir

    @($plansDir, $docsDir) | ForEach-Object {
        if (-not (Test-Path $_)) {
            New-Item -ItemType Directory -Path $_ -Force | Out-Null
        }
    }

    Write-FlowOutput "Flow initialized (no global context)" -Level Success
}
