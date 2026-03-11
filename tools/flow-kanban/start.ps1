$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# node_modules 없으면 설치
if (-not (Test-Path "$root\node_modules")) {
    Write-Host "Installing dependencies..." -ForegroundColor Cyan
    Push-Location $root
    npm install
    Pop-Location
}

# 스펙 디렉터리 없으면 데모 데이터 생성
$specsDir = Join-Path $env:USERPROFILE ".flow\specs"
if (-not (Test-Path $specsDir)) {
    Write-Host "Seeding demo data..." -ForegroundColor Cyan
    node "$root\seed-demo.js"
}

Write-Host "Starting Flow Kanban at http://localhost:3000" -ForegroundColor Green
node "$root\server.js"
