function Resolve-UnityEditorExe {
    param(
        [Parameter(Mandatory = $false)]
        [string]$ProjectPath = (Resolve-Path ".").Path,

        [Parameter(Mandatory = $false)]
        [string]$ExplicitUnityExe = ""
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitUnityExe)) {
        if (-not (Test-Path $ExplicitUnityExe)) {
            throw "Unity executable not found: $ExplicitUnityExe"
        }

        return (Resolve-Path $ExplicitUnityExe).Path
    }

    if (-not [string]::IsNullOrWhiteSpace($env:UNITY_EXE) -and (Test-Path $env:UNITY_EXE)) {
        return (Resolve-Path $env:UNITY_EXE).Path
    }

    $versionFile = Join-Path $ProjectPath "ProjectSettings/ProjectVersion.txt"
    $editorVersion = ""

    if (Test-Path $versionFile) {
        $raw = Get-Content -Path $versionFile -Raw
        $match = [regex]::Match($raw, 'm_EditorVersion:\s*(?<ver>[^\r\n]+)')
        if ($match.Success) {
            $editorVersion = $match.Groups['ver'].Value.Trim()
        }
    }

    $attempted = @()
    $candidateEditorRoots = @(
        "C:\Program Files\Unity\Hub\Editor",
        "C:\Program Files (x86)\Unity\Hub\Editor",
        "D:\Projects\Unity Hub\Editor"
    )

    $secondaryInstallPathFile = Join-Path $env:AppData "UnityHub\secondaryInstallPath.json"
    if (Test-Path $secondaryInstallPathFile) {
        try {
            $secondaryRaw = Get-Content -Path $secondaryInstallPathFile -Raw
            $secondaryJson = ConvertFrom-Json -InputObject $secondaryRaw

            if ($secondaryJson -is [string]) {
                if (-not [string]::IsNullOrWhiteSpace($secondaryJson)) {
                    $candidateEditorRoots += $secondaryJson
                }
            }
            elseif ($secondaryJson -ne $null) {
                if ($secondaryJson.PSObject.Properties.Name -contains "path" -and -not [string]::IsNullOrWhiteSpace($secondaryJson.path)) {
                    $candidateEditorRoots += [string]$secondaryJson.path
                }

                if ($secondaryJson.PSObject.Properties.Name -contains "installPath" -and -not [string]::IsNullOrWhiteSpace($secondaryJson.installPath)) {
                    $candidateEditorRoots += [string]$secondaryJson.installPath
                }
            }
        }
        catch {
        }
    }

    $candidateEditorRoots = $candidateEditorRoots | Select-Object -Unique

    if (-not [string]::IsNullOrWhiteSpace($editorVersion)) {
        foreach ($editorRoot in $candidateEditorRoots) {
            if ([string]::IsNullOrWhiteSpace($editorRoot)) {
                continue
            }

            $hubPath = Join-Path $editorRoot (Join-Path $editorVersion "Editor\Unity.exe")
            $attempted += $hubPath
            if (Test-Path $hubPath) {
                return (Resolve-Path $hubPath).Path
            }
        }
    }

    foreach ($editorRoot in $candidateEditorRoots) {
        if ([string]::IsNullOrWhiteSpace($editorRoot) -or -not (Test-Path $editorRoot)) {
            continue
        }

        $candidates = Get-ChildItem -Path $editorRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object -Property Name -Descending

        foreach ($dir in $candidates) {
            $hubPath = Join-Path $dir.FullName "Editor\Unity.exe"
            $attempted += $hubPath
            if (Test-Path $hubPath) {
                return (Resolve-Path $hubPath).Path
            }
        }
    }

    $attemptText = if ($attempted.Count -gt 0) { ($attempted | Select-Object -Unique) -join "; " } else { "none" }
    throw "Unity executable could not be resolved. Pass -UnityExe or set UNITY_EXE env var. Attempted: $attemptText"
}
