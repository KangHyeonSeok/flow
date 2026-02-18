#!/usr/bin/env pwsh
if ($args.Count -gt 0 -and $args[0] -eq "capture") {
	$captureArgs = if ($args.Count -gt 1) { @($args | Select-Object -Skip 1) } else { @() }
	$captureProcess = Start-Process -FilePath "$PSScriptRoot/.flow/bin/capture.exe" -ArgumentList $captureArgs -NoNewWindow -Wait -PassThru
	exit $captureProcess.ExitCode
}

& "$PSScriptRoot/.flow/bin/flow.exe" @args
