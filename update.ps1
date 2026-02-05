#!/usr/bin/env pwsh
# Flow Prompt 업데이트 스크립트 (Windows/macOS/Linux)
# 사용법: irm https://raw.githubusercontent.com/OWNER/REPO/main/update.ps1 | iex

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

function Resolve-Sqlite3Path {
    $wingetLink = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Links\sqlite3.exe"
    if (Test-Path $wingetLink) {
        return $wingetLink
    }

    $sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue
    if ($sqlite) {
        return $sqlite.Source
    }

    return $null
}

function Test-SqliteExtensionLoad {
    param([string]$VecPath)

    $sqlitePath = Resolve-Sqlite3Path
    if (-not $sqlitePath) {
        return $false
    }

    if (-not (Test-Path $VecPath)) {
        return $false
    }

    $vecPathSql = $VecPath.Replace('\\', '/')
    & $sqlitePath "-cmd" ".load $vecPathSql" ":memory:" 2>&1 | Out-Null
    return ($LASTEXITCODE -eq 0)
}

function Ensure-SqliteExtensionSupport {
    param([string]$VecPath)

    if (Test-SqliteExtensionLoad -VecPath $VecPath) {
        return $true
    }

    if (-not $IsWindows) {
        Write-Warning "sqlite 확장 로딩을 지원하는 sqlite3가 필요합니다."
        return $false
    }

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        Write-Warning "winget을 찾을 수 없어 sqlite3를 설치할 수 없습니다."
        return $false
    }

    Write-Step "sqlite3(확장 로딩 지원) 설치 중..."
    try {
        winget install SQLite.SQLite -e --accept-package-agreements --accept-source-agreements | Out-Null
    }
    catch {
        Write-Warning "sqlite3 설치 실패: $_"
        return $false
    }

    Start-Sleep -Seconds 1

    if (Test-SqliteExtensionLoad -VecPath $VecPath) {
        Write-Success "sqlite3 확장 로딩 확인 완료"
        return $true
    }

    Write-Warning "sqlite3 확장 로딩을 확인하지 못했습니다."
    return $false
}

Write-Host ""
Write-Host "═══════════════════════════════════════" -ForegroundColor Blue
Write-Host "  Flow Prompt 업데이트 확인" -ForegroundColor Blue
Write-Host "═══════════════════════════════════════" -ForegroundColor Blue
Write-Host ""

# 1. 현재 버전 확인
$flowDir = Join-Path (Get-Location) ".flow"
$versionFile = Join-Path $flowDir "version.txt"

if (-not (Test-Path $versionFile)) {
    Write-Warning "Flow가 설치되어 있지 않습니다."
    Write-Host "  설치하려면: irm https://raw.githubusercontent.com/$Repo/main/install.ps1 | iex" -ForegroundColor Gray
    exit 1
}

$currentVersion = (Get-Content $versionFile -Raw).Trim()
Write-Step "현재 버전: $currentVersion"

# 2. 최신 릴리스 정보 조회
Write-Step "최신 버전 확인 중..."
$apiUrl = "https://api.github.com/repos/$Repo/releases/latest"
try {
    $release = Invoke-RestMethod -Uri $apiUrl -Headers @{ "User-Agent" = "PowerShell" }
    $latestVersion = $release.tag_name -replace '^v', ''
    Write-Step "최신 버전: $latestVersion"
} catch {
    Write-Warning "릴리스 조회 실패: $_"
    exit 1
}

# 3. 버전 비교
if ($currentVersion -eq $latestVersion -and -not $Force) {
    Write-Host ""
    Write-Host "═══════════════════════════════════════" -ForegroundColor Green
    Write-Success "Already up to date (v$currentVersion)"
    Write-Host "═══════════════════════════════════════" -ForegroundColor Green
    Write-Host ""
    exit 0
}

Write-Host ""
Write-Host "  새 버전 발견: $currentVersion → $latestVersion" -ForegroundColor Yellow
Write-Host ""

# 4. 설치 스크립트 실행
Write-Step "업데이트 중..."

$zipAsset = $release.assets | Where-Object { $_.name -match '\.zip$' } | Select-Object -First 1
if (-not $zipAsset) {
    Write-Warning "zip 파일을 찾을 수 없습니다."
    exit 1
}

$downloadUrl = $zipAsset.browser_download_url

# 기존 .flow 제거 전 db 백업
$dbBackupDir = $null
$ragDbPath = Join-Path $flowDir "rag\db"
if (Test-Path $ragDbPath) {
    $dbBackupDir = Join-Path ([System.IO.Path]::GetTempPath()) "flow-db-backup-$([Guid]::NewGuid().ToString('N'))"
    Write-Step "RAG 데이터베이스 백업 중..."
    New-Item -ItemType Directory -Path $dbBackupDir -Force | Out-Null
    Copy-Item -Path $ragDbPath -Destination $dbBackupDir -Recurse -Force
    Write-Success "데이터베이스 백업 완료: $dbBackupDir"
}

