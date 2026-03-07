<#
.SYNOPSIS
    Flow VSCode Extension 빌드 및 패키징

.DESCRIPTION
    tools/flow-ext 확장을 빌드하고 .vsix 설치 파일을 생성합니다.
    VERSION 파일에서 버전을 읽어 package.json에 반영합니다.
    빌드가 끝나면 생성된 .vsix를 VS Code에 강제 설치합니다.

.PARAMETER Version
    패키지 버전 (지정하지 않으면 VERSION 파일에서 읽음)

.PARAMETER OutputDir
    .vsix 파일 출력 디렉토리 (기본: 프로젝트 루트의 dist/)

.EXAMPLE
    .\build-flow-ext.ps1
    .\build-flow-ext.ps1 -Version "1.2.3" -OutputDir "./artifacts"
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
$ExtDir = Join-Path $ProjectRoot "tools" "flow-ext"

Write-Host "=== Flow Extension Build ===" -ForegroundColor Cyan

function Get-CurrentVSCodeCliCandidate {
    $currentExe = $env:VSCODE_GIT_ASKPASS_NODE
    if (-not [string]::IsNullOrWhiteSpace($currentExe) -and (Test-Path $currentExe)) {
        return $currentExe
    }

    return $null
}

function Resolve-VSCodeCli {
    $currentVsCodeCli = Get-CurrentVSCodeCliCandidate
    if ($currentVsCodeCli) {
        return $currentVsCodeCli
    }

    $codeCmd = Get-Command code -ErrorAction SilentlyContinue
    if ($codeCmd) {
        return $codeCmd.Source
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Microsoft VS Code\bin\code.cmd"),
        (Join-Path $env:LOCALAPPDATA "Programs\Microsoft VS Code Insiders\bin\code-insiders.cmd"),
        (Join-Path $env:ProgramFiles "Microsoft VS Code\bin\code.cmd"),
        (Join-Path ${env:ProgramFiles(x86)} "Microsoft VS Code\bin\code.cmd")
    ) | Where-Object { $_ -and (Test-Path $_) }

    return $candidates | Select-Object -First 1
}

function Write-ReloadSignal {
    param(
        [string]$ProjectRootPath,
        [string]$VersionText
    )

    $signalDir = Join-Path $ProjectRootPath ".flow"
    $signalPath = Join-Path $signalDir "flow-ext.reload.signal"

    if (-not (Test-Path $signalDir)) {
        New-Item -ItemType Directory -Path $signalDir -Force | Out-Null
    }

    $payload = @{
        requestedAt = (Get-Date).ToString('o')
        version = $VersionText
        source = 'build-flow-ext.ps1'
    } | ConvertTo-Json -Depth 5

    Set-Content -Path $signalPath -Value $payload -Encoding UTF8
    Write-Host "[INFO] 자동 리로드 신호 기록: $signalPath" -ForegroundColor Gray
}

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
    Write-Host "[1/5] package.json 버전 업데이트: $Version" -ForegroundColor White
    $pkgJson = Get-Content "package.json" -Raw | ConvertFrom-Json
    $pkgJson.version = $Version
    $pkgJson | ConvertTo-Json -Depth 20 | Set-Content "package.json" -Encoding UTF8

    # 6. 의존성 설치
    Write-Host "[2/5] npm install..." -ForegroundColor White
    npm install --ignore-scripts 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] npm install 실패" -ForegroundColor Red
        exit 1
    }

    # 7. 빌드
    Write-Host "[3/5] 빌드 중..." -ForegroundColor White
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
    Write-Host "[4/5] .vsix 패키징..." -ForegroundColor White
    npx vsce package --no-dependencies --allow-missing-repository 2>&1 | Out-String | Write-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] 패키징 실패" -ForegroundColor Red
        exit 1
    }

    # 10. .vsix 파일을 출력 디렉토리로 이동
    $vsixFile = Get-ChildItem "*.vsix" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($vsixFile) {
        $destName = "flow-ext-$Version.vsix"
        $destPath = Join-Path $OutputDir $destName
        Move-Item $vsixFile.FullName $destPath -Force
        Write-Host ""
        Write-Host "=== 빌드 완료 ===" -ForegroundColor Green
        Write-Host "  .vsix: $destPath" -ForegroundColor Green
        Write-Host "  크기: $([math]::Round($((Get-Item $destPath).Length / 1KB), 1)) KB" -ForegroundColor Green
        Write-Host ""

        $vsCodeCli = Resolve-VSCodeCli
        if (-not $vsCodeCli) {
            Write-Host "[ERROR] VS Code CLI(code)를 찾을 수 없습니다. PATH 또는 VS Code 설치를 확인하세요." -ForegroundColor Red
            exit 1
        }

        Write-Host "[5/5] VS Code 확장 강제 설치..." -ForegroundColor White
        Write-Host "[INFO] VS Code CLI: $vsCodeCli" -ForegroundColor Gray
        & $vsCodeCli --install-extension $destPath --force
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[ERROR] VS Code 확장 설치 실패" -ForegroundColor Red
            exit 1
        }

        Write-Host "[INFO] 강제 설치 완료" -ForegroundColor Green
        Write-ReloadSignal -ProjectRootPath $ProjectRoot -VersionText $Version
    } else {
        Write-Host "[ERROR] .vsix 파일을 찾을 수 없습니다" -ForegroundColor Red
        exit 1
    }
} finally {
    Pop-Location
}
