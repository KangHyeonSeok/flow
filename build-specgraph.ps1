<#
.SYNOPSIS
    Spec Graph VSCode Extension 빌드 및 패키징

.DESCRIPTION
    tools/spec-graph-ext 확장을 빌드하고 .vsix 설치 파일을 생성합니다.
    VERSION 파일에서 버전을 읽어 package.json에 반영합니다.

.PARAMETER Version
    패키지 버전 (지정하지 않으면 VERSION 파일에서 읽음)

.PARAMETER OutputDir
    .vsix 파일 출력 디렉토리 (기본: 프로젝트 루트의 dist/)

.EXAMPLE
    .\build-specgraph.ps1
    .\build-specgraph.ps1 -Version "1.2.3" -OutputDir "./artifacts"
#>
param(
    [string]$Version,
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $ProjectRoot "VERSION"))) {
    $ProjectRoot = $PSScriptRoot
}
$ExtDir = Join-Path $ProjectRoot "tools" "spec-graph-ext"

Write-Host "=== Spec Graph Extension Build ===" -ForegroundColor Cyan

# 1. 버전 결정
if (-not $Version) {
    $versionFile = Join-Path $ProjectRoot "VERSION"
    if (Test-Path $versionFile) {
        $Version = (Get-Content $versionFile -Raw).Trim()
        Write-Host "[INFO] VERSION 파일에서 버전 읽음: $Version" -ForegroundColor Gray
    } else {
        $Version = "0.1.0"
        Write-Host "[WARN] VERSION 파일 없음, 기본값 사용: $Version" -ForegroundColor Yellow
    }
}

# 2. 출력 디렉토리
if (-not $OutputDir) {
    $OutputDir = Join-Path $ProjectRoot "dist"
}
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# 3. Node.js 확인
$npmCmd = Get-Command npm -ErrorAction SilentlyContinue
if (-not $npmCmd) {
    # 일반적인 설치 경로 시도
    $candidates = @(
        "C:\Program Files\nodejs",
        "C:\Program Files (x86)\nodejs",
        "$env:LOCALAPPDATA\Programs\nodejs"
    )
    foreach ($p in $candidates) {
        if (Test-Path (Join-Path $p "npm.cmd")) {
            $env:PATH = "$p;$env:APPDATA\npm;$env:PATH"
            break
        }
    }
    $npmCmd = Get-Command npm -ErrorAction SilentlyContinue
    if (-not $npmCmd) {
        Write-Host "[ERROR] Node.js/npm을 찾을 수 없습니다. 설치 후 다시 시도하세요." -ForegroundColor Red
        exit 1
    }
}
Write-Host "[INFO] Node: $(node --version), npm: $(npm --version)" -ForegroundColor Gray

# 4. 디렉토리 이동
Push-Location $ExtDir
try {
    # 5. package.json 버전 업데이트
    Write-Host "[1/4] package.json 버전 업데이트: $Version" -ForegroundColor White
    $pkgJson = Get-Content "package.json" -Raw | ConvertFrom-Json
    $pkgJson.version = $Version
    $pkgJson | ConvertTo-Json -Depth 20 | Set-Content "package.json" -Encoding UTF8

    # 6. 의존성 설치
    Write-Host "[2/4] npm install..." -ForegroundColor White
    npm install --ignore-scripts 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] npm install 실패" -ForegroundColor Red
        exit 1
    }

    # 7. 빌드
    Write-Host "[3/4] 빌드 중..." -ForegroundColor White
    npm run build
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] 빌드 실패" -ForegroundColor Red
        exit 1
    }

    # 8. LICENSE 복사 (없으면)
    if (-not (Test-Path "LICENSE")) {
        $rootLicense = Join-Path $ProjectRoot "LICENSE"
        if (Test-Path $rootLicense) {
            Copy-Item $rootLicense "LICENSE"
        }
    }

    # 9. .vsix 패키징
    Write-Host "[4/4] .vsix 패키징..." -ForegroundColor White
    npx vsce package --no-dependencies --allow-missing-repository 2>&1 | Out-String | Write-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] 패키징 실패" -ForegroundColor Red
        exit 1
    }

    # 10. .vsix 파일을 출력 디렉토리로 이동
    $vsixFile = Get-ChildItem "*.vsix" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($vsixFile) {
        $destName = "spec-graph-$Version.vsix"
        $destPath = Join-Path $OutputDir $destName
        Move-Item $vsixFile.FullName $destPath -Force
        Write-Host ""
        Write-Host "=== 빌드 완료 ===" -ForegroundColor Green
        Write-Host "  .vsix: $destPath" -ForegroundColor Green
        Write-Host "  크기: $([math]::Round($((Get-Item $destPath).Length / 1KB), 1)) KB" -ForegroundColor Green
        Write-Host ""
        Write-Host "설치 방법:" -ForegroundColor Cyan
        Write-Host "  code --install-extension `"$destPath`"" -ForegroundColor White
    } else {
        Write-Host "[ERROR] .vsix 파일을 찾을 수 없습니다" -ForegroundColor Red
        exit 1
    }
} finally {
    Pop-Location
}
