#!/usr/bin/env pwsh
# 로그 기록 on/off 설정

[CmdletBinding()]
param(
    [switch]$On,
    [switch]$Off,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

if ($Help) {
    Write-Output "Usage: ./set-log.ps1 -On | -Off"
    Write-Output "  -On   로그 기록 활성화"
    Write-Output "  -Off  로그 기록 비활성화"
    exit 0
}

if ($On -and $Off) {
    Write-Output "Error: -On과 -Off는 동시에 사용할 수 없습니다."
    exit 1
}

if (-not $On -and -not $Off) {
    Write-Output "Error: -On 또는 -Off 중 하나를 지정해야 합니다."
    exit 1
}

. "$PSScriptRoot/common.ps1"

$settings = Get-FlowSettings

if (-not ($settings.PSObject.Properties.Name -contains "logging")) {
    $settings | Add-Member -NotePropertyName logging -NotePropertyValue @{ enabled = $false }
}

$settings.logging.enabled = [bool]$On

Set-FlowSettings -Settings $settings

$state = if ($settings.logging.enabled) { "on" } else { "off" }
Write-FlowOutput "로그 기록이 $state 로 설정되었습니다." -Level Info
