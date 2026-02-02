#!/usr/bin/env pwsh
# Flow Prompt 설치 스크립트 (Windows/macOS/Linux)
# 사용법: irm https://raw.githubusercontent.com/OWNER/REPO/main/install.ps1 | iex

[CmdletBinding()]
param(
    [string]$Repo = "KangHyeonSeok/flow",
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host "  → $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "  ✅ $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "  ⚠️ $Message" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "═══════════════════════════════════════" -ForegroundColor Blue
Write-Host "  Flow Prompt 설치" -ForegroundColor Blue
Write-Host "═══════════════════════════════════════" -ForegroundColor Blue
Write-Host ""

# 1. 최신 릴리스 정보 조회
Write-Step "최신 릴리스 확인 중..."
$apiUrl = "https://api.github.com/repos/$Repo/releases/latest"
try {
    $release = Invoke-RestMethod -Uri $apiUrl -Headers @{ "User-Agent" = "PowerShell" }
    $version = $release.tag_name -replace '^v', ''
    $zipAsset = $release.assets | Where-Object { $_.name -match '\.zip$' } | Select-Object -First 1
    
    if (-not $zipAsset) {
        throw "zip 파일을 찾을 수 없습니다."
    }
    
    $downloadUrl = $zipAsset.browser_download_url
    Write-Success "버전 $version 발견"
} catch {
    Write-Warning "릴리스 조회 실패: $_"
    Write-Host "  직접 다운로드: https://github.com/$Repo/releases" -ForegroundColor Gray
    exit 1
}

# 2. 기존 .flow 백업
$flowDir = Join-Path (Get-Location) ".flow"
$backupDir = Join-Path (Get-Location) ".flow.bak"

if (Test-Path $flowDir) {
    Write-Step "기존 .flow 백업 중..."
    if (Test-Path $backupDir) {
        Remove-Item -Path $backupDir -Recurse -Force
    }
    Move-Item -Path $flowDir -Destination $backupDir -Force
    Write-Success "백업 완료: .flow.bak"
}

# 3. zip 다운로드
Write-Step "다운로드 중..."
$tempZip = Join-Path ([System.IO.Path]::GetTempPath()) "flow-prompts-$version.zip"
try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $tempZip -UseBasicParsing
    Write-Success "다운로드 완료"
} catch {
    Write-Warning "다운로드 실패: $_"
    # 백업 복원
    if (Test-Path $backupDir) {
        Move-Item -Path $backupDir -Destination $flowDir -Force
        Write-Host "  백업에서 복원됨" -ForegroundColor Gray
    }
    exit 1
}

# 4. 압축 해제
Write-Step "압축 해제 중..."
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "flow-extract-$([Guid]::NewGuid().ToString('N'))"
try {
    Expand-Archive -Path $tempZip -DestinationPath $tempDir -Force
    
    # .flow 폴더 복사
    $extractedFlow = Join-Path $tempDir ".flow"
    if (Test-Path $extractedFlow) {
        Copy-Item -Path $extractedFlow -Destination $flowDir -Recurse -Force
    } else {
        throw ".flow 폴더를 찾을 수 없습니다."
    }

    # .claude 폴더 복사 (있으면)
    $extractedClaude = Join-Path $tempDir ".claude"
    $claudeDir = Join-Path (Get-Location) ".claude"
    if (Test-Path $extractedClaude) {
        Copy-Item -Path $extractedClaude -Destination $claudeDir -Recurse -Force
    }

    # .github/prompts 폴더 복사 (있으면)
    $extractedPrompts = Join-Path $tempDir "prompts"
    if (Test-Path $extractedPrompts) {
        $githubDir = Join-Path (Get-Location) ".github"
        $targetPrompts = Join-Path $githubDir "prompts"
        New-Item -ItemType Directory -Path $githubDir -Force | Out-Null
        Copy-Item -Path $extractedPrompts -Destination $targetPrompts -Recurse -Force
    }
    
    Write-Success "설치 완료"
} catch {
    Write-Warning "압축 해제 실패: $_"
    # 백업 복원
    if (Test-Path $backupDir) {
        Move-Item -Path $backupDir -Destination $flowDir -Force
        Write-Host "  백업에서 복원됨" -ForegroundColor Gray
    }
    exit 1
} finally {
    # 임시 파일 정리
    if (Test-Path $tempZip) { Remove-Item -Path $tempZip -Force }
    if (Test-Path $tempDir) { Remove-Item -Path $tempDir -Recurse -Force }
}

# 5. 설치된 버전 확인
$installedVersion = ""
$versionFile = Join-Path $flowDir "version.txt"
if (Test-Path $versionFile) {
    $installedVersion = (Get-Content $versionFile -Raw).Trim()
}

Write-Host ""
Write-Host "═══════════════════════════════════════" -ForegroundColor Green
Write-Success "Flow Prompt v$installedVersion installed"
Write-Host "═══════════════════════════════════════" -ForegroundColor Green
Write-Host ""
Write-Host "  사용법: Copilot Chat에서 /flow 입력" -ForegroundColor Gray
Write-Host ""
