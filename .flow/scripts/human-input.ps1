#!/usr/bin/env pwsh
# 사용자 입력 처리

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("Confirm", "Select", "Text", "Review")]
    [string]$Type,
    
    [string]$Prompt = "",
    [string[]]$Options = @(),
    [int]$TimeoutSeconds = 0,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

if ($Help) {
    Write-Output "Usage: ./human-input.ps1 -Type <type> [-Prompt <text>] [-Options <array>] [-TimeoutSeconds <sec>]"
    Write-Output ""
    Write-Output "Types:"
    Write-Output "  Confirm  - Yes/No 확인 (Y/N)"
    Write-Output "  Select   - 선택지 중 선택 (1,2,3...)"
    Write-Output "  Text     - 자유 텍스트 입력"
    Write-Output "  Review   - 파일 열고 확인 대기"
    exit 0
}

. "$PSScriptRoot/common.ps1"

function Get-UserInput {
    param(
        [string]$PromptText,
        [int]$Timeout = 0
    )
    
    if ($Timeout -gt 0) {
        Write-Output "  ($Timeout 초 후 타임아웃)"
        
        $task = [System.Threading.Tasks.Task]::Factory.StartNew({
            Read-Host
        })
        
        if ($task.Wait([TimeSpan]::FromSeconds($Timeout))) {
            return $task.Result
        } else {
            return $null
        }
    } else {
        return Read-Host $PromptText
    }
}

$result = @{
    type = $Type
    prompt = $Prompt
    response = $null
    timestamp = (Get-Date -Format "o")
    timed_out = $false
}

switch ($Type) {
    "Confirm" {
        Write-Output ""
        Write-Output "════════════"
        Write-Output "  확인 요청"
        Write-Output "════════════"
        Write-Output ""
        if ($Prompt) { Write-Output "  $Prompt" }
        Write-Output ""
        
        $response = Get-UserInput -PromptText "  (Y/N)" -Timeout $TimeoutSeconds
        
        if ($null -eq $response) {
            $result.timed_out = $true
            $result.response = $false
        } else {
            $result.response = ($response -eq 'Y' -or $response -eq 'y')
        }
    }
    
    "Select" {
        Write-Output ""
        Write-Output "════════════"
        Write-Output "  선택 요청"
        Write-Output "════════════"
        Write-Output ""
        if ($Prompt) { Write-Output "  $Prompt" }
        Write-Output ""
        
        for ($i = 0; $i -lt $Options.Count; $i++) {
            Write-Output "  [$($i + 1)] $($Options[$i])"
        }
        Write-Output ""
        
        $response = Get-UserInput -PromptText "  선택 (1-$($Options.Count))" -Timeout $TimeoutSeconds
        
        if ($null -eq $response) {
            $result.timed_out = $true
        } else {
            $index = [int]$response - 1
            if ($index -ge 0 -and $index -lt $Options.Count) {
                $result.response = $Options[$index]
            } else {
                $result.response = $null
            }
        }
    }
    
    "Text" {
        Write-Output ""
        Write-Output "══════════════"
        Write-Output "  텍스트 입력"
        Write-Output "══════════════"
        Write-Output ""
        if ($Prompt) { Write-Output "  $Prompt" }
        Write-Output ""
        
        $result.response = Get-UserInput -PromptText "  입력" -Timeout $TimeoutSeconds
        
        if ($null -eq $result.response) {
            $result.timed_out = $true
        }
    }
    
    "Review" {
        Write-Output ""
        Write-Output "════════════"
        Write-Output "  검토 요청"
        Write-Output "════════════"
        Write-Output ""
        if ($Prompt) { Write-Output "  $Prompt" }
        Write-Output ""
        Write-Output "  검토가 완료되면 Enter를 누르세요."
        Write-Output ""
        
        $null = Read-Host
        $result.response = $true
    }
}

# 결과 출력
$result | ConvertTo-Json -Compress
