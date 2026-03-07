#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run flow-cli integration tests for db-add with delayed search

.DESCRIPTION
    Executes DbAddIntegrationTests which test adding 5 documents to the database,
    waiting 1 minute, and then querying for them.
    
    Note: These tests take at least 2 minutes to run due to the deliberate delays.

.PARAMETER Filter
    Optional test filter to run specific tests. Default runs all DbAddIntegrationTests.

.PARAMETER Verbose
    Enable verbose output from dotnet test

.EXAMPLE
    .\run-integration-tests.ps1
    Run all integration tests in DbAddIntegrationTests class

.EXAMPLE
    .\run-integration-tests.ps1 -Filter "DbAdd_FiveDocuments_WaitOneMinute_CanQueryAll"
    Run only the specific test method

.EXAMPLE
    .\run-integration-tests.ps1 -Verbose
    Run tests with detailed output
#>

param(
    [string]$Filter = "DbAddIntegrationTests",
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"

# Get script directory and project root
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $scriptDir "flow-cli.Tests.csproj"

# Verify project exists
if (-not (Test-Path $projectPath)) {
    Write-Error "Project file not found: $projectPath"
    exit 1
}

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "Flow CLI Integration Tests - DB Add with Delayed Search" -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Project: $projectPath" -ForegroundColor Gray
Write-Host "Filter:  $Filter" -ForegroundColor Gray
Write-Host ""
Write-Host "WARNING: These tests include 1-minute delays and will take ~2+ minutes to complete." -ForegroundColor Yellow
Write-Host ""

# Build test arguments
$testArgs = @(
    "test"
    $projectPath
    "--filter", $Filter
    "--logger", "console;verbosity=normal"
)

if ($Verbose) {
    $testArgs += "--verbosity", "detailed"
}

# Record start time
$startTime = Get-Date

# Run tests
Write-Host "Starting tests..." -ForegroundColor Green
Write-Host ""

try {
    & dotnet @testArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Error "Tests failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }
    
    # Calculate duration
    $endTime = Get-Date
    $duration = $endTime - $startTime
    
    Write-Host ""
    Write-Host "==================================================================" -ForegroundColor Green
    Write-Host "All tests passed!" -ForegroundColor Green
    Write-Host "Duration: $($duration.ToString('mm\:ss'))" -ForegroundColor Gray
    Write-Host "==================================================================" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Error "Test execution failed: $_"
    exit 1
}
