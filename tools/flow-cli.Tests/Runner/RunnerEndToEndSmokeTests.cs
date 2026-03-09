using System.Diagnostics;
using FlowCLI.Services.Runner;
using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests.Runner;

public class RunnerEndToEndSmokeTests : IDisposable
{
    private readonly string _tempDir;

    public RunnerEndToEndSmokeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-runner-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task RunDaemon_WithFakeCopilot_TransitionsQueuedToVerifiedWithinSingleCycle()
    {
        const string specId = "F-900";
        var result = await RunScenarioAsync(
            specId,
            "verified",
            spec => string.Equals(spec?.Status, "verified", StringComparison.OrdinalIgnoreCase));

        var spec = result.Spec;
        var observedStatuses = result.ObservedStatuses;
        spec.Should().NotBeNull();
        spec.Status.Should().Be("verified");
        spec.Conditions.Should().ContainSingle();
        spec.Conditions[0].Status.Should().Be("verified");
        spec.Metadata.Should().NotBeNull();
        spec.Metadata!["reviewDisposition"].ToString().Should().Be("review-verified");
        spec.Metadata.ContainsKey("reviewReason").Should().BeFalse();
        spec.Metadata["lastReviewBy"].ToString().Should().Be("fake-copilot");
        spec.Metadata["verificationSource"].ToString().Should().Be("copilot-cli-review");

        observedStatuses.Should().Contain("working");
        observedStatuses.Last().Should().Be("verified");
        IndexOf(observedStatuses, "working").Should().BeLessThan(IndexOf(observedStatuses, "verified"));

        var readmeContent = File.ReadAllText(Path.Combine(_tempDir, "README.md"));
        readmeContent.Should().Contain("implemented by fake copilot");
    }

    [Fact]
    public async Task RunDaemon_WithFakeCopilot_RequeuesWhenReviewMarksAutoTestFailed()
    {
        const string specId = "F-901";
        var result = await RunScenarioAsync(
            specId,
            "test-failed",
            spec => string.Equals(spec?.Status, "queued", StringComparison.OrdinalIgnoreCase)
                && string.Equals(GetMetadataString(spec, "reviewDisposition"), "test-failed", StringComparison.OrdinalIgnoreCase)
                && string.Equals(GetMetadataString(spec, "lastReviewBy"), "fake-copilot", StringComparison.OrdinalIgnoreCase));

        var spec = result.Spec;
        spec.Status.Should().Be("queued");
        spec.Metadata.Should().NotBeNull();
        spec.Metadata!["reviewDisposition"].ToString().Should().Be("test-failed");
        spec.Metadata["reviewReason"].ToString().Should().Be("test-failed");
        spec.Metadata["lastReviewBy"].ToString().Should().Be("fake-copilot");
        spec.Metadata.ContainsKey("verificationSource").Should().BeFalse();

        var condition = spec.Conditions.Should().ContainSingle().Subject;
        condition.Status.Should().Be("draft");
        condition.Tests.Should().ContainSingle();
        condition.Tests[0].Status.Should().Be("failed");
        condition.Evidence.Should().ContainSingle(evidence => evidence.Type == "test-result");

        result.ObservedStatuses.Should().Contain("working");
        result.ObservedStatuses.Should().Contain("queued");
        result.ObservedStatuses.Should().NotContain("verified");
    }

