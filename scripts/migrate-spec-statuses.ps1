param(
    [string]$SpecsDir = (Join-Path $PSScriptRoot "..\.flow\spec-cache\specs"),
    [switch]$NoBackup
)

$ErrorActionPreference = 'Stop'

function Get-NewStatus {
    param([string]$Status)

    switch ($Status) {
        'requested' { return 'queued' }
        'context-gathering' { return 'queued' }
        'plan' { return 'queued' }
        'active' { return 'working' }
        'in-progress' { return 'working' }
        default { return $Status }
    }
}

function Update-StatusesRecursively {
    param(
        [object]$Node,
        [hashtable]$Counters
    )

    if ($null -eq $Node) {
        return
    }

    if ($Node -is [System.Collections.IDictionary]) {
        if ($Node.Contains('status') -and $Node['status'] -is [string]) {
            $oldStatus = [string]$Node['status']
            $newStatus = Get-NewStatus -Status $oldStatus
            if ($oldStatus -ne $newStatus) {
                $Node['status'] = $newStatus
                if (-not $Counters.ContainsKey($oldStatus)) {
                    $Counters[$oldStatus] = 0
                }
                $Counters[$oldStatus]++
            }
        }

        foreach ($key in @($Node.Keys)) {
            Update-StatusesRecursively -Node $Node[$key] -Counters $Counters
        }
        return
    }

    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        foreach ($item in $Node) {
            Update-StatusesRecursively -Node $item -Counters $Counters
        }
    }
}

$resolvedSpecsDir = [System.IO.Path]::GetFullPath((Resolve-Path $SpecsDir).Path)
if (-not (Test-Path $resolvedSpecsDir)) {
    throw "Specs directory not found: $resolvedSpecsDir"
}

$specFiles = Get-ChildItem -Path $resolvedSpecsDir -File -Filter 'F-*.json' | Sort-Object Name
if ($specFiles.Count -eq 0) {
    Write-Host "No spec files found in $resolvedSpecsDir"
    exit 0
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupDir = Join-Path $resolvedSpecsDir ".migration-backup\$timestamp"
if (-not $NoBackup) {
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
}

$fileCount = 0
$changedFileCount = 0
$statusCounters = @{}
$failedFiles = New-Object System.Collections.Generic.List[string]

foreach ($file in $specFiles) {
    $fileCount++
    try {
        $raw = Get-Content -Path $file.FullName -Raw -Encoding UTF8
        $json = $raw | ConvertFrom-Json -Depth 100 -AsHashtable
    }
    catch {
        $failedFiles.Add("$($file.Name): JSON parse failed - $($_.Exception.Message)")
        continue
    }

    if ($null -eq $json) {
        $failedFiles.Add("$($file.Name): JSON content resolved to null")
        continue
    }

    $fileCounters = @{}

    Update-StatusesRecursively -Node $json -Counters $fileCounters

    if ($fileCounters.Count -eq 0) {
        continue
    }

    if (-not $NoBackup) {
        Copy-Item -Path $file.FullName -Destination (Join-Path $backupDir $file.Name) -Force
    }

    $changedFileCount++
    foreach ($entry in $fileCounters.GetEnumerator()) {
        if (-not $statusCounters.ContainsKey($entry.Key)) {
            $statusCounters[$entry.Key] = 0
        }
        $statusCounters[$entry.Key] += $entry.Value
    }

    $formatted = $json | ConvertTo-Json -Depth 100
    [System.IO.File]::WriteAllText($file.FullName, $formatted + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Updated $($file.Name)"
}

Write-Host ""
Write-Host "Migration complete"
Write-Host "- Specs directory: $resolvedSpecsDir"
Write-Host "- Files scanned: $fileCount"
Write-Host "- Files changed: $changedFileCount"
if (-not $NoBackup) {
    Write-Host "- Backup: $backupDir"
}

if ($statusCounters.Count -gt 0) {
    Write-Host "- Status replacements:"
    foreach ($entry in $statusCounters.GetEnumerator() | Sort-Object Name) {
        Write-Host "  $($entry.Key) -> $(Get-NewStatus -Status $entry.Key): $($entry.Value)"
    }
}

if ($failedFiles.Count -gt 0) {
    Write-Host "- Skipped files:"
    foreach ($failed in $failedFiles) {
        Write-Host "  $failed"
    }
}