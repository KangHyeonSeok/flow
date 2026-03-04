<#
.SYNOPSIS
    SessionStart hook - Injects spec graph context into agent conversation.
.DESCRIPTION
    Copilot Agent Hook: SessionStart
    Runs flow spec-list and spec-graph --tree to inject spec overview as additionalContext.
#>
param()

$ErrorActionPreference = "SilentlyContinue"

# Read stdin (hook input JSON) - use StreamReader for proper UTF-8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$reader = [System.IO.StreamReader]::new([Console]::OpenStandardInput(), [System.Text.Encoding]::UTF8)
$inputJson = $reader.ReadToEnd()
$hookInput = $null
try { $hookInput = $inputJson | ConvertFrom-Json } catch {}

$flowScript = Join-Path $PSScriptRoot "..\..\flow.ps1"

# 세션 시작 시 스펙을 벡터 DB에 인덱싱 (백그라운드 - hook 타임아웃에 영향 없음)
Start-Process pwsh `
    -ArgumentList "-NoProfile", "-NonInteractive", "-Command",
        "& '$flowScript' spec-index 2>`$null" `
    -WindowStyle Hidden `
    -WorkingDirectory (Join-Path $PSScriptRoot "..\..")

# Gather spec context
$specList = ""
$specTree = ""

try {
    $specList = & $flowScript spec-list --pretty 2>$null | Out-String
} catch {}

try {
    $specTree = & $flowScript spec-graph --tree 2>$null | Out-String
} catch {}

# Build additional context
$context = @"
[Flow Spec Graph Context]
현재 프로젝트의 스펙 그래프 상태입니다. 구현 작업 시 참고하세요.

== 스펙 트리 ==
$specTree

== 스펙 목록 (JSON) ==
$specList

[Spec Workflow Rules]
1. 사용자가 구현을 요청하면, 관련 스펙이 있는지 확인하세요.
   - 관련 스펙이 있으면: 해당 스펙의 status를 "active"로 변경하세요 (스펙 JSON 파일 직접 편집).
   - 관련 스펙이 없으면: flow.ps1 spec-create로 새 스펙을 생성하고 status를 "active"로 설정하세요.
2. 구현 완료 후에는:
   - 스펙의 codeRefs에 구현한 파일 경로를 추가하세요.
   - conditions의 status를 적절히 업데이트하세요 (구현 완료된 조건은 "verified").
   - 스펙의 status를 "needs-review"로 변경하세요.
3. 스펙 파일 위치: `$env:USERPROFILE\.flow\specs\flow-spec\specs\{id}.json`
4. 스펙 상태: draft → active → needs-review → verified
"@

# Output JSON
$output = @{
    hookSpecificOutput = @{
        hookEventName    = "SessionStart"
        additionalContext = $context
    }
} | ConvertTo-Json -Depth 5

Write-Output $output
exit 0
