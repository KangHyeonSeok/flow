using FlowCore.Agents;
using FlowCore.Agents.Cli;
using FlowCore.Backend;
using FlowCore.Models;
using FluentAssertions;

namespace FlowCore.Tests;

public class CliSpecValidatorTests
{
    /// <summary>테스트용 가짜 백엔드</summary>
    private sealed class FakeBackend : ICliBackend
    {
        public string BackendId => "fake";
        private readonly CliResponse _response;
        public string? LastPrompt { get; private set; }
        public CliBackendOptions? LastOptions { get; private set; }

        public FakeBackend(CliResponse response) => _response = response;

        public Task<CliResponse> RunPromptAsync(
            string prompt, CliBackendOptions options, CancellationToken ct = default)
        {
            LastPrompt = prompt;
            LastOptions = options;
            return Task.FromResult(_response);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static BackendRegistry CreateRegistry(ICliBackend backend)
    {
        var config = new BackendConfig
        {
            AgentBackends = new()
            {
                ["specValidator"] = new AgentBackendMapping { Backend = "fake" }
            },
            Backends = new()
            {
                ["fake"] = new BackendDefinition
                {
                    Command = "test",
                    IdleTimeoutSeconds = 120,
                    HardTimeoutSeconds = 600,
                    AllowedTools = ["Read", "Grep"]
                }
            }
        };
        return new BackendRegistry(config, new Dictionary<string, ICliBackend> { ["fake"] = backend });
    }

    private static AgentInput CreateInput(
        FlowState state = FlowState.Review,
        string? worktreePath = null)
    {
        var assignment = new Assignment
        {
            Id = "asg-001",
            SpecId = "spec-001",
            AgentRole = AgentRole.SpecValidator,
            Type = AssignmentType.SpecValidation,
            Worktree = worktreePath != null
                ? new AssignmentWorktree { Id = "wt-001", Path = worktreePath, Branch = "feature/test" }
                : null
        };
        return new AgentInput
        {
            Spec = new Spec
            {
                Id = "spec-001",
                ProjectId = "proj-001",
                Title = "Test Spec",
                State = state,
                ProcessingStatus = ProcessingStatus.InReview
            },
            Assignment = assignment,
            ProjectId = "proj-001",
            RunId = "run-001",
            CurrentVersion = 3
        };
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulResponse_ReturnsSuccess()
    {
        var backend = new FakeBackend(new CliResponse
        {
            ResponseText = """{"proposedEvent":"specValidationPassed","summary":"모든 AC 충족"}""",
            Success = true,
            StopReason = CliStopReason.Completed
        });
        var validator = new CliSpecValidator(
            CreateRegistry(backend), new PromptBuilder(), new OutputParser());

        var output = await validator.ExecuteAsync(CreateInput());

        output.Result.Should().Be(AgentResult.Success);
        output.ProposedEvent.Should().Be(FlowEvent.SpecValidationPassed);
        output.BaseVersion.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_BackendFailure_ReturnsRetryableFailure()
    {
        var backend = new FakeBackend(new CliResponse
        {
            ResponseText = string.Empty,
            Success = false,
            ErrorMessage = "exit code 1",
            StopReason = CliStopReason.Error
        });
        var validator = new CliSpecValidator(
            CreateRegistry(backend), new PromptBuilder(), new OutputParser());

        var output = await validator.ExecuteAsync(CreateInput());

        output.Result.Should().Be(AgentResult.RetryableFailure);
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutResponse_ReturnsRetryableFailure()
    {
        var backend = new FakeBackend(new CliResponse
        {
            ResponseText = string.Empty,
            Success = false,
            ErrorMessage = "idle timeout",
            StopReason = CliStopReason.Timeout
        });
        var validator = new CliSpecValidator(
            CreateRegistry(backend), new PromptBuilder(), new OutputParser());

        var output = await validator.ExecuteAsync(CreateInput());

        output.Result.Should().Be(AgentResult.RetryableFailure);
    }

    [Fact]
    public async Task ExecuteAsync_UnparsableResponse_ReturnsRetryableFailure()
    {
        var backend = new FakeBackend(new CliResponse
        {
            ResponseText = "이것은 JSON이 아닙니다",
            Success = true,
            StopReason = CliStopReason.Completed
        });
        var validator = new CliSpecValidator(
            CreateRegistry(backend), new PromptBuilder(), new OutputParser());

        var output = await validator.ExecuteAsync(CreateInput());

        output.Result.Should().Be(AgentResult.RetryableFailure);
        output.Message.Should().Contain("parse");
    }

    [Fact]
    public async Task ExecuteAsync_NoBackendConfigured_ReturnsTerminalFailure()
    {
        var emptyConfig = new BackendConfig();
        var registry = new BackendRegistry(emptyConfig, new Dictionary<string, ICliBackend>());
        var validator = new CliSpecValidator(registry, new PromptBuilder(), new OutputParser());

        var output = await validator.ExecuteAsync(CreateInput());

        output.Result.Should().Be(AgentResult.TerminalFailure);
        output.Message.Should().Contain("no backend");
    }

    [Fact]
    public async Task ExecuteAsync_PassesWorktreePathAsWorkingDirectory()
    {
        var backend = new FakeBackend(new CliResponse
        {
            ResponseText = """{"proposedEvent":"specValidationPassed","summary":"ok"}""",
            Success = true,
            StopReason = CliStopReason.Completed
        });
        var validator = new CliSpecValidator(
            CreateRegistry(backend), new PromptBuilder(), new OutputParser());

        await validator.ExecuteAsync(CreateInput(worktreePath: "/tmp/worktree/spec-001"));

        backend.LastOptions!.WorkingDirectory.Should().Be("/tmp/worktree/spec-001");
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesAllowedToolsFromDefinition()
    {
        var backend = new FakeBackend(new CliResponse
        {
            ResponseText = """{"proposedEvent":"specValidationPassed","summary":"ok"}""",
            Success = true,
            StopReason = CliStopReason.Completed
        });
        var validator = new CliSpecValidator(
            CreateRegistry(backend), new PromptBuilder(), new OutputParser());

        await validator.ExecuteAsync(CreateInput());

        backend.LastOptions!.AllowedTools.Should().BeEquivalentTo(["Read", "Grep"]);
    }

    [Fact]
    public async Task ExecuteAsync_PromptContainsWorktreeInfo()
    {
        var backend = new FakeBackend(new CliResponse
        {
            ResponseText = """{"proposedEvent":"specValidationPassed","summary":"ok"}""",
            Success = true,
            StopReason = CliStopReason.Completed
        });
        var validator = new CliSpecValidator(
            CreateRegistry(backend), new PromptBuilder(), new OutputParser());

        await validator.ExecuteAsync(CreateInput(worktreePath: "/tmp/wt"));

        backend.LastPrompt.Should().Contain("/tmp/wt");
        backend.LastPrompt.Should().Contain("feature/test");
    }

    [Fact]
    public async Task ExecuteAsync_DraftState_AcPrecheck()
    {
        var backend = new FakeBackend(new CliResponse
        {
            ResponseText = """{"proposedEvent":"acPrecheckPassed","summary":"AC 적절"}""",
            Success = true,
            StopReason = CliStopReason.Completed
        });
        var validator = new CliSpecValidator(
            CreateRegistry(backend), new PromptBuilder(), new OutputParser());

        var input = CreateInput(state: FlowState.Draft);
        var output = await validator.ExecuteAsync(input);

        output.Result.Should().Be(AgentResult.Success);
        output.ProposedEvent.Should().Be(FlowEvent.AcPrecheckPassed);
        backend.LastPrompt.Should().Contain("AC Precheck");
    }

    [Fact]
    public async Task ExecuteAsync_ReviewRequestInResponse_ParsedCorrectly()
    {
        var backend = new FakeBackend(new CliResponse
        {
            ResponseText = """
                ```json
                {
                  "proposedEvent": "specValidationUserReviewRequested",
                  "summary": "사용자 확인 필요",
                  "proposedReviewRequest": {
                    "summary": "구현 방향 확인 필요",
                    "questions": ["이 구현이 맞습니까?"],
                    "options": [
                      { "id": "approve", "label": "승인", "description": "진행" }
                    ]
                  }
                }
                ```
                """,
            Success = true,
            StopReason = CliStopReason.Completed
        });
        var validator = new CliSpecValidator(
            CreateRegistry(backend), new PromptBuilder(), new OutputParser());

        var output = await validator.ExecuteAsync(CreateInput());

        output.Result.Should().Be(AgentResult.Success);
        output.ProposedEvent.Should().Be(FlowEvent.SpecValidationUserReviewRequested);
        output.ProposedReviewRequest.Should().NotBeNull();
        output.ProposedReviewRequest!.Summary.Should().Be("구현 방향 확인 필요");
    }
}
