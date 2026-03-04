<#
.SYNOPSIS
    spec-auto-update.ps1 - 백그라운드에서 프롬프트와 관련된 스펙을 업데이트하거나 생성합니다.
.DESCRIPTION
    UserPromptSubmit hook에 의해 백그라운드로 실행됩니다.
    1. 프롬프트 임시 파일에서 사용자 입력 읽기
    2. RAG 벡터 DB에서 관련 스펙 검색 (임베딩 유사도)
    3. copilot -p 로 스펙 업데이트 또는 신규 생성 요청
    4. flow spec-index 로 벡터 DB 갱신
.PARAMETER PromptFile
    사용자 프롬프트가 저장된 임시 파일 경로
.PARAMETER Model
    copilot 호출에 사용할 AI 모델 (기본값: gpt-5-mini)
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$PromptFile,

    [string]$Model = "gpt-5-mini"
)

$ErrorActionPreference = "SilentlyContinue"

$flowScript = Join-Path $PSScriptRoot "..\..\flow.ps1"
$logFile = Join-Path $env:TEMP ".flow-spec-auto-update.log"

function Write-Log {
    param([string]$Message)
    $timestamp = (Get-Date).ToString("HH:mm:ss")
    Add-Content -Path $logFile -Value "[$timestamp] $Message" -Encoding UTF8
}

Write-Log "=== spec-auto-update 시작 ==="

# 1. 프롬프트 읽기
if (-not (Test-Path $PromptFile)) {
    Write-Log "ERROR: 프롬프트 파일 없음: $PromptFile"
    exit 1
}

$userPrompt = Get-Content $PromptFile -Raw -Encoding UTF8
Remove-Item $PromptFile -Force -ErrorAction SilentlyContinue
Write-Log "프롬프트: $($userPrompt.Substring(0, [Math]::Min(100, $userPrompt.Length)))..."

# 2. RAG DB에서 관련 스펙 검색
Write-Log "벡터 DB에서 관련 스펙 검색 중..."
$queryResult = $null
try {
    $queryJson = & $flowScript db-query --query $userPrompt --tags "spec" --top 5 2>$null | Out-String
    $queryResult = $queryJson | ConvertFrom-Json
} catch {
    Write-Log "ERROR: db-query 실패: $_"
}

$relatedSpecs = @()
if ($queryResult -and $queryResult.data -and $queryResult.data.results) {
    foreach ($r in $queryResult.data.results) {
        $featureName = $r.feature_name
        if ($featureName -match '^spec:(.+)$') {
            $specId = $Matches[1]
            $relatedSpecs += @{
                id      = $specId
                content = $r.content
            }
        }
    }
}
Write-Log "관련 스펙 후보: $($relatedSpecs.Count)건"

# 3. 관련 스펙의 실제 파일 경로 탐색 (상세 내용 보완용)
$specDirs = @(
    (Join-Path $env:USERPROFILE ".flow\specs\flow-spec\specs"),
    (Join-Path $PSScriptRoot "..\..\docs\specs")
)

$specDetails = @()
foreach ($spec in $relatedSpecs) {
    $specFile = $null
    foreach ($dir in $specDirs) {
        $candidate = Join-Path $dir "$($spec.id).json"
        if (Test-Path $candidate) { $specFile = $candidate; break }
    }

    if ($specFile) {
        try {
            $json = Get-Content $specFile -Raw -Encoding UTF8 | ConvertFrom-Json
            $specDetails += @{
                id       = $spec.id
                title    = $json.title
                status   = $json.status
                filePath = $specFile
                summary  = $spec.content
            }
        } catch {
            $specDetails += @{
                id       = $spec.id
                title    = "(파싱 실패)"
                status   = "unknown"
                filePath = $specFile
                summary  = $spec.content
            }
        }
    } else {
        $specDetails += @{
            id       = $spec.id
            title    = "(파일 없음)"
            status   = "unknown"
            filePath = ""
            summary  = $spec.content
        }
    }
}

# 4. copilot 프롬프트 구성
$specSection = if ($specDetails.Count -gt 0) {
    $lines = $specDetails | ForEach-Object {
        $fileLine = if ($_.filePath) { "`n파일: $($_.filePath)" } else { "" }
        "### $($_.id): $($_.title) (상태: $($_.status))$fileLine`n$($_.summary)"
    }
    "## 관련 스펙 후보 (벡터 유사도 높은 순)`n`n" + ($lines -join "`n`n")
} else {
    "## 관련 스펙 후보`n관련 스펙이 없습니다."
}

$specDirList = ($specDirs | Where-Object { Test-Path $_ }) -join " 또는 "

$copilotPrompt = @"
다음 사용자 요청을 분석하여 관련 스펙을 업데이트하거나 새 스펙을 생성하세요.
이 요청은 메인 에이전트와 독립적으로 실행되는 스펙 관리 자동화 작업입니다.

## 사용자 요청
$userPrompt

$specSection

## 지침
0. 사용자 요청이 소프트웨어 기능 구현/변경/추가와 무관한 경우 (질문, 설명 요청, 현황 조회 등):
   - 아무것도 하지 마세요. 파일 편집도, 명령 실행도 하지 않습니다.

1. 사용자 요청이 구현/기능과 관련이 있고, 위 스펙 후보 중 관련도가 높은 스펙이 있으면:
   - 해당 스펙 JSON 파일을 직접 편집하여 title, description, conditions 등을 보완하세요.
   - 동일한 기능을 다루는 스펙이면 status를 "active"로 변경하세요.

2. 구현/기능과 관련이 있지만 관련 스펙이 없거나 관련도가 너무 낮으면 (완전히 다른 기능):
   - 다음 명령으로 새 스펙을 생성하세요:
     pwsh -NoProfile -File flow.ps1 spec-create --title "<제목>" --description "<설명>" --status active
   - 스펙 파일 위치: $specDirList

3. 1번 또는 2번 작업을 수행한 경우에만 반드시 실행:
   pwsh -NoProfile -File flow.ps1 spec-index

4. 불필요한 설명 없이 파일 편집 또는 명령 실행만 수행하세요.
"@

# copilot 프롬프트를 임시 파일에 저장 (특수문자 안전 처리)
$copilotPromptFile = Join-Path $env:TEMP ".flow-copilot-spec-prompt-$(Get-Date -Format 'yyyyMMddHHmmss').txt"
$copilotPrompt | Set-Content $copilotPromptFile -Encoding UTF8

Write-Log "copilot -p 호출 중 (모델: $Model)..."

# 5. copilot -p 실행
try {
    $promptText = Get-Content $copilotPromptFile -Raw -Encoding UTF8
    Remove-Item $copilotPromptFile -Force -ErrorAction SilentlyContinue

    & copilot -p $promptText `
        --model $Model `
        --allow-all `
        --yolo 2>&1 | Tee-Object -Append $logFile

    Write-Log "copilot 실행 완료"
} catch {
    Write-Log "ERROR: copilot 실행 실패: $_"
}

Write-Log "=== spec-auto-update 완료 ==="
