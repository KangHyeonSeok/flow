param(
    [Parameter(Mandatory = $false)]
    [string]$UnityExe = "",

    [Parameter(Mandatory = $false)]
    [string]$ProjectPath = (Resolve-Path ".").Path,

    [Parameter(Mandatory = $false)]
    [string]$BuildMethod = "Jankyeol.Editor.BuildScript.BuildE2EWindows",

    [Parameter(Mandatory = $false)]
    [switch]$SkipBuild,

    [Parameter(Mandatory = $false)]
    [switch]$SkipEditMode,

    [Parameter(Mandatory = $false)]
    [switch]$SkipPlayMode,

    [Parameter(Mandatory = $false)]
    [string]$ArtifactRoot = "artifacts/unity-cli",

    [Parameter(Mandatory = $false)]
    [int]$BuildTimeoutSec = 1200,

    [Parameter(Mandatory = $false)]
    [int]$TestTimeoutSec = 1200,

    [Parameter(Mandatory = $false)]
    [string]$TestFilter = ""
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Resolve-UnityEditor.ps1")

function Write-Step($message) {
    Write-Host "[UNITY-CLI] $message" -ForegroundColor Cyan
}

function Invoke-UnityCli {
    param(
        [string]$UnityExe,
        [string[]]$UnityArgs,
        [string]$LogFile,
        [int]$TimeoutSec,
        [string]$StepName
    )

    Write-Step "$StepName"
    Write-Host "[UNITY-CLI] $UnityExe $($UnityArgs -join ' ')" -ForegroundColor DarkGray

    $process = Start-Process -FilePath $UnityExe -ArgumentList $UnityArgs -PassThru -NoNewWindow

    if (-not $process.WaitForExit($TimeoutSec * 1000)) {
        try { Stop-Process -Id $process.Id -Force } catch {}
        throw "$StepName timeout after ${TimeoutSec}s"
    }

    if ($process.ExitCode -ne 0) {
        throw "$StepName failed with exit code $($process.ExitCode). Check log: $LogFile"
    }

    if (-not (Test-Path $LogFile)) {
        throw "$StepName did not produce expected log file: $LogFile"
    }
}

function Write-CondensedUnityLog {
    param(
        [string]$SourceLog,
        [string]$DestinationLog
    )

    if (-not (Test-Path $SourceLog)) {
        return
    }

    $content = Get-Content -Path $SourceLog -Raw
    if ([string]::IsNullOrWhiteSpace($content)) {
        Set-Content -Path $DestinationLog -Value "" -Encoding UTF8
        return
    }

    $marker = "##### ExitCode"
    $index = $content.IndexOf($marker)

    if ($index -ge 0) {
        $tail = $content.Substring($index)
        Set-Content -Path $DestinationLog -Value $tail -Encoding UTF8
        return
    }

    $fallbackLines = (Get-Content -Path $SourceLog | Select-Object -Last 200)
    $fallbackLines | Set-Content -Path $DestinationLog -Encoding UTF8
}

function Write-CompilerErrorExtract {
    param(
        [string]$SourceLog,
        [string]$DestinationLog
    )

    if (-not (Test-Path $SourceLog)) {
        Set-Content -Path $DestinationLog -Value "" -Encoding UTF8
        return 0
    }

    $pattern = '(?im)^.*error\s+CS\d{4}:.*$|^Scripts have compiler errors\..*$'
    $matches = Select-String -Path $SourceLog -Pattern $pattern -AllMatches

    if ($matches -and $matches.Count -gt 0) {
        $lines = @()
        foreach ($m in $matches) {
            $lines += $m.Line
        }

        $lines | Set-Content -Path $DestinationLog -Encoding UTF8
        return $lines.Count
    }

    Set-Content -Path $DestinationLog -Value "" -Encoding UTF8
    return 0
}

function Write-UniqueLinesFromFile {
    param(
        [string]$SourceFile,
        [string]$DestinationFile
    )

    if (-not (Test-Path $SourceFile)) {
        Set-Content -Path $DestinationFile -Value "" -Encoding UTF8
        return 0
    }

    $seen = New-Object 'System.Collections.Generic.HashSet[string]'
    $unique = New-Object 'System.Collections.Generic.List[string]'

    foreach ($line in Get-Content -Path $SourceFile) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        if ($seen.Add($line)) {
            $unique.Add($line)
        }
    }

    if ($unique.Count -gt 0) {
        $unique | Set-Content -Path $DestinationFile -Encoding UTF8
        return $unique.Count
    }

    Set-Content -Path $DestinationFile -Value "" -Encoding UTF8
    return 0
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$artifactDir = Join-Path $ProjectPath (Join-Path $ArtifactRoot $timestamp)
New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null

$buildLog = Join-Path $artifactDir "build.log"
$editModeLog = Join-Path $artifactDir "editmode.log"
$playModeLog = Join-Path $artifactDir "playmode.log"
$buildCondensedLog = Join-Path $artifactDir "build.condensed.log"
$editModeCondensedLog = Join-Path $artifactDir "editmode.condensed.log"
$playModeCondensedLog = Join-Path $artifactDir "playmode.condensed.log"
$buildErrorsLog = Join-Path $artifactDir "build.errors.log"
$editModeErrorsLog = Join-Path $artifactDir "editmode.errors.log"
$playModeErrorsLog = Join-Path $artifactDir "playmode.errors.log"
$buildErrorsUniqueLog = Join-Path $artifactDir "build.errors.unique.log"
$editModeErrorsUniqueLog = Join-Path $artifactDir "editmode.errors.unique.log"
$playModeErrorsUniqueLog = Join-Path $artifactDir "playmode.errors.unique.log"
$editModeResult = Join-Path $artifactDir "editmode-results.xml"
$playModeResult = Join-Path $artifactDir "playmode-results.xml"
$summaryPath = Join-Path $artifactDir "summary.json"

$summary = [ordered]@{
    unityExe = ""
    build = "skipped"
    editMode = "skipped"
    playMode = "skipped"
    error = ""
    artifactPath = $artifactDir
    buildLog = $buildLog
    editModeLog = $editModeLog
    playModeLog = $playModeLog
    buildCondensedLog = $buildCondensedLog
    editModeCondensedLog = $editModeCondensedLog
    playModeCondensedLog = $playModeCondensedLog
    buildErrorsLog = $buildErrorsLog
    editModeErrorsLog = $editModeErrorsLog
    playModeErrorsLog = $playModeErrorsLog
    buildErrorsUniqueLog = $buildErrorsUniqueLog
    editModeErrorsUniqueLog = $editModeErrorsUniqueLog
    playModeErrorsUniqueLog = $playModeErrorsUniqueLog
    editModeResult = $editModeResult
    playModeResult = $playModeResult
}

$currentStep = "resolve"

try {
    $resolvedUnityExe = Resolve-UnityEditorExe -ProjectPath $ProjectPath -ExplicitUnityExe $UnityExe
    $summary['unityExe'] = $resolvedUnityExe

    if (-not $SkipBuild) {
        $currentStep = "build"
        $buildArgs = @(
            "-batchmode",
            "-nographics",
            "-quit",
            "-projectPath", $ProjectPath,
            "-buildTarget", "Win64",
            "-executeMethod", $BuildMethod,
            "-logFile", $buildLog
        )

        Invoke-UnityCli -UnityExe $resolvedUnityExe -UnityArgs $buildArgs -LogFile $buildLog -TimeoutSec $BuildTimeoutSec -StepName "Build"
        Write-CondensedUnityLog -SourceLog $buildLog -DestinationLog $buildCondensedLog
        $buildCompilerErrorCount = Write-CompilerErrorExtract -SourceLog $buildLog -DestinationLog $buildErrorsLog
        Write-UniqueLinesFromFile -SourceFile $buildErrorsLog -DestinationFile $buildErrorsUniqueLog | Out-Null
        if ($buildCompilerErrorCount -gt 0) {
            throw "Build detected compiler errors ($buildCompilerErrorCount). Check: $buildErrorsLog"
        }
        $summary['build'] = "passed"
    }

    if (-not $SkipEditMode) {
        $currentStep = "editmode"
        $editArgs = @(
            "-batchmode",
            "-nographics",
            "-projectPath", $ProjectPath,
            "-runTests",
            "-testPlatform", "EditMode",
            "-testResults", $editModeResult,
            "-logFile", $editModeLog
        )

        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $editArgs += @("-testFilter", $TestFilter)
        }

        Invoke-UnityCli -UnityExe $resolvedUnityExe -UnityArgs $editArgs -LogFile $editModeLog -TimeoutSec $TestTimeoutSec -StepName "EditMode Tests"
        Write-CondensedUnityLog -SourceLog $editModeLog -DestinationLog $editModeCondensedLog
        $editCompilerErrorCount = Write-CompilerErrorExtract -SourceLog $editModeLog -DestinationLog $editModeErrorsLog
        Write-UniqueLinesFromFile -SourceFile $editModeErrorsLog -DestinationFile $editModeErrorsUniqueLog | Out-Null
        if ($editCompilerErrorCount -gt 0) {
            throw "EditMode Tests detected compiler errors ($editCompilerErrorCount). Check: $editModeErrorsLog"
        }
        if (-not (Test-Path $editModeResult)) {
            throw "EditMode Tests did not produce expected result file: $editModeResult"
        }
        $summary['editMode'] = "passed"
    }

    if (-not $SkipPlayMode) {
        $currentStep = "playmode"
        $playArgs = @(
            "-batchmode",
            "-nographics",
            "-projectPath", $ProjectPath,
            "-runTests",
            "-testPlatform", "PlayMode",
            "-testResults", $playModeResult,
            "-logFile", $playModeLog
        )

        if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
            $playArgs += @("-testFilter", $TestFilter)
        }

        Invoke-UnityCli -UnityExe $resolvedUnityExe -UnityArgs $playArgs -LogFile $playModeLog -TimeoutSec $TestTimeoutSec -StepName "PlayMode Tests"
        Write-CondensedUnityLog -SourceLog $playModeLog -DestinationLog $playModeCondensedLog
        $playCompilerErrorCount = Write-CompilerErrorExtract -SourceLog $playModeLog -DestinationLog $playModeErrorsLog
        Write-UniqueLinesFromFile -SourceFile $playModeErrorsLog -DestinationFile $playModeErrorsUniqueLog | Out-Null
        if ($playCompilerErrorCount -gt 0) {
            throw "PlayMode Tests detected compiler errors ($playCompilerErrorCount). Check: $playModeErrorsLog"
        }
        if (-not (Test-Path $playModeResult)) {
            throw "PlayMode Tests did not produce expected result file: $playModeResult"
        }
        $summary['playMode'] = "passed"
    }
}
catch {
    [Console]::Error.WriteLine([string]$_)

    if ($currentStep -eq "resolve") {
        if (-not $SkipBuild) { $summary['build'] = "failed" }
        if (-not $SkipEditMode) { $summary['editMode'] = "failed" }
        if (-not $SkipPlayMode) { $summary['playMode'] = "failed" }
    }
    elseif ($summary['build'] -eq "skipped" -and -not $SkipBuild) {
        $summary['build'] = "failed"
    }
    elseif ($summary['editMode'] -eq "skipped" -and -not $SkipEditMode) {
        $summary['editMode'] = "failed"
    }
    elseif ($summary['playMode'] -eq "skipped" -and -not $SkipPlayMode) {
        $summary['playMode'] = "failed"
    }

    $summary['error'] = [string]$_
}
finally {
    Write-CondensedUnityLog -SourceLog $buildLog -DestinationLog $buildCondensedLog
    Write-CondensedUnityLog -SourceLog $editModeLog -DestinationLog $editModeCondensedLog
    Write-CondensedUnityLog -SourceLog $playModeLog -DestinationLog $playModeCondensedLog
    Write-CompilerErrorExtract -SourceLog $buildLog -DestinationLog $buildErrorsLog | Out-Null
    Write-CompilerErrorExtract -SourceLog $editModeLog -DestinationLog $editModeErrorsLog | Out-Null
    Write-CompilerErrorExtract -SourceLog $playModeLog -DestinationLog $playModeErrorsLog | Out-Null
    Write-UniqueLinesFromFile -SourceFile $buildErrorsLog -DestinationFile $buildErrorsUniqueLog | Out-Null
    Write-UniqueLinesFromFile -SourceFile $editModeErrorsLog -DestinationFile $editModeErrorsUniqueLog | Out-Null
    Write-UniqueLinesFromFile -SourceFile $playModeErrorsLog -DestinationFile $playModeErrorsUniqueLog | Out-Null

    $summary | ConvertTo-Json -Depth 10 | Set-Content -Path $summaryPath -Encoding UTF8
    Write-Host "[UNITY-CLI] summary: $summaryPath" -ForegroundColor Green
    Write-Host "[UNITY-CLI] artifacts: $artifactDir" -ForegroundColor Green

    if ($summary['build'] -eq "failed" -or $summary['editMode'] -eq "failed" -or $summary['playMode'] -eq "failed") {
        exit 1
    }
}
