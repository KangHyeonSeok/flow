<#
.SYNOPSIS
    Stop hook - Reminds agent to update spec status before finishing.
.DESCRIPTION
    Copilot Agent Hook: Stop
    - Checks if there are specs in "active" status (being worked on).
    - If stop_hook_active is false (first time), blocks and asks agent to update specs.
    - If stop_hook_active is true, allows stop to prevent infinite loop.
#>
param()

$ErrorActionPreference = "SilentlyContinue"

# Read stdin (hook input JSON) - use StreamReader for proper UTF-8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$reader = [System.IO.StreamReader]::new([Console]::OpenStandardInput(), [System.Text.Encoding]::UTF8)
$inputJson = $reader.ReadToEnd()
$hookInput = $null
try { $hookInput = $inputJson | ConvertFrom-Json } catch {}

# Prevent infinite loop
$stopHookActive = $false
if ($hookInput -and $hookInput.stop_hook_active) {
    $stopHookActive = $hookInput.stop_hook_active
}

if ($stopHookActive) {
    # Already ran once - clean up session file and allow stop
    $sessionId = if ($hookInput.sessionId) { $hookInput.sessionId } else { "unknown" }
    $sessionFile = Join-Path $env:TEMP ".flow-hook-session-$sessionId.json"
    if (Test-Path $sessionFile) { Remove-Item $sessionFile -Force }
    $output = @{ continue = $true } | ConvertTo-Json
    Write-Output $output
    exit 0
}

# Read session-tracked spec IDs
$sessionId = if ($hookInput.sessionId) { $hookInput.sessionId } else { "unknown" }
$sessionFile = Join-Path $env:TEMP ".flow-hook-session-$sessionId.json"
$trackedSpecIds = @()
if (Test-Path $sessionFile) {
    try {
        $sessionData = Get-Content $sessionFile -Raw | ConvertFrom-Json
        $trackedSpecIds = @($sessionData.specIds)
    }
    catch {}
}

# If no specs were tracked in this session, allow stop
if ($trackedSpecIds.Count -eq 0) {
    $output = @{ continue = $true } | ConvertTo-Json
    Write-Output $output
    exit 0
}

# Check tracked specs that are still in "active" status
$specDir = Join-Path $env:USERPROFILE ".flow\specs\flow-spec\specs"
$activeSpecs = @()

foreach ($specId in $trackedSpecIds) {
    $specFile = Join-Path $specDir "$specId.json"
    if (Test-Path $specFile) {
        try {
            $spec = Get-Content $specFile -Raw | ConvertFrom-Json
            if ($spec.status -eq "active") {
                $activeSpecs += @{ id = $spec.id; title = $spec.title }
            }
        }
        catch {}
    }
}

if ($activeSpecs.Count -eq 0) {
    # All tracked specs have been updated - allow stop
    $output = @{ continue = $true } | ConvertTo-Json
    Write-Output $output
    exit 0
}

# Build the reason message
$specSummary = ($activeSpecs | ForEach-Object {
    "  - $($_.id): $($_.title)"
}) -join "`n"

$reason = @"
작업을 완료하기 전에 다음 active 상태의 스펙을 업데이트해주세요:
$specSummary

각 스펙에 대해:
1. 구현한 코드 파일 경로를 codeRefs에 추가하세요.
2. 충족된 conditions의 status를 "verified"로, codeRefs를 추가하세요.
3. 구현이 완료되었다면 스펙 status를 "needs-review"로 변경하세요.
4. flow.ps1 spec-validate --id {specId} 로 검증하세요.

스펙 파일 위치: $env:USERPROFILE\.flow\specs\flow-spec\specs\{id}.json
"@

$output = @{
    hookSpecificOutput = @{
        hookEventName = "Stop"
        decision      = "block"
        reason        = $reason
    }
} | ConvertTo-Json -Depth 5

Write-Output $output
exit 0
