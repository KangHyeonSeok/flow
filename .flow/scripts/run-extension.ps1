#!/usr/bin/env pwsh
# 확장 실행 스크립트

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ExtensionId,
    
    [string]$FeatureName,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

if ($Help) {
    Write-Output "Usage: ./run-extension.ps1 -ExtensionId <id> [-FeatureName <name>]"
    Write-Output "  -ExtensionId   실행할 확장 ID (예: STRUCTURE_REVIEW)"
    Write-Output "  -FeatureName   현재 작업 중인 기능 이름"
    exit 0
}

. "$PSScriptRoot/common.ps1"

$phase = Get-CurrentPhase

if (-not $FeatureName) {
    $FeatureName = $phase.feature_name
}

# extensions.json 읽기
$projectRoot = Get-ProjectRoot
$extensionsPath = Join-Path $projectRoot ".flow/extensions.json"

if (-not (Test-Path $extensionsPath)) {
    Write-FlowOutput "확장 파일이 없습니다: $extensionsPath" -Level Error
    exit 1
}

$extensionsConfig = Get-Content $extensionsPath -Raw | ConvertFrom-Json
$extension = $extensionsConfig.extensions.$ExtensionId

if (-not $extension) {
    Write-FlowOutput "확장을 찾을 수 없습니다: $ExtensionId" -Level Error
    exit 1
}

if (-not $extension.enabled) {
    Write-FlowOutput "비활성화된 확장입니다: $ExtensionId" -Level Warning
    exit 0
}

Write-Output ""
Write-Output "📋 확장 실행: $ExtensionId ($($extension.name))"
Write-Output ""
Write-Output "$($extension.description)"
Write-Output ""

# 확장별 실행 로직
switch ($ExtensionId) {
    "STRUCTURE_REVIEW" {
        # 변경된 파일 목록 가져오기
        $featurePath = Join-Path $projectRoot "docs/implements/$FeatureName"
        $planPath = Join-Path $featurePath "plan.md"
        
        if (-not (Test-Path $planPath)) {
            Write-FlowOutput "플랜 파일이 없습니다: $planPath" -Level Warning
            exit 0
        }
        
        # Git 변경 사항 확인
        Push-Location $projectRoot
        $changedFiles = git diff --name-only HEAD 2>$null
        Pop-Location
        
        if (-not $changedFiles) {
            Write-FlowOutput "✅ 리팩토링 제안 없음 - 구조가 적절합니다." -Level Success
            Write-Output "→ COMPLETED 상태로 자동 전이"
            exit 0
        }
        
        Write-Output "🔍 분석 결과:"
        
        # 간단한 분석 (실제로는 더 정교한 분석 가능)
        $suggestions = @()
        
        foreach ($file in $changedFiles) {
            $fullPath = Join-Path $projectRoot $file
            if (Test-Path $fullPath) {
                $content = Get-Content $fullPath -Raw -ErrorAction SilentlyContinue
                if ($content) {
                    $lines = ($content -split "`n").Count
                    
                    # 간단한 체크: 긴 파일
                    if ($lines -gt 200) {
                        $suggestions += @{
                            file = $file
                            issue = "파일 길이 초과"
                            detail = "$lines 줄 → 분리 권장"
                        }
                    }
                }
            }
        }
        
        if ($suggestions.Count -eq 0) {
            Write-FlowOutput "✅ 리팩토링 제안 없음 - 구조가 적절합니다." -Level Success
            Write-Output "→ COMPLETED 상태로 자동 전이"
            
            $result = @{
                has_suggestions = $false
                suggestions = @()
            }
        } else {
            $i = 1
            foreach ($suggestion in $suggestions) {
                Write-Output "$i. [$($suggestion.file)] $($suggestion.issue) ($($suggestion.detail))"
                $i++
            }
            Write-Output ""
            
            $result = @{
                has_suggestions = $true
                suggestions = $suggestions
            }
        }
        
        # 결과를 JSON으로 출력
        $result | ConvertTo-Json -Depth 10
    }
    
    default {
        Write-FlowOutput "확장 실행 로직이 구현되지 않았습니다: $ExtensionId" -Level Warning
        
        $result = @{
            has_suggestions = $false
            suggestions = @()
            message = "확장 실행 로직 미구현"
        }
        
        $result | ConvertTo-Json -Depth 10
    }
}
