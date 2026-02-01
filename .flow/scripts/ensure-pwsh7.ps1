#!/usr/bin/env pwsh
# PowerShell 7 설치 확인 및 자동 업그레이드

[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$Help
)

if ($Help) {
    Write-Output "Usage: ./ensure-pwsh7.ps1 [-Force] [-Help]"
    Write-Output "  -Force  강제 재설치"
    exit 0
}

$minVersion = [Version]"7.0.0"
$currentVersion = $PSVersionTable.PSVersion

Write-Host ""
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  PowerShell Version Check" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "  현재 버전: $currentVersion" -ForegroundColor White
Write-Host "  필요 버전: $minVersion 이상" -ForegroundColor White
Write-Host ""

if ($currentVersion -ge $minVersion -and -not $Force) {
    Write-Host "  ✅ PowerShell 7 이상입니다. 업그레이드가 필요하지 않습니다." -ForegroundColor Green
    Write-Host ""
    exit 0
}

# PowerShell 5.x인 경우
Write-Host "  ⚠️  PowerShell $currentVersion은 인코딩 문제가 있을 수 있습니다." -ForegroundColor Yellow
Write-Host ""

# winget 확인
$wingetAvailable = Get-Command winget -ErrorAction SilentlyContinue

if (-not $wingetAvailable) {
    Write-Host "  ❌ winget이 설치되어 있지 않습니다." -ForegroundColor Red
    Write-Host ""
    Write-Host "  수동 설치 방법:" -ForegroundColor White
    Write-Host "    1. https://github.com/PowerShell/PowerShell/releases 방문" -ForegroundColor Gray
    Write-Host "    2. 최신 .msi 파일 다운로드 및 설치" -ForegroundColor Gray
    Write-Host ""
    exit 1
}

Write-Host "  PowerShell 7을 설치하시겠습니까? (Y/N): " -ForegroundColor Yellow -NoNewline
$confirm = Read-Host

if ($confirm -ne 'Y' -and $confirm -ne 'y') {
    Write-Host ""
    Write-Host "  설치가 취소되었습니다." -ForegroundColor Yellow
    Write-Host "  인코딩 문제가 발생하면 다시 실행해주세요." -ForegroundColor Gray
    Write-Host ""
    exit 0
}

Write-Host ""
Write-Host "  PowerShell 7 설치 중..." -ForegroundColor Cyan

try {
    winget install Microsoft.PowerShell --accept-source-agreements --accept-package-agreements
    
    Write-Host ""
    Write-Host "  ✅ PowerShell 7 설치 완료!" -ForegroundColor Green
    Write-Host ""
    Write-Host "  다음 단계:" -ForegroundColor White
    Write-Host "    1. VS Code 재시작 또는 새 터미널 열기" -ForegroundColor Gray
    Write-Host "    2. Ctrl+Shift+P → 'Terminal: Select Default Profile' → PowerShell 선택" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  또는 pwsh로 직접 실행:" -ForegroundColor White
    Write-Host "    pwsh -File ./start-plan.ps1 -Title ""제목""" -ForegroundColor Gray
    Write-Host ""
}
catch {
    Write-Host ""
    Write-Host "  ❌ 설치 실패: $_" -ForegroundColor Red
    Write-Host ""
    exit 1
}
