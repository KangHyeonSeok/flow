<#
.SYNOPSIS
    UserPromptSubmit hook - Detects implementation requests and manages spec status.
.DESCRIPTION
    Copilot Agent Hook: UserPromptSubmit
    - Detects implementation-related prompts (구현, 개발, 만들어, 추가 등)
    - If a spec ID (F-XXX) is mentioned and spec is in draft, changes it to active.
    - Shows systemMessage about spec state changes.
#>
param()

$ErrorActionPreference = "SilentlyContinue"

# Always output valid JSON even on unexpected errors
trap {
    $output = @{ hookSpecificOutput = @{ hookEventName = "UserPromptSubmit"; additionalContext = "" } } | ConvertTo-Json -Depth 3
    Write-Output $output
    exit 0
}

try {

# Read stdin (hook input JSON) - use StreamReader for proper UTF-8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$reader = [System.IO.StreamReader]::new([Console]::OpenStandardInput(), [System.Text.Encoding]::UTF8)
$inputJson = $reader.ReadToEnd()
$hookInput = $null
try { $hookInput = $inputJson | ConvertFrom-Json } catch {}

$prompt = ""
if ($hookInput -and $hookInput.prompt) {
    $prompt = $hookInput.prompt
}

# Implementation-related keywords
$implKeywords = @(
    '구현', '개발', '만들어', '추가해', '작성해', '빌드', '코딩',
    '기능.*추가', '기능.*개발', '기능.*구현',
    'implement', 'develop', 'build', 'create', 'add feature', 'code'
)

$isImplRequest = $false
foreach ($kw in $implKeywords) {
    if ($prompt -match $kw) {
        $isImplRequest = $true
        break
    }
}

if (-not $isImplRequest) {
    # Not an implementation request - pass through silently
    $output = @{ hookSpecificOutput = @{ hookEventName = "UserPromptSubmit"; additionalContext = "" } } | ConvertTo-Json -Depth 3
    Write-Output $output
    exit 0
}

# ─── 구현 요청: 백그라운드로 스펙 자동 업데이트 시작 ─────────────────────
# 프롬프트를 임시 파일에 저장 후 spec-auto-update.ps1 을 완전히 detached 프로세스로 실행.
# hook 자체는 즉시 리턴한다.
$promptTempFile = Join-Path $env:TEMP ".flow-spec-prompt-$(Get-Date -Format 'yyyyMMddHHmmssff').txt"
$prompt | Set-Content $promptTempFile -Encoding UTF8

$autoUpdateScript = Join-Path $PSScriptRoot "spec-auto-update.ps1"
if (Test-Path $autoUpdateScript) {
    Start-Process pwsh `
        -ArgumentList "-NoProfile", "-NonInteractive", "-File", $autoUpdateScript, "-PromptFile", $promptTempFile `
        -WindowStyle Hidden `
        -WorkingDirectory (Join-Path $PSScriptRoot "..\..")
}

# Check if a spec ID is mentioned in the prompt
$specIdPattern = 'F-\d{3}'
$specIds = [regex]::Matches($prompt, $specIdPattern) | ForEach-Object { $_.Value } | Select-Object -Unique

$messages = @()
$specDir = Join-Path $env:USERPROFILE ".flow\specs\flow-spec\specs"

foreach ($specId in $specIds) {
    $specFile = Join-Path $specDir "$specId.json"
    if (Test-Path $specFile) {
        try {
            $spec = Get-Content $specFile -Raw | ConvertFrom-Json
            if ($spec.status -eq "draft") {
                # Change draft -> active
                $spec.status = "active"
                $spec.updatedAt = (Get-Date).ToUniversalTime().ToString("o")
                $spec | ConvertTo-Json -Depth 10 | Set-Content $specFile -Encoding UTF8
                $messages += "[$specId] '$($spec.title)' 스펙을 active(진행 중) 상태로 변경했습니다."
            }
            elseif ($spec.status -eq "active") {
                $messages += "[$specId] '$($spec.title)' 스펙은 이미 active(진행 중) 상태입니다."
            }
            else {
                $messages += "[$specId] '$($spec.title)' 스펙 현재 상태: $($spec.status)"
            }
        }
        catch {
            $messages += "[$specId] 스펙 파일 읽기 실패: $_"
        }
    }
    else {
        $messages += "[$specId] 스펙 파일이 존재하지 않습니다."
    }
}

# Track activated/referenced spec IDs in session file
$sessionId = if ($hookInput.sessionId) { $hookInput.sessionId } else { "unknown" }
$sessionFile = Join-Path $env:TEMP ".flow-hook-session-$sessionId.json"
$sessionData = @{ specIds = @() }
if (Test-Path $sessionFile) {
    try { $sessionData = Get-Content $sessionFile -Raw | ConvertFrom-Json } catch {}
}
$allTrackedIds = @($sessionData.specIds) + @($specIds) | Select-Object -Unique
$sessionData = @{ specIds = @($allTrackedIds) }
$sessionData | ConvertTo-Json -Depth 5 | Set-Content $sessionFile -Encoding UTF8

if ($specIds.Count -eq 0) {
    $messages += "구현 요청이 감지되었습니다. 백그라운드에서 관련 스펙을 벡터 검색하여 자동 업데이트/생성 중입니다."
} else {
    $messages += "백그라운드에서 관련 스펙 벡터 검색 및 자동 업데이트도 병행 실행 중입니다."
}

$additionalContext = "[Flow Spec Hook] " + ($messages -join "`n")

$output = @{
    hookSpecificOutput = @{
        hookEventName    = "UserPromptSubmit"
        additionalContext = $additionalContext
    }
} | ConvertTo-Json -Depth 5

Write-Output $output
exit 0

} catch {
    $output = @{ hookSpecificOutput = @{ hookEventName = "UserPromptSubmit"; additionalContext = "" } } | ConvertTo-Json -Depth 3
    Write-Output $output
    exit 0
}
