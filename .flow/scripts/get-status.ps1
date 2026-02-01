#!/usr/bin/env pwsh
# Get current Flow status

[CmdletBinding()]
param(
    [switch]$Json,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

if ($Help) {
    Write-Output "Usage: ./get-status.ps1 [-Json] [-Help]"
    exit 0
}

. "$PSScriptRoot/common.ps1"

$phase = Get-CurrentPhase

if ($Json) {
    if ($null -eq $phase) {
        @{ initialized = $true; active = $false; message = "No active context" } | ConvertTo-Json
        exit 0
    }

    $phase | ConvertTo-Json -Depth 10
} else {
    if ($null -eq $phase) {
        Write-Output ""
        Write-Output "==================================="
        Write-Output "  FLOW STATUS"
        Write-Output "==================================="
        Write-Output ""
        Write-Output "  No active context-phase.json found."
        Write-Output "  Start a plan or set FLOW_FEATURE to select a feature."
        Write-Output ""
        Write-Output "==================================="
        exit 0
    }

    Write-Output ""
    Write-Output "==================================="
    Write-Output "  FLOW STATUS"
    Write-Output "==================================="
    Write-Output ""
    Write-Output "  Phase:          $($phase.phase)"
    Write-Output "  Started:        $($phase.started_at)"
    
    $featureName = if ($phase.feature_name) { $phase.feature_name } else { "None" }
    Write-Output "  Feature:        $featureName"
    Write-Output "  Requires Human: $($phase.requires_human)"
    Write-Output "  Retry Count:    $($phase.retry_count) / $($phase.max_retries)"
    Write-Output ""
    Write-Output "  Last Decision:"
    Write-Output "    Action:  $($phase.last_decision.action)"
    Write-Output "    Reason:  $($phase.last_decision.reason)"
    Write-Output "    Time:    $($phase.last_decision.timestamp)"
    Write-Output ""
    Write-Output "==================================="
    
    $nextActions = switch ($phase.phase) {
        "IDLE"       { "/flow.plan - Start new plan" }
        "PLANNING"   { "Writing plan... then move to REVIEWING" }
        "REVIEWING"  { "/flow.approve - Approve | Request changes" }
        "READY"      { "/flow.execute - Start execution" }
        "EXECUTING"  { "Executing... waiting for completion" }
        "VALIDATING" { "Validating..." }
        "RETRYING"   { "Retrying... ($($phase.retry_count)/$($phase.max_retries))" }
        "BLOCKED"    { "!! Human intervention needed | abort-to-idle.ps1" }
        "COMPLETED"  { "Done! Ready for next task" }
        default      { "Unknown state" }
    }
    Write-Output "  Next: $nextActions"
    Write-Output ""
}
