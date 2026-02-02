#!/usr/bin/env pwsh
# 설계 문서 승인 - 상태 머신과 독립적으로 동작

[CmdletBinding()]
param(
    [string]$DesignFile = "",
    [switch]$List,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

if ($Help) {
    Write-Output "Usage: ./approve-design.ps1 [-DesignFile <path>] [-List] [-Help]"
    Write-Output "  -DesignFile  승인할 설계 문서 경로 (생략 시 Draft 문서 자동 검색)"
    Write-Output "  -List        Draft 상태 설계 문서 목록 표시"
    exit 0
}

# 프로젝트 루트 찾기
$flowRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectRoot = (Resolve-Path (Join-Path $flowRoot "..")).Path
$designsDir = Join-Path $projectRoot "docs/flow/implements/designs"

function Write-FlowOutput {
    param(
        [Parameter(Mandatory)]
        [string]$Message,
        [ValidateSet("Info", "Success", "Warning", "Error")]
        [string]$Level = "Info"
    )
    
    $prefix = switch ($Level) {
        "Info"    { "[DESIGN]" }
        "Success" { "[DESIGN OK]" }
        "Warning" { "[DESIGN WARN]" }
        "Error"   { "[DESIGN ERR]" }
    }
    
    $color = switch ($Level) {
        "Info"    { "Cyan" }
        "Success" { "Green" }
        "Warning" { "Yellow" }
        "Error"   { "Red" }
    }
    
    Write-Host "$prefix $Message" -ForegroundColor $color
}

function Get-DraftDesigns {
    if (-not (Test-Path $designsDir)) {
        return ,@()
    }
    
    $drafts = @()
    $files = Get-ChildItem -Path $designsDir -Filter "*.md"
    foreach ($file in $files) {
        $content = Get-Content $file.FullName -Raw -Encoding UTF8
        # Status: Draft 또는 Reviewing 상태인 문서 찾기 (마크다운 볼드 포함)
        if ($content -match "\*{0,2}Status\*{0,2}:\s*(Draft|Reviewing)") {
            $draft = [PSCustomObject]@{
                Path = $file.FullName
                Name = $file.BaseName
                Status = $Matches[1]
            }
            $drafts += $draft
        }
    }
    return ,$drafts
}

function Update-DesignStatus {
    param(
        [string]$FilePath,
        [string]$NewStatus
    )
    
    $content = [System.IO.File]::ReadAllText($FilePath, [System.Text.Encoding]::UTF8)
    # 마크다운 볼드 형태 포함하여 Status 업데이트
    $updated = $content -replace "(\*{0,2}Status\*{0,2}:\s*)(Draft|Reviewing)", "`$1$NewStatus"
    [System.IO.File]::WriteAllText($FilePath, $updated, [System.Text.UTF8Encoding]::new($false))
}

# -List 옵션: Draft 문서 목록 표시
if ($List) {
    $drafts = Get-DraftDesigns
    if ($drafts.Count -eq 0) {
        Write-FlowOutput "승인 대기 중인 설계 문서가 없습니다." -Level Info
    } else {
        Write-Output ""
        Write-Output "═══════════════════════════════════════"
        Write-Output "  승인 대기 중인 설계 문서"
        Write-Output "═══════════════════════════════════════"
        Write-Output ""
        $drafts | ForEach-Object {
            Write-Output "  [$($_.Status)] $($_.Name)"
            Write-Output "         $($_.Path)"
        }
        Write-Output ""
    }
    exit 0
}

# 설계 문서 찾기
$targetDesign = $null

if ($DesignFile) {
    # 명시적으로 지정된 경우
    if (Test-Path $DesignFile) {
        $targetDesign = @{
            Path = (Resolve-Path $DesignFile).Path
            Name = [System.IO.Path]::GetFileNameWithoutExtension($DesignFile)
        }
    } else {
        Write-FlowOutput "파일을 찾을 수 없습니다: $DesignFile" -Level Error
        exit 1
    }
} else {
    # 자동 검색
    $drafts = Get-DraftDesigns
    
    if ($drafts.Count -eq 0) {
        Write-FlowOutput "승인 대기 중인 설계 문서가 없습니다." -Level Warning
        Write-Output ""
        Write-Output "  설계 문서 위치: $designsDir"
        Write-Output "  Status가 'Draft' 또는 'Reviewing'인 문서를 찾습니다."
        exit 0
    }
    
    if ($drafts.Count -eq 1) {
        $targetDesign = $drafts[0]
    } else {
        # 여러 개인 경우 선택
        Write-Output ""
        Write-Output "═══════════════════════════════════════"
        Write-Output "  승인할 설계 문서를 선택하세요"
        Write-Output "═══════════════════════════════════════"
        Write-Output ""
        
        for ($i = 0; $i -lt $drafts.Count; $i++) {
            Write-Output "  [$($i + 1)] $($drafts[$i].Name) ($($drafts[$i].Status))"
        }
        Write-Output ""
        
        $selection = Read-Host "  번호 입력 (1-$($drafts.Count))"
        $index = [int]$selection - 1
        
        if ($index -lt 0 -or $index -ge $drafts.Count) {
            Write-FlowOutput "잘못된 선택입니다." -Level Error
            exit 1
        }
        
        $targetDesign = $drafts[$index]
    }
}

# VS Code로 설계 문서 열기
Write-FlowOutput "설계 문서를 VS Code에서 엽니다..." -Level Info
code $targetDesign.Path

# 사용자 확인
Write-Output ""
Write-Output "═══════════════════════════════════════"
Write-Output "  설계 문서 승인"
Write-Output "═══════════════════════════════════════"
Write-Output ""
Write-Output "  문서: $($targetDesign.Name)"
Write-Output "  경로: $($targetDesign.Path)"
Write-Output ""
Write-Output "  설계 내용을 검토해주세요."
Write-Output ""

$confirm = Read-Host "  승인하시겠습니까? (Y/피드백 입력)"

if ($confirm -eq 'Y' -or $confirm -eq 'y') {
    # 승인 처리
    Update-DesignStatus -FilePath $targetDesign.Path -NewStatus "Approved"
    
    Write-FlowOutput "설계가 승인되었습니다!" -Level Success
    Write-Output ""
    Write-Output "  Status: Draft/Reviewing → Approved"
    Write-Output "  다음 단계: AI가 backlog를 생성합니다."
    Write-Output ""
    Write-Output "  AI에게 다음과 같이 말해주세요:"
    Write-Output "  '설계가 승인되었습니다. backlog를 생성해주세요.'"
    Write-Output ""
} else {
    # 피드백 저장
    $feedbackDir = [System.IO.Path]::GetDirectoryName($targetDesign.Path)
    $feedbackPath = Join-Path $feedbackDir "feedback-$($targetDesign.Name).txt"
    
    $feedbackContent = @"
# 설계 피드백
Date: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Design: $($targetDesign.Name)

## 피드백 내용
$confirm
"@
    
    [System.IO.File]::WriteAllText($feedbackPath, $feedbackContent, [System.Text.UTF8Encoding]::new($false))
    
    # 문서 상태를 Reviewing으로 변경
    Update-DesignStatus -FilePath $targetDesign.Path -NewStatus "Reviewing"
    
    Write-FlowOutput "피드백이 저장되었습니다." -Level Info
    Write-Output ""
    Write-Output "  피드백 파일: $feedbackPath"
    Write-Output "  Status: → Reviewing"
    Write-Output ""
    Write-Output "  AI에게 다음과 같이 말해주세요:"
    Write-Output "  '설계에 피드백이 있습니다. 수정해주세요.'"
    Write-Output ""
}
