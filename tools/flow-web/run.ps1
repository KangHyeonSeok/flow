#!/usr/bin/env pwsh
<#
.SYNOPSIS
flow-web 빌드 및 실행 스크립트

.DESCRIPTION
Vite 기반 flow-web 프로젝트의 의존성을 확인하고, 필요 시 빌드한 뒤
실행 모드에 따라 flow-api 통합 서버 또는 Vite 개발 서버를 실행하고 브라우저를 엽니다.

.PARAMETER Mode
실행 모드. Preview는 프로덕션 빌드 후 flow-api가 정적 파일과 API를 함께 서비스하고,
Dev는 flow-api와 Vite 개발 서버를 함께 실행합니다.

.PARAMETER Port
브라우저로 여는 메인 서버 포트. 지정하지 않으면 Preview는 5000, Dev는 3000을 사용합니다.

.PARAMETER ApiPort
Dev 모드에서 flow-api가 사용할 포트입니다. 지정하지 않으면 5000을 사용합니다.

.PARAMETER SkipBuild
Preview 모드에서 build 단계를 건너뜁니다.

.PARAMETER NoBrowser
브라우저를 자동으로 열지 않습니다.

.PARAMETER TimeoutSeconds
서버가 준비될 때까지 기다리는 최대 시간(초)입니다.

.EXAMPLE
./run.ps1

.EXAMPLE
./run.ps1 -Mode Dev

.EXAMPLE
./run.ps1 -Port 3100 -ApiPort 5001 -NoBrowser
#>

[CmdletBinding()]
param(
    [ValidateSet("Preview", "Dev")]
    [string]$Mode = "Preview",

    [int]$Port,

    [int]$ApiPort = 5000,

    [switch]$SkipBuild,

    [switch]$NoBrowser,

    [ValidateRange(1, 300)]
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$packageJsonPath = Join-Path $scriptRoot 'package.json'
$apiProjectPath = Join-Path $scriptRoot '..\flow-api\flow-api.csproj'

if (-not (Test-Path $packageJsonPath)) {
    Write-Error "package.json not found: $packageJsonPath"
    exit 1
}

if (-not (Test-Path $apiProjectPath)) {
    Write-Error "flow-api project not found: $apiProjectPath"
    exit 1
}

$effectivePort = if ($PSBoundParameters.ContainsKey('Port')) {
    $Port
}
elseif ($Mode -eq 'Dev') {
    3000
}
else {
    5000
}

$serverUrl = "http://localhost:$effectivePort"
$apiUrl = "http://localhost:$ApiPort"

function Get-NpmCommand {
    $npmCmd = Get-Command 'npm.cmd' -ErrorAction SilentlyContinue
    if ($npmCmd) {
        return $npmCmd.Source
    }

    $npm = Get-Command 'npm' -ErrorAction SilentlyContinue
    if ($npm) {
        return $npm.Source
    }

    throw 'npm executable was not found in PATH.'
}

function Get-DotnetCommand {
    $dotnet = Get-Command 'dotnet' -ErrorAction SilentlyContinue
    if ($dotnet) {
        return $dotnet.Source
    }

    throw 'dotnet executable was not found in PATH.'
}

function Invoke-Npm {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $script:npmCommand @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "npm command failed: npm $($Arguments -join ' ')"
    }
}

function Wait-ForHttpServer {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,

        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,

        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) {
            throw "Server process exited before becoming ready. Exit code: $($Process.ExitCode)"
        }

        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -Method Get -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
            continue
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Timed out waiting for server: $Url"
}

function Stop-ManagedProcess {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($Process.HasExited) {
        return
    }

    try {
        Stop-Process -Id $Process.Id -Force -ErrorAction Stop
        Write-Host "Stopped $Name (PID: $($Process.Id))" -ForegroundColor Gray
    }
    catch {
        Write-Warning "Failed to stop $Name (PID: $($Process.Id)): $_"
    }
}

$npmCommand = Get-NpmCommand
$dotnetCommand = Get-DotnetCommand
$nodeModulesPath = Join-Path $scriptRoot 'node_modules'
$lockFilePath = Join-Path $scriptRoot 'package-lock.json'
$apiProcess = $null
$webProcess = $null

Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  flow-web Run" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Mode: $Mode" -ForegroundColor Gray
Write-Host "URL: $serverUrl" -ForegroundColor Gray
if ($Mode -eq 'Dev') {
    Write-Host "API: $apiUrl" -ForegroundColor Gray
}
Write-Host "Project: $scriptRoot" -ForegroundColor Gray
Write-Host ""

