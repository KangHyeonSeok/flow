#!/usr/bin/env pwsh
# Flow E2E 테스트 도구 설치 스크립트

[CmdletBinding()]
param(
    [string]$Repo = "KangHyeonSeok/flow",
    [string]$Version,
    [switch]$SkipSetup,
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

function Resolve-PythonCommand {
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) {
        return @("python")
    }

    $py = Get-Command py -ErrorAction SilentlyContinue
    if ($py) {
        return @("py", "-3")
    }

    return $null
}

Write-Host ""
Write-Host "═══════════════════════════════════════" -ForegroundColor Blue
Write-Host "  Flow E2E 설치" -ForegroundColor Blue
Write-Host "═══════════════════════════════════════" -ForegroundColor Blue
Write-Host ""

$apiUrl = "https://api.github.com/repos/$Repo/releases/latest"
if ($Version) {
    $apiUrl = "https://api.github.com/repos/$Repo/releases/tags/v$Version"
}

Write-Step "E2E 릴리스 확인 중..."
try {
    $release = Invoke-RestMethod -Uri $apiUrl -Headers @{ "User-Agent" = "PowerShell" }
}
catch {
    Write-Warning "릴리스 조회 실패: $_"
    exit 1
}

$targetVersion = $release.tag_name -replace '^v', ''
$e2eAsset = $release.assets | Where-Object { $_.name -match '^flow-e2e-test-.*\.zip$' } | Select-Object -First 1

if (-not $e2eAsset) {
    Write-Warning "flow-e2e-test zip 자산을 찾을 수 없습니다."
    exit 1
}

$flowDir = Split-Path $PSScriptRoot -Parent
$targetDir = Join-Path $flowDir "e2e-test"
$tempZip = Join-Path ([System.IO.Path]::GetTempPath()) "flow-e2e-test-$targetVersion.zip"
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "flow-e2e-extract-$([Guid]::NewGuid().ToString('N'))"

try {
    if (Test-Path $targetDir) {
        if ($Force) {
            Write-Step "기존 e2e-test 제거 중..."
            Remove-Item -Path $targetDir -Recurse -Force
        }
        else {
            Write-Step "기존 e2e-test 갱신 중..."
            Remove-Item -Path $targetDir -Recurse -Force
        }
    }

    Write-Step "flow-e2e-test 다운로드 중..."
    Invoke-WebRequest -Uri $e2eAsset.browser_download_url -OutFile $tempZip -UseBasicParsing

    Write-Step "압축 해제 중..."
    Expand-Archive -Path $tempZip -DestinationPath $tempDir -Force

    $extractedDir = Join-Path $tempDir "e2e-test"
    if (-not (Test-Path $extractedDir)) {
        throw "e2e-test 폴더를 찾을 수 없습니다."
    }

    Copy-Item -Path $extractedDir -Destination $targetDir -Recurse -Force
    Write-Success "e2e-test 파일 설치 완료"

    if (-not $SkipSetup) {
        $pythonCommand = Resolve-PythonCommand
        if (-not $pythonCommand) {
            Write-Warning "Python 실행 파일을 찾지 못해 자동 환경 설정을 건너뜁니다."
        }
        else {
            Write-Step "Python 가상환경 설정 중..."
            Push-Location $targetDir
            try {
                $extraArgs = @()
                if ($pythonCommand.Length -gt 1) {
                    $extraArgs = $pythonCommand[1..($pythonCommand.Length - 1)]
                }
                & $pythonCommand[0] @($extraArgs + @("-m", "e2e_test.installer.venv_manager", "--setup"))
                Write-Success "e2e Python 환경 설정 완료"
            }
            catch {
                Write-Warning "e2e 환경 설정 실패: $_"
                Write-Host "  수동 실행: cd .flow/e2e-test; python -m e2e_test.installer.venv_manager --setup" -ForegroundColor Gray
            }
            finally {
                Pop-Location
            }
        }
    }

    Write-Success "Flow E2E 설치 완료 (v$targetVersion)"
}
catch {
    Write-Warning "e2e 설치 실패: $_"
    exit 1
}
finally {
    if (Test-Path $tempZip) { Remove-Item -Path $tempZip -Force }
    if (Test-Path $tempDir) { Remove-Item -Path $tempDir -Recurse -Force }
}
