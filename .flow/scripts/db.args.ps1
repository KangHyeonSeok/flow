#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'

function Unquote-Arg {
    param([string]$Value)
    if ([string]::IsNullOrEmpty($Value)) {
        return $Value
    }
    if (($Value.StartsWith('"') -and $Value.EndsWith('"')) -or ($Value.StartsWith("'") -and $Value.EndsWith("'"))) {
        $inner = $Value.Substring(1, $Value.Length - 2)
        if ($Value.StartsWith("'")) {
            return $inner -replace "''", "'"
        }
        return $inner -replace '""', '"'
    }
    return $Value
}

function Get-ArgsFromLine {
    param(
        [string]$Line,
        [string]$ScriptPath
    )
    if ([string]::IsNullOrWhiteSpace($Line)) {
        return @()
    }

    $nullRef = $null
    $tokens = [System.Management.Automation.PSParser]::Tokenize($Line, [ref]$nullRef)
    if (-not $tokens) {
        return @()
    }

    $scriptName = [System.IO.Path]::GetFileName($ScriptPath)
    $startIndex = -1

    for ($i = 0; $i -lt $tokens.Count; $i++) {
        $content = $tokens[$i].Content
        if (-not $content) { continue }
        if ($content -like "*$scriptName") {
            $startIndex = $i
            break
        }
    }

    if ($startIndex -lt 0) {
        return @()
    }

    $argsList = New-Object System.Collections.Generic.List[string]
    for ($j = $startIndex + 1; $j -lt $tokens.Count; $j++) {
        $t = $tokens[$j]
        if ($t.Type -eq 'StatementSeparator') { break }
        if ($t.Type -in @('CommandArgument', 'String', 'Number', 'Parameter')) {
            $argsList.Add($t.Content)
        }
    }

    return $argsList.ToArray()
}

function Parse-DbArgs {
    param(
        [string[]]$Args,
        [string]$InvocationLine,
        [string]$ScriptPath
    )

    $result = [ordered]@{
        Add = $null
        Query = $null
        Tags = $null
        Metadata = "{}"
        TopK = 5
        DbPath = $null
        Init = $false
        ShowHelp = $false
        HasArgs = $false
    }

    $parsedArgs = $Args
    if (-not $parsedArgs -or $parsedArgs.Count -eq 0) {
        $parsedArgs = Get-ArgsFromLine -Line $InvocationLine -ScriptPath $ScriptPath
    }

    if ($parsedArgs -and $parsedArgs.Count -gt 0) {
        $result.HasArgs = $true
    }

    function Get-NextArgValue {
        param(
            [string[]]$InputArgs,
            [int]$Index
        )
        if ($Index + 1 -ge $InputArgs.Count) {
            Write-Host "옵션 값이 필요합니다: $($InputArgs[$Index])" -ForegroundColor Yellow
            $result.ShowHelp = $true
            return $null
        }
        return Unquote-Arg -Value $InputArgs[$Index + 1]
    }

    for ($i = 0; $i -lt $parsedArgs.Count; $i++) {
        $arg = $parsedArgs[$i]
        if (-not $arg) { continue }

        if ($arg -match '^-{1,2}([^=]+)=(.*)$') {
            $key = $Matches[1].ToLowerInvariant()
            $value = Unquote-Arg -Value $Matches[2]

            switch ($key) {
                'add' { $result.Add = $value }
                'query' { $result.Query = $value }
                'tags' { $result.Tags = $value }
                'metadata' { $result.Metadata = $value }
                'topk' { $result.TopK = [int]$value }
                'db' { $result.DbPath = $value }
                'dbpath' { $result.DbPath = $value }
                'init' { $result.Init = $true }
                'help' { $result.ShowHelp = $true }
                'h' { $result.ShowHelp = $true }
                default {
                    Write-Host "알 수 없는 옵션: $arg" -ForegroundColor Yellow
                    $result.ShowHelp = $true
                }
            }
            continue
        }

        if ($arg -match '^-{1,2}.+') {
            $key = ($arg -replace '^-{1,2}', '').ToLowerInvariant()
            switch ($key) {
                'add' {
                    $result.Add = Get-NextArgValue -InputArgs $parsedArgs -Index $i
                    $i++
                }
                'query' {
                    $result.Query = Get-NextArgValue -InputArgs $parsedArgs -Index $i
                    $i++
                }
                'tags' {
                    $result.Tags = Get-NextArgValue -InputArgs $parsedArgs -Index $i
                    $i++
                }
                'metadata' {
                    $result.Metadata = Get-NextArgValue -InputArgs $parsedArgs -Index $i
                    $i++
                }
                'topk' {
                    $result.TopK = [int](Get-NextArgValue -InputArgs $parsedArgs -Index $i)
                    $i++
                }
                'db' {
                    $result.DbPath = Get-NextArgValue -InputArgs $parsedArgs -Index $i
                    $i++
                }
                'dbpath' {
                    $result.DbPath = Get-NextArgValue -InputArgs $parsedArgs -Index $i
                    $i++
                }
                'init' { $result.Init = $true }
                'help' { $result.ShowHelp = $true }
                'h' { $result.ShowHelp = $true }
                default {
                    Write-Host "알 수 없는 옵션: $arg" -ForegroundColor Yellow
                    $result.ShowHelp = $true
                }
            }
        }
        else {
            if (-not $result.Add -and -not $result.Query -and -not $result.Init) {
                $result.Add = Unquote-Arg -Value $arg
            }
            elseif ($result.Add) {
                $result.Add = ($result.Add + " " + (Unquote-Arg -Value $arg)).Trim()
            }
            elseif ($result.Query) {
                $result.Query = ($result.Query + " " + (Unquote-Arg -Value $arg)).Trim()
            }
        }
    }

    return [pscustomobject]$result
}