Push-Location $scriptRoot
try {
    if (-not (Test-Path $nodeModulesPath)) {
        $installArgs = if (Test-Path $lockFilePath) {
            @('ci')
        }
        else {
            @('install')
        }

        Write-Host 'Installing dependencies...' -ForegroundColor Yellow
        Invoke-Npm -Arguments $installArgs
        Write-Host ''
    }

    if ($Mode -eq 'Preview' -and -not $SkipBuild) {
        Write-Host 'Building flow-web...' -ForegroundColor Yellow
        Invoke-Npm -Arguments @('run', 'build')
        Write-Host ''
    }

    if ($Mode -eq 'Preview') {
        Write-Host 'Starting integrated flow-api server...' -ForegroundColor Yellow
        $apiProcess = Start-Process -FilePath $dotnetCommand -ArgumentList @('run', '--project', $apiProjectPath, '--urls', $serverUrl) -WorkingDirectory (Split-Path $apiProjectPath) -PassThru

        Wait-ForHttpServer -Url $serverUrl -Process $apiProcess -TimeoutSeconds $TimeoutSeconds

        Write-Host ''
        Write-Host '═══════════════════════════════════════' -ForegroundColor Cyan
        Write-Host '  Server Ready' -ForegroundColor Cyan
        Write-Host '═══════════════════════════════════════' -ForegroundColor Cyan
        Write-Host "  PID: $($apiProcess.Id)" -ForegroundColor Gray
        Write-Host "  URL: $serverUrl" -ForegroundColor Green

        if (-not $NoBrowser) {
            Start-Process $serverUrl | Out-Null
            Write-Host '  Browser: opened' -ForegroundColor Gray
        }
        else {
            Write-Host '  Browser: skipped' -ForegroundColor Gray
        }

        Write-Host ''
        Write-Host 'Press Ctrl+C to stop the server.' -ForegroundColor Gray
        Wait-Process -Id $apiProcess.Id
    }
    else {
        Write-Host 'Starting flow-api backend...' -ForegroundColor Yellow
        $apiProcess = Start-Process -FilePath $dotnetCommand -ArgumentList @('run', '--project', $apiProjectPath, '--urls', $apiUrl) -WorkingDirectory (Split-Path $apiProjectPath) -PassThru
        Wait-ForHttpServer -Url $apiUrl -Process $apiProcess -TimeoutSeconds $TimeoutSeconds

        Write-Host 'Starting Vite dev server...' -ForegroundColor Yellow
        $webProcess = Start-Process -FilePath $npmCommand -ArgumentList @('run', 'dev', '--', '--host', 'localhost', '--port', "$effectivePort") -WorkingDirectory $scriptRoot -PassThru
        Wait-ForHttpServer -Url $serverUrl -Process $webProcess -TimeoutSeconds $TimeoutSeconds

        Write-Host ''
        Write-Host '═══════════════════════════════════════' -ForegroundColor Cyan
        Write-Host '  Dev Servers Ready' -ForegroundColor Cyan
        Write-Host '═══════════════════════════════════════' -ForegroundColor Cyan
        Write-Host "  Web PID: $($webProcess.Id)" -ForegroundColor Gray
        Write-Host "  API PID: $($apiProcess.Id)" -ForegroundColor Gray
        Write-Host "  Web URL: $serverUrl" -ForegroundColor Green
        Write-Host "  API URL: $apiUrl" -ForegroundColor Gray

        if (-not $NoBrowser) {
            Start-Process $serverUrl | Out-Null
            Write-Host '  Browser: opened' -ForegroundColor Gray
        }
        else {
            Write-Host '  Browser: skipped' -ForegroundColor Gray
        }

        Write-Host ''
        Write-Host 'Press Ctrl+C to stop both servers.' -ForegroundColor Gray
        Wait-Process -Id $webProcess.Id
    }
}
catch {
    Write-Host ''
    Write-Host "❌ $_" -ForegroundColor Red
    exit 1
}
finally {
    if ($Mode -eq 'Dev') {
        if ($webProcess) {
            Stop-ManagedProcess -Process $webProcess -Name 'Vite dev server'
        }

        if ($apiProcess) {
            Stop-ManagedProcess -Process $apiProcess -Name 'flow-api backend'
        }
    }

    Pop-Location
}