<#
.SYNOPSIS
    Unity Editor 경로를 자동 탐색합니다.
.DESCRIPTION
    다음 순서로 Unity Editor 실행 파일을 찾습니다:
    1. UNITY_EDITOR_PATH 환경변수
    2. Unity Hub 기본 경로에서 ProjectVersion.txt 버전 매칭
    3. Unity Hub 기본 경로에서 최신 버전 fallback
.PARAMETER ProjectPath
    Unity 프로젝트 경로 (ProjectVersion.txt 탐색용)
.OUTPUTS
    Unity Editor 실행 파일의 전체 경로 (문자열)
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectPath = "."
)

$ErrorActionPreference = "Stop"

function Get-UnityHubBasePath {
    if ($IsWindows -or $env:OS -match "Windows") {
        return "C:\Program Files\Unity\Hub\Editor"
    }
    elseif ($IsMacOS) {
        return "/Applications/Unity/Hub/Editor"
    }
    else {
        return Join-Path $HOME "Unity/Hub/Editor"
    }
}

function Get-UnityExecutableName {
    if ($IsWindows -or $env:OS -match "Windows") {
        return "Unity.exe"
    }
    elseif ($IsMacOS) {
        return "Unity.app/Contents/MacOS/Unity"
    }
    else {
        return "Unity"
    }
}

function Get-ProjectUnityVersion {
    param([string]$Path)

    $versionFile = Join-Path $Path "ProjectSettings" "ProjectVersion.txt"
    if (-not (Test-Path $versionFile)) {
        return $null
    }

    $content = Get-Content $versionFile -Raw
    if ($content -match "m_EditorVersion:\s*(\S+)") {
        return $Matches[1]
    }
    return $null
}

function Find-UnityEditor {
    param([string]$ProjectPath)

    # 1. 환경변수 우선
    if ($env:UNITY_EDITOR_PATH -and (Test-Path $env:UNITY_EDITOR_PATH)) {
        return $env:UNITY_EDITOR_PATH
    }

    $hubBase = Get-UnityHubBasePath
    $exeName = Get-UnityExecutableName

    if (-not (Test-Path $hubBase)) {
        return $null
    }

    # 2. ProjectVersion.txt 버전 매칭
    $version = Get-ProjectUnityVersion -Path $ProjectPath
    if ($version) {
        $versionPath = Join-Path $hubBase $version $exeName
        if (Test-Path $versionPath) {
            return $versionPath
        }

        # 버전에서 suffix 제거 시도 (예: 2022.3.10f1 → 2022.3.10)
        $baseVersion = $version -replace '[a-zA-Z]\d+$', ''
        $candidates = Get-ChildItem $hubBase -Directory | Where-Object { $_.Name -like "$baseVersion*" }
        foreach ($dir in $candidates) {
            $candidatePath = Join-Path $dir.FullName $exeName
            if (Test-Path $candidatePath) {
                return $candidatePath
            }
        }
    }

    # 3. 최신 버전 fallback
    $versions = Get-ChildItem $hubBase -Directory | Sort-Object Name -Descending
    foreach ($dir in $versions) {
        $candidatePath = Join-Path $dir.FullName $exeName
        if (Test-Path $candidatePath) {
            return $candidatePath
        }
    }

    return $null
}

# --- Main ---
$resolvedPath = Find-UnityEditor -ProjectPath (Resolve-Path $ProjectPath).Path

if (-not $resolvedPath) {
    $result = @{
        success = $false
        error   = "Unity Editor를 찾을 수 없습니다. UNITY_EDITOR_PATH 환경변수를 설정하거나 Unity Hub를 통해 설치해 주세요."
        searched = @(
            "env:UNITY_EDITOR_PATH"
            (Get-UnityHubBasePath)
        )
    }
    $result | ConvertTo-Json -Depth 5
    exit 1
}

$result = @{
    success     = $true
    editor_path = $resolvedPath
    version     = (Get-ProjectUnityVersion -Path (Resolve-Path $ProjectPath).Path)
}
$result | ConvertTo-Json -Depth 5
exit 0
