using System.Diagnostics;
using System.Text.Json;
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
        await InitializeGitRepoAsync(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "runner-scenario.txt"), "test-failed");
        WriteFakeCopilotScript(_tempDir);

        var store = new SpecStore(_tempDir);
        store.Initialize();
        store.Create(CreateFeatureSpec(specId));
        await CommitAllAsync(_tempDir, "seed spec");

        var runner = new RunnerService(_tempDir, new RunnerConfig
        {
            MaxConcurrentSpecs = 1,
            CopilotCliPath = Path.Combine(_tempDir, "fake-copilot.ps1"),
            MainBranch = "main",
            RemoteName = "origin"
        });

        var firstCycle = await runner.RunOnceAsync();
        firstCycle.Should().ContainSingle();
        firstCycle[0].Action.Should().Be("handoff-review");

        var secondCycle = await runner.RunOnceAsync();
        secondCycle.Should().ContainSingle();
        secondCycle[0].Action.Should().Be("handoff-review");

        var thirdCycle = await runner.RunOnceAsync();
        thirdCycle.Should().ContainSingle();
        thirdCycle[0].Action.Should().Be("requeue");

        var spec = store.Get(specId);
        spec.Should().NotBeNull();
        spec = spec!;
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

    [Fact]
    public async Task RunDaemon_WithFakeCopilot_ClassifiesRateLimitAndSchedulesCooldown()
    {
        const string specId = "F-903";
        var startedAt = DateTime.UtcNow;
        var result = await RunScenarioAsync(
            specId,
            "rate-limited",
            spec => string.Equals(spec?.Status, "queued", StringComparison.OrdinalIgnoreCase)
                && string.Equals(GetMetadataString(spec, "reviewDisposition"), "rate-limited", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(GetMetadataString(spec, "retryNotBefore")));

        var spec = result.Spec;
        spec.Status.Should().Be("queued");
        spec.Metadata!["reviewDisposition"].ToString().Should().Be("rate-limited");
        spec.Metadata["reviewReason"].ToString().Should().Be("rate-limited");
        spec.Metadata["lastErrorType"].ToString().Should().Be("rate-limited");
        DateTime.Parse(spec.Metadata["retryNotBefore"].ToString()!).Should().BeAfter(startedAt);

        result.ObservedStatuses.Should().Contain("working");
        result.ObservedStatuses.Should().Contain("queued");
    }

    [Fact]
    public async Task RunDaemon_WithFakeCopilot_ClassifiesTransportErrorAndSchedulesCooldown()
    {
        const string specId = "F-904";
        var startedAt = DateTime.UtcNow;
        var result = await RunScenarioAsync(
            specId,
            "transport-error",
            spec => string.Equals(spec?.Status, "queued", StringComparison.OrdinalIgnoreCase)
                && string.Equals(GetMetadataString(spec, "reviewDisposition"), "transport-error", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(GetMetadataString(spec, "retryNotBefore")));

        var spec = result.Spec;
        spec.Status.Should().Be("queued");
        spec.Metadata!["reviewDisposition"].ToString().Should().Be("transport-error");
        spec.Metadata["reviewReason"].ToString().Should().Be("transport-error");
        spec.Metadata["lastErrorType"].ToString().Should().Be("transport-error");
        DateTime.Parse(spec.Metadata["retryNotBefore"].ToString()!).Should().BeAfter(startedAt);

        result.ObservedStatuses.Should().Contain("working");
        result.ObservedStatuses.Should().Contain("queued");
    }

    [Fact]
    public async Task RunOnce_StagesReviewSoNextImplementationRunsBeforePreviousReview()
    {
        await InitializeGitRepoAsync(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "runner-scenario.txt"), "verified");
        WriteFakeCopilotScript(_tempDir);

        var store = new SpecStore(_tempDir);
        store.Initialize();
        store.Create(CreateFeatureSpec("F-910"));
        store.Create(CreateFeatureSpec("F-911"));

        await CommitAllAsync(_tempDir, "seed specs");

        var runner = new RunnerService(_tempDir, new RunnerConfig
        {
            MaxConcurrentSpecs = 1,
            CopilotCliPath = Path.Combine(_tempDir, "fake-copilot.ps1"),
            MainBranch = "main",
            RemoteName = "origin"
        });

        var firstCycle = await runner.RunOnceAsync();
        firstCycle.Should().ContainSingle();
        firstCycle[0].SpecId.Should().Be("F-910");
        firstCycle[0].Action.Should().Be("handoff-review");

        var firstSpecAfterCycle1 = store.Get("F-910");
        var secondSpecAfterCycle1 = store.Get("F-911");
        firstSpecAfterCycle1.Should().NotBeNull();
        secondSpecAfterCycle1.Should().NotBeNull();
        firstSpecAfterCycle1!.Status.Should().Be("working");
        firstSpecAfterCycle1.Metadata!["runnerStage"].ToString().Should().Be("test-validation");
        secondSpecAfterCycle1!.Status.Should().Be("queued");

        var secondCycle = await runner.RunOnceAsync();
        secondCycle.Should().HaveCount(2);
        secondCycle[0].SpecId.Should().Be("F-911");
        secondCycle[0].Action.Should().Be("handoff-review");
        secondCycle[1].SpecId.Should().Be("F-910");
        secondCycle[1].Action.Should().Be("handoff-review");
        secondCycle[1].TriggeredReschedule.Should().BeTrue();

        store.Get("F-910")!.Status.Should().Be("working");
        store.Get("F-910")!.Metadata!["runnerStage"].ToString().Should().Be("review");
        store.Get("F-911")!.Status.Should().Be("working");
        store.Get("F-911")!.Metadata!["runnerStage"].ToString().Should().Be("test-validation");

        var thirdCycle = await runner.RunOnceAsync();
        thirdCycle.Should().ContainSingle();
        thirdCycle[0].SpecId.Should().Be("F-911");
        thirdCycle[0].Action.Should().Be("handoff-review");

        var fourthCycle = await runner.RunOnceAsync();
        fourthCycle.Should().ContainSingle();
        fourthCycle[0].SpecId.Should().Be("F-910");
        fourthCycle[0].Action.Should().Be("verify");

        store.Get("F-910")!.Status.Should().Be("verified");
        store.Get("F-911")!.Status.Should().Be("working");
        store.Get("F-911")!.Metadata!["runnerStage"].ToString().Should().Be("review");
    }

    private static SpecNode CreateFeatureSpec(string specId) => new()
    {
        Id = specId,
        Title = $"Runner smoke {specId}",
        Description = $"runner smoke test for {specId}",
        Status = "queued",
        NodeType = "feature",
        Conditions =
        [
            new SpecCondition
            {
                Id = $"{specId}-C1",
                Description = $"condition for {specId}",
                Status = "draft"
            }
        ]
    };

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
            PollIntervalMinutes = 0,
            ReviewPollIntervalSeconds = 1,
            MaxConcurrentSpecs = 1,
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
            SpecNode? spec;
            try
            {
                spec = store.Get(specId);
            }
            catch (JsonException)
            {
                await Task.Delay(50, cancellationToken);
                continue;
            }

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

function Find-ScenarioPath([string]$startDir) {
    $cursor = $startDir
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        $candidate = Join-Path $cursor 'runner-scenario.txt'
        if (Test-Path $candidate) {
            return $candidate
        }

        $parent = Split-Path $cursor -Parent
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) {
            break
        }

        $cursor = $parent
    }

    return $null
}