    [Fact]
    public async Task RunDaemon_WithFakeCopilot_KeepsNeedsReviewWhenManualVerificationIsRequired()
    {
        const string specId = "F-902";
        var result = await RunScenarioAsync(
            specId,
            "user-test-required",
            spec => string.Equals(spec?.Status, "needs-review", StringComparison.OrdinalIgnoreCase)
                && string.Equals(GetMetadataString(spec, "reviewDisposition"), "user-test-required", StringComparison.OrdinalIgnoreCase)
                && string.Equals(GetMetadataString(spec, "lastReviewBy"), "fake-copilot", StringComparison.OrdinalIgnoreCase));

        var spec = result.Spec;
        spec.Status.Should().Be("needs-review");
        spec.Metadata.Should().NotBeNull();
        spec.Metadata!["reviewDisposition"].ToString().Should().Be("user-test-required");
        spec.Metadata["reviewReason"].ToString().Should().Be("user-test-required");
        spec.Metadata["lastReviewBy"].ToString().Should().Be("fake-copilot");
        spec.Metadata.ContainsKey("verificationSource").Should().BeFalse();

        var condition = spec.Conditions.Should().ContainSingle().Subject;
        condition.Status.Should().Be("needs-review");
        condition.Tests.Should().ContainSingle();
        condition.Tests[0].Status.Should().Be("passed");
        condition.Evidence.Should().ContainSingle(evidence => evidence.Type == "test-result");
        condition.Metadata.Should().NotBeNull();
        condition.Metadata!["requiresManualVerification"].ToString().Should().Be("True");
        condition.Metadata["manualVerificationReason"].ToString().Should().Be("Requires manual verification");

        result.ObservedStatuses.Should().Contain("working");
        result.ObservedStatuses.Should().Contain("needs-review");
        result.ObservedStatuses.Should().NotContain("verified");
    }

    private async Task<ScenarioResult> RunScenarioAsync(string specId, string scenario, Func<SpecNode?, bool> completionPredicate)
    {
        await InitializeGitRepoAsync(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "runner-scenario.txt"), scenario);
        WriteFakeCopilotScript(_tempDir);

        var store = new SpecStore(_tempDir);
        store.Initialize();
        store.Create(new SpecNode
        {
            Id = specId,
            Title = $"Runner smoke {scenario}",
            Description = $"runner smoke test for {scenario}",
            Status = "queued",
            NodeType = "feature",
            Conditions =
            [
                new SpecCondition
                {
                    Id = $"{specId}-C1",
                    Description = $"condition for {scenario}",
                    Status = "draft"
                }
            ]
        });

        await CommitAllAsync(_tempDir, "seed spec");

        var config = new RunnerConfig
        {
            PollIntervalMinutes = 1,
            ReviewPollIntervalSeconds = 1,
            MaxConcurrentSpecs = 1,
            AutomatedTestsEnabled = false,
            CopilotCliPath = Path.Combine(_tempDir, "fake-copilot.ps1"),
            MainBranch = "main",
            RemoteName = "origin"
        };

        var runner = new RunnerService(_tempDir, config);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var daemonTask = runner.RunDaemonAsync(cts.Token);
        var observedStatuses = await ObserveUntilAsync(store, specId, completionPredicate, cts.Token);

        cts.Cancel();
        await AwaitDaemonStopAsync(daemonTask);

        var spec = store.Get(specId);
        spec.Should().NotBeNull();
        return new ScenarioResult(spec!, observedStatuses);
    }

    private static async Task<List<string>> ObserveUntilAsync(
        SpecStore store,
        string specId,
        Func<SpecNode?, bool> completionPredicate,
        CancellationToken cancellationToken)
    {
        var observed = new List<string>();
        string? lastStatus = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var spec = store.Get(specId);
            var currentStatus = spec?.Status;

            if (!string.IsNullOrWhiteSpace(currentStatus) && !string.Equals(currentStatus, lastStatus, StringComparison.OrdinalIgnoreCase))
            {
                observed.Add(currentStatus);
                lastStatus = currentStatus;
            }

            if (completionPredicate(spec))
            {
                return observed;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new Xunit.Sdk.XunitException($"spec '{specId}' did not reach verified. observed: {string.Join(" -> ", observed)}");
    }

    private static string? GetMetadataString(SpecNode? spec, string key)
    {
        if (spec?.Metadata == null || !spec.Metadata.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        return value.ToString();
    }

    private static async Task AwaitDaemonStopAsync(Task daemonTask)
    {
        var completed = await Task.WhenAny(daemonTask, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().Be(daemonTask, "runner daemon should stop promptly after cancellation");
        await daemonTask;
    }

    private static int IndexOf(List<string> values, string target)
        => values.FindIndex(value => string.Equals(value, target, StringComparison.OrdinalIgnoreCase));

    private static async Task InitializeGitRepoAsync(string root)
    {
        File.WriteAllText(Path.Combine(root, "README.md"), "runner e2e");

        await RunGitAsync("init", root);
        await RunGitAsync("config user.email \"test@test.com\"", root);
        await RunGitAsync("config user.name \"Test\"", root);
        await RunGitAsync("add .", root);
        await RunGitAsync("commit -m \"init\"", root);
        await RunGitAsync("branch -M main", root);
    }

    private static async Task CommitAllAsync(string root, string message)
    {
        await RunGitAsync("add .", root);
        await RunGitAsync($"commit -m \"{message}\"", root);
    }

    private static async Task RunGitAsync(string arguments, string workingDirectory)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Xunit.Sdk.XunitException($"git {arguments} failed: {stderr}\n{stdout}");
        }
    }

    private static void WriteFakeCopilotScript(string root)
    {
        var scriptPath = Path.Combine(root, "fake-copilot.ps1");
        var script = """
param(
    [string]$p,
    [string]$model,
    [switch]$yolo,
    [switch]$autopilot,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs
)

$cwd = (Get-Location).Path

if ($cwd -like '*.flow\worktrees\*') {
    Start-Sleep -Milliseconds 1200
    Add-Content -Path (Join-Path $cwd 'README.md') -Value 'implemented by fake copilot'
    Write-Output 'implemented'
    exit 0
}

Start-Sleep -Milliseconds 400

$scenarioPath = Join-Path $cwd 'runner-scenario.txt'
$scenario = if (Test-Path $scenarioPath) { (Get-Content $scenarioPath -Raw).Trim() } else { 'verified' }

$specPath = Get-ChildItem -Path (Join-Path $cwd 'docs\\specs') -Filter '*.json' |
    Sort-Object Name |
    Select-Object -First 1 -ExpandProperty FullName

$spec = Get-Content $specPath -Raw | ConvertFrom-Json -AsHashtable
$now = [DateTime]::UtcNow.ToString('o')

$verifiedConditionIds = @()
if ($spec['conditions'] -and $spec['conditions'].Count -gt 0) {
    $condition = $spec['conditions'][0]

    if (-not $condition.ContainsKey('metadata') -or $null -eq $condition['metadata']) {
        $condition['metadata'] = @{}
    }

    if (-not $condition.ContainsKey('tests') -or $null -eq $condition['tests']) {
        $condition['tests'] = @()
    }

    if (-not $condition.ContainsKey('evidence') -or $null -eq $condition['evidence']) {
        $condition['evidence'] = @()
    }
}

if (-not $spec.ContainsKey('metadata') -or $null -eq $spec['metadata']) {
    $spec['metadata'] = @{}
}

switch ($scenario) {
    'test-failed' {
        $spec['status'] = 'needs-review'
        if ($condition) {
            $condition['status'] = 'needs-review'
            $condition['tests'] = @(
                @{
                    testId = 'fake-test:failed'
                    name = 'fake failed test'
                    suite = 'fake-copilot'
                    status = 'failed'
                    runAt = $now
                    quarantined = $false
                    errorMessage = 'simulated automated test failure'
                }
            )
            $condition['evidence'] = @(
                @{
                    type = 'test-result'
                    path = 'fake://runner-tests/failed.trx'
                    capturedAt = $now
                    platform = 'fake-dotnet'
                    summary = 'simulated automated test failure'
                }
            )
        }

        $spec['metadata']['review'] = @{
            source = 'copilot-cli-review'
            reviewedAt = $now
            reviewedBy = 'fake-copilot'
            summary = 'auto test failed'
            failureReasons = @('simulated automated test failure')
            alternatives = @()
            suggestedAttempts = @('fix the failing automated test')
            verifiedConditionIds = @()
            additionalInformationRequests = @()
        }
        $spec['metadata']['reviewDisposition'] = 'test-failed'
        $spec['metadata']['reviewReason'] = 'test-failed'
        $spec['metadata'].Remove('verificationSource') | Out-Null
        $spec['metadata'].Remove('lastVerifiedAt') | Out-Null
        $spec['metadata'].Remove('lastVerifiedBy') | Out-Null
    }
    'user-test-required' {
        $spec['status'] = 'needs-review'
        if ($condition) {
            $condition['status'] = 'needs-review'
            $condition['metadata']['requiresManualVerification'] = $true
            $condition['metadata']['manualVerificationReason'] = 'Requires manual verification'
            $condition['metadata']['manualVerificationItems'] = @('Open the screen and verify the final state')
            $condition['tests'] = @(
                @{
                    testId = 'fake-test:passed'
                    name = 'fake passed test'
                    suite = 'fake-copilot'
                    status = 'passed'
                    runAt = $now
                    quarantined = $false
                }
            )
            $condition['evidence'] = @(
                @{
                    type = 'test-result'
                    path = 'fake://runner-tests/passed.trx'
                    capturedAt = $now
                    platform = 'fake-dotnet'
                    summary = 'automated test passed but manual verification remains'
                }
            )
        }

        $spec['metadata']['review'] = @{
            source = 'copilot-cli-review'
            reviewedAt = $now
            reviewedBy = 'fake-copilot'
            summary = 'manual verification required'
            failureReasons = @()
            alternatives = @()
            suggestedAttempts = @('request manual verification')
            verifiedConditionIds = @()
            additionalInformationRequests = @()
        }
        $spec['metadata']['reviewDisposition'] = 'user-test-required'
        $spec['metadata']['reviewReason'] = 'user-test-required'
        $spec['metadata'].Remove('verificationSource') | Out-Null
        $spec['metadata'].Remove('lastVerifiedAt') | Out-Null
        $spec['metadata'].Remove('lastVerifiedBy') | Out-Null
    }
    default {
        $spec['status'] = 'verified'
        if ($condition) {
            $condition['status'] = 'verified'
            $verifiedConditionIds = @($condition['id'])
            $condition['metadata'].Remove('requiresManualVerification') | Out-Null
            $condition['metadata'].Remove('manualVerificationReason') | Out-Null
            $condition['metadata'].Remove('manualVerificationItems') | Out-Null
        }

        $spec['metadata']['review'] = @{
            source = 'copilot-cli-review'
            reviewedAt = $now
            reviewedBy = 'fake-copilot'
            summary = 'review verified'
            failureReasons = @()
            alternatives = @()
            suggestedAttempts = @()
            verifiedConditionIds = $verifiedConditionIds
            additionalInformationRequests = @()
        }
        $spec['metadata']['reviewDisposition'] = 'review-verified'
        $spec['metadata'].Remove('reviewReason') | Out-Null
        $spec['metadata']['verificationSource'] = 'copilot-cli-review'
        $spec['metadata']['lastVerifiedAt'] = $now
        $spec['metadata']['lastVerifiedBy'] = 'fake-copilot'
    }
}

$spec['metadata']['lastReviewAt'] = $now
$spec['metadata']['lastReviewBy'] = 'fake-copilot'
$spec['metadata'].Remove('questionStatus') | Out-Null
$spec['updatedAt'] = $now

$spec | ConvertTo-Json -Depth 30 | Set-Content -Path $specPath -Encoding UTF8
Write-Output '{"success":true}'
""";

        File.WriteAllText(scriptPath, script);
    }

    private sealed record ScenarioResult(SpecNode Spec, List<string> ObservedStatuses);
}