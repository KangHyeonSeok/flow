$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# 스펙 디렉터리: ~/.flow/specs/
if (-not $env:FLOW_SPECS_DIR) {
    $env:FLOW_SPECS_DIR = Join-Path $env:USERPROFILE ".flow\specs"
}

# 워크트리 디렉터리: ~/.flow/worktrees/
if (-not $env:FLOW_WORKTREES_DIR) {
    $env:FLOW_WORKTREES_DIR = Join-Path $env:USERPROFILE ".flow\worktrees"
}

# node_modules 없으면 설치
if (-not (Test-Path "$root\node_modules")) {
    Write-Host "Installing dependencies..." -ForegroundColor Cyan
    Push-Location $root
    npm install
    Pop-Location
}

Write-Host "Starting Flow Kanban at http://localhost:3000" -ForegroundColor Green
Write-Host "  Specs dir:     $env:FLOW_SPECS_DIR" -ForegroundColor DarkGray
Write-Host "  Worktrees dir: $env:FLOW_WORKTREES_DIR" -ForegroundColor DarkGray
node "$root\server.js"
