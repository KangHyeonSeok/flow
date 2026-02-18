#!/usr/bin/env pwsh
function Resolve-PythonCommand {
	$workspaceVenvPython = Join-Path $PSScriptRoot ".venv/Scripts/python.exe"
	if (Test-Path $workspaceVenvPython) {
		return @($workspaceVenvPython)
	}

	$workspaceVenvUnixPython = Join-Path $PSScriptRoot ".venv/bin/python"
	if (Test-Path $workspaceVenvUnixPython) {
		return @($workspaceVenvUnixPython)
	}

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

if ($args.Count -gt 0 -and $args[0] -eq "capture") {
	$captureArgs = if ($args.Count -gt 1) { @($args | Select-Object -Skip 1) } else { @() }
	$captureProcess = Start-Process -FilePath "$PSScriptRoot/.flow/bin/capture.exe" -ArgumentList $captureArgs -NoNewWindow -Wait -PassThru
	exit $captureProcess.ExitCode
}

if ($args.Count -gt 0 -and $args[0] -eq "vlm") {
	$vlmArgs = if ($args.Count -gt 1) { @($args | Select-Object -Skip 1) } else { @() }
	$vlmScript = Join-Path $PSScriptRoot ".flow/bin/gemini_vlm.py"

	if (-not (Test-Path $vlmScript)) {
		Write-Error "VLM script not found: $vlmScript"
		exit 1
	}

	$pythonCommand = @(Resolve-PythonCommand)
	if (-not $pythonCommand) {
		Write-Error "Python executable not found. Run install/update script to auto-setup .venv, or install Python 3.12+ and retry."
		exit 1
	}

	$extraArgs = @()
	if ($pythonCommand.Length -gt 1) {
		$extraArgs = $pythonCommand[1..($pythonCommand.Length - 1)]
	}

	& $pythonCommand[0] @($extraArgs + @($vlmScript) + $vlmArgs)
	exit $LASTEXITCODE
}

& "$PSScriptRoot/.flow/bin/flow.exe" @args