$scenarioPath = Find-ScenarioPath $cwd
$scenario = if ($scenarioPath) { (Get-Content $scenarioPath -Raw).Trim() } else { 'verified' }

if ($cwd -like '*.flow\worktrees\*') {
    switch ($scenario) {
        'rate-limited' {
            Write-Error '429 Too Many Requests: retry after 60 seconds'
            exit 1
        }
        'transport-error' {
            Write-Error 'ECONNRESET: connection reset by peer'
            exit 1
        }
    }

    Start-Sleep -Milliseconds 1200
    Add-Content -Path (Join-Path $cwd 'README.md') -Value 'implemented by fake copilot'
    Write-Output 'implemented'
    exit 0
}

Start-Sleep -Milliseconds 400

$projectName = Split-Path $cwd -Leaf
$specId = $null
if ($p) {
    $specIdMatch = [regex]::Match($p, '"id"\s*:\s*"([^"]+)"')
    if ($specIdMatch.Success) {
        $specId = $specIdMatch.Groups[1].Value
    }
}
$candidateSpecDirs = @(
    (Join-Path (Join-Path $HOME '.flow') (Join-Path $projectName 'specs')),
    (Join-Path $cwd 'docs\\specs')
)

$specPath = $null
foreach ($candidateDir in $candidateSpecDirs) {
    if (-not (Test-Path $candidateDir)) {
        continue
    }

    if ($specId) {
        $candidatePath = Join-Path $candidateDir ("{0}.json" -f $specId)
        if (Test-Path $candidatePath) {
            $specPath = $candidatePath
            break
        }
    }

    $specPath = Get-ChildItem -Path $candidateDir -Filter '*.json' |
        Sort-Object Name |
        Select-Object -First 1 -ExpandProperty FullName

    if ($specPath) {
        break
    }
}

if (-not $specPath) {
    throw "Unable to locate spec JSON under shared or local spec directories."
}

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
