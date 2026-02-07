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

function Resolve-Sqlite3Path {
    if ($env:FLOW_SQLITE3_PATH -and (Test-Path $env:FLOW_SQLITE3_PATH)) {
        return $env:FLOW_SQLITE3_PATH
    }

    $pkgRoot = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages"
    if (Test-Path $pkgRoot) {
        $sqlitePkgs = Get-ChildItem -Path $pkgRoot -Directory -Filter "SQLite.SQLite_*" -ErrorAction SilentlyContinue
        foreach ($pkg in $sqlitePkgs) {
            $exe = Get-ChildItem -Path $pkg.FullName -Filter "sqlite3.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($exe) {
                return $exe.FullName
            }
        }
    }

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
    $output = ".load $vecPathSql" | & $sqlitePath ":memory:" 2>&1
    if ($LASTEXITCODE -ne 0) {
        return $false
    }
    if ($output -match "Error:") {
        return $false
    }
    return $true
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

# 2. 기존 .flow 제거 (백업 없이)
$flowDir = Join-Path (Get-Location) ".flow"

if (Test-Path $flowDir) {
    Write-Step "기존 .flow 제거 중..."
    Remove-Item -Path $flowDir -Recurse -Force
}

# 3. zip 다운로드
Write-Step "다운로드 중..."
$tempZip = Join-Path ([System.IO.Path]::GetTempPath()) "flow-prompts-$version.zip"
try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $tempZip -UseBasicParsing
    Write-Success "다운로드 완료"
} catch {
    Write-Warning "다운로드 실패: $_"
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

    # .github/prompts 폴더 복사 (있으면)
    $extractedPrompts = Join-Path $tempDir "prompts"
    if (Test-Path $extractedPrompts) {
        $githubDir = Join-Path (Get-Location) ".github"
        $targetPrompts = Join-Path $githubDir "prompts"
        New-Item -ItemType Directory -Path $targetPrompts -Force | Out-Null
        Copy-Item -Path "$extractedPrompts\*" -Destination $targetPrompts -Recurse -Force
    }

    # .github/agents 폴더 복사 (있으면)
    $extractedAgents = Join-Path $tempDir ".github\agents"
    if (Test-Path $extractedAgents) {
        $targetAgents = Join-Path (Get-Location) ".github\agents"
        New-Item -ItemType Directory -Path $targetAgents -Force | Out-Null
        Copy-Item -Path "$extractedAgents\*" -Destination $targetAgents -Recurse -Force
    }

    # flow.ps1 복사 (있으면)
    $extractedFlowPs1 = Join-Path $tempDir "flow.ps1"
    if (Test-Path $extractedFlowPs1) {
        Copy-Item -Path $extractedFlowPs1 -Destination (Join-Path (Get-Location) "flow.ps1") -Force
    }
    
    # 5. 필수 디렉토리 사전 생성
    $requiredDirs = @(
        ".github/prompts",
        ".github/agents",
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
    
    Write-Success "설치 완료"
} catch {
    Write-Warning "압축 해제 실패: $_"
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