# 기존 .flow 제거
if (Test-Path $flowDir) {
    Write-Step "기존 .flow 제거 중..."
    Remove-Item -Path $flowDir -Recurse -Force
}

# zip 다운로드
$tempZip = Join-Path ([System.IO.Path]::GetTempPath()) "flow-prompts-$latestVersion.zip"
try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $tempZip -UseBasicParsing
    Write-Success "다운로드 완료"
} catch {
    Write-Warning "다운로드 실패: $_"
    exit 1
}

# 압축 해제
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "flow-extract-$([Guid]::NewGuid().ToString('N'))"
try {
    Expand-Archive -Path $tempZip -DestinationPath $tempDir -Force
    
    $extractedFlow = Join-Path $tempDir ".flow"
    if (Test-Path $extractedFlow) {
        Copy-Item -Path $extractedFlow -Destination $flowDir -Recurse -Force
    } else {
        throw ".flow 폴더를 찾을 수 없습니다."
    }

    # RAG 데이터베이스 복구
    if ($null -ne $dbBackupDir) {
        $backupDbPath = Join-Path $dbBackupDir "db"
        if (Test-Path $backupDbPath) {
            Write-Step "RAG 데이터베이스 복구 중..."
            $ragDir = Join-Path $flowDir "rag"
            if (-not (Test-Path $ragDir)) {
                New-Item -ItemType Directory -Path $ragDir -Force | Out-Null
            }
            Copy-Item -Path $backupDbPath -Destination $ragDir -Recurse -Force
            Write-Success "데이터베이스 복구 완료"
            Remove-Item -Path $dbBackupDir -Recurse -Force -ErrorAction SilentlyContinue
        }
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
        New-Item -ItemType Directory -Path $targetPrompts -Force | Out-Null
        Copy-Item -Path "$extractedPrompts\*" -Destination $targetPrompts -Recurse -Force
        
        # .claude/commands 폴더에도 복사 (prompt 제거)
        $claudeCommandsDir = Join-Path (Get-Location) ".claude/commands"
        New-Item -ItemType Directory -Path $claudeCommandsDir -Force | Out-Null
        $basePromptPath = (Resolve-Path -LiteralPath $extractedPrompts).Path
        Get-ChildItem -Path $extractedPrompts -File -Recurse | ForEach-Object {
            $filePath = (Resolve-Path -LiteralPath $_.FullName).Path
            $relativePath = $filePath.Substring($basePromptPath.Length).TrimStart('\', '/')
            $targetRelative = $relativePath -replace '\.prompt', ''
            $targetPath = Join-Path $claudeCommandsDir $targetRelative
            $targetDir = Split-Path -Path $targetPath -Parent
            if (-not (Test-Path $targetDir)) {
                New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
            }
            Copy-Item -Path $_.FullName -Destination $targetPath -Force
        }
    }
    
    # 필수 디렉토리 사전 생성
    $requiredDirs = @(
        ".github/prompts",
        ".claude/commands",
        "docs",
        "docs/flow",
        "docs/flow/backlogs",
        "docs/flow/implements",
        "docs/flow/meta"
    )
    foreach ($dir in $requiredDirs) {
        $fullPath = Join-Path (Get-Location) $dir
        if (-not (Test-Path $fullPath)) {
            New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
        }
    }

    # sqlite-vec 확장 로딩 확인 (필요 시 sqlite3 설치)
    $vecPath = Join-Path $flowDir "rag\bin\vec0.dll"
    Ensure-SqliteExtensionSupport -VecPath $vecPath | Out-Null

    # RAG DB가 없으면 초기화
    $ragDbPath = Join-Path $flowDir "rag\db\local.db"
    $dbScript = Join-Path $flowDir "scripts\db.ps1"
    if (-not (Test-Path $ragDbPath) -and (Test-Path $dbScript)) {
        Write-Step "RAG 데이터베이스 초기화 중..."
        try {
            & $dbScript -init | Out-Null
            Write-Success "RAG 데이터베이스 초기화 완료"
        }
        catch {
            Write-Warning "RAG 데이터베이스 초기화 실패: $_"
        }
    }
    
    Write-Success "업데이트 완료"
} catch {
    Write-Warning "압축 해제 실패: $_"
    exit 1
} finally {
    if (Test-Path $tempZip) { Remove-Item -Path $tempZip -Force }
    if (Test-Path $tempDir) { Remove-Item -Path $tempDir -Recurse -Force }
}

# 설치된 버전 확인
$installedVersion = ""
if (Test-Path $versionFile) {
    $installedVersion = (Get-Content $versionFile -Raw).Trim()
}

Write-Host ""
Write-Host "═══════════════════════════════════════" -ForegroundColor Green
Write-Success "Flow Prompt v$installedVersion installed"
Write-Host "  업데이트됨: $currentVersion → $installedVersion" -ForegroundColor Gray
Write-Host "═══════════════════════════════════════" -ForegroundColor Green
Write-Host ""
