using FlowCore.Agents;
using FlowCore.Agents.Cli;
using FlowCore.Backend;
using FlowCore.Models;
using FlowCore.Planning;
using FlowCore.Runner;
using FluentAssertions;

namespace FlowCore.Tests;

/// <summary>
/// Planner-specific tests: ProposedSpec parsing, planning dispatch priority,
/// ApplyProposedSpec field replacement, runner contract enforcement,
/// and Path B PlannerOutputParser.
/// </summary>
public class PlannerTests
{
    private static readonly OutputParser Parser = new();

    // ── Helper factories ──

    private static AgentInput CreatePlanningInput(
        FlowState state = FlowState.Draft,
        ProcessingStatus processingStatus = ProcessingStatus.Pending,
        int version = 3)
    {
        return new AgentInput
        {
            Spec = new Spec
            {
                Id = "spec-001", ProjectId = "proj-001",
                Title = "Original Title",
                Problem = "Original Problem",
                Goal = "Original Goal",
                State = state, ProcessingStatus = processingStatus,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                Version = version
            },
            Assignment = new Assignment
            {
                Id = "asg-001", SpecId = "spec-001",
                AgentRole = AgentRole.Planner,
                Type = AssignmentType.Planning,
                Status = AssignmentStatus.Running
            },
            ProjectId = "proj-001",
            RunId = "run-001",
            CurrentVersion = version
        };
    }

    private static AgentInput CreateNonPlanningInput(int version = 5)
    {
        return new AgentInput
        {
            Spec = new Spec
            {
                Id = "spec-001", ProjectId = "proj-001",
                Title = "Test Spec",
                State = FlowState.Review,
                ProcessingStatus = ProcessingStatus.InReview,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                Version = version
            },
            Assignment = new Assignment
            {
                Id = "asg-001", SpecId = "spec-001",
                AgentRole = AgentRole.SpecValidator,
                Type = AssignmentType.SpecValidation
            },
            ProjectId = "proj-001",
            RunId = "run-001",
            CurrentVersion = version
        };
    }

    private static Spec MakeSpec(
        string id = "spec-001",
        FlowState state = FlowState.Draft,
        ProcessingStatus processingStatus = ProcessingStatus.Pending,
        RiskLevel riskLevel = RiskLevel.Low) => new()
    {
        Id = id, ProjectId = "proj-001", Title = "Test",
        State = state, ProcessingStatus = processingStatus,
        RiskLevel = riskLevel,
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        Version = 1
    };

    // ═══════════════════════════════════════════
    //  1. OutputParser — ProposedSpec parsing
    // ═══════════════════════════════════════════

    [Fact]
    public void Parse_ProposedSpec_FullPayload_ParsesAllFields()
    {
        var response = new CliResponse
        {
            ResponseText = """
                ```json
                {
                  "proposedEvent": "draftUpdated",
                  "summary": "AC를 구체화했습니다.",
                  "proposedSpec": {
                    "title": "구체화된 스펙",
                    "type": "feature",
                    "problem": "해결할 문제",
                    "goal": "달성 목표",
                    "acceptanceCriteria": [
                      { "text": "Given X When Y Then Z", "testable": true, "notes": "참고" },
                      { "text": "Second AC", "testable": false }
                    ],
                    "riskLevel": "medium",
                    "dependsOn": ["spec-100", "spec-200"]
                  }
                }
                ```
                """,
            Success = true
        };

        var output = Parser.Parse(response, CreatePlanningInput());

        output.Should().NotBeNull();
        output!.Result.Should().Be(AgentResult.Success);
        output.ProposedEvent.Should().Be(FlowEvent.DraftUpdated);
        output.ProposedSpec.Should().NotBeNull();

        var ps = output.ProposedSpec!;
        ps.Title.Should().Be("구체화된 스펙");
        ps.Type.Should().Be(SpecType.Feature);
        ps.Problem.Should().Be("해결할 문제");
        ps.Goal.Should().Be("달성 목표");
        ps.RiskLevel.Should().Be(RiskLevel.Medium);
        ps.DependsOn.Should().BeEquivalentTo(["spec-100", "spec-200"]);

        ps.AcceptanceCriteria.Should().HaveCount(2);
        ps.AcceptanceCriteria![0].Text.Should().Be("Given X When Y Then Z");
        ps.AcceptanceCriteria[0].Testable.Should().BeTrue();
        ps.AcceptanceCriteria[0].Notes.Should().Be("참고");
        ps.AcceptanceCriteria[1].Testable.Should().BeFalse();
    }

    [Fact]
    public void Parse_ProposedSpec_NullFields_ParsesPartialPayload()
    {
        var response = new CliResponse
        {
            ResponseText = """
                ```json
                {
                  "proposedEvent": "draftUpdated",
                  "summary": "제목만 변경",
                  "proposedSpec": {
                    "title": "새 제목"
                  }
                }
                ```
                """,
            Success = true
        };

        var output = Parser.Parse(response, CreatePlanningInput());
        output.Should().NotBeNull();
        output!.ProposedSpec.Should().NotBeNull();
        output.ProposedSpec!.Title.Should().Be("새 제목");
        output.ProposedSpec.Type.Should().BeNull();
        output.ProposedSpec.Problem.Should().BeNull();
        output.ProposedSpec.AcceptanceCriteria.Should().BeNull();
    }

    [Fact]
    public void Parse_PlanningAssignment_DraftUpdated_WithoutProposedSpec_ReturnsNull()
    {
        var response = new CliResponse
        {
            ResponseText = """
                ```json
                {
                  "proposedEvent": "draftUpdated",
                  "summary": "no spec payload"
                }
                ```
                """,
            Success = true
        };

        var output = Parser.Parse(response, CreatePlanningInput());
        output.Should().BeNull("Planning assignment requires ProposedSpec for DraftUpdated");
    }

    [Fact]
    public void Parse_PlanningAssignment_DraftCreated_WithoutProposedSpec_ReturnsNull()
    {
        var response = new CliResponse
        {
            ResponseText = """
                ```json
                {
                  "proposedEvent": "draftCreated",
                  "summary": "no spec payload"
                }
                ```
                """,
            Success = true
        };

        var output = Parser.Parse(response, CreatePlanningInput());
        output.Should().BeNull("Planning assignment requires ProposedSpec for DraftCreated");
    }

    [Fact]
    public void Parse_NonPlanningAssignment_DraftUpdated_WithoutProposedSpec_Succeeds()
    {
        // Non-planning agents returning DraftUpdated (edge case) should not be rejected by parser
        var response = new CliResponse
        {
            ResponseText = """
                ```json
                {
                  "proposedEvent": "specValidationPassed",
                  "summary": "all good"
                }
                ```
                """,
            Success = true
        };

        var output = Parser.Parse(response, CreateNonPlanningInput());
        output.Should().NotBeNull();
        output!.ProposedSpec.Should().BeNull();
    }

    [Fact]
    public void Parse_ProposedSpec_EmptyAcText_Filtered()
    {
        var response = new CliResponse
        {
            ResponseText = """
                ```json
                {
                  "proposedEvent": "draftUpdated",
                  "summary": "test",
                  "proposedSpec": {
                    "title": "Title",
                    "acceptanceCriteria": [
                      { "text": "Valid AC" },
                      { "text": "" },
                      { "text": "  " },
                      { "text": "Another valid AC" }
                    ]
                  }
                }
                ```
                """,
            Success = true
        };

        var output = Parser.Parse(response, CreatePlanningInput());
        output.Should().NotBeNull();
        output!.ProposedSpec!.AcceptanceCriteria.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_ProposedSpec_TestableDefaultsToTrue()
    {
        var response = new CliResponse
        {
            ResponseText = """
                ```json
                {
                  "proposedEvent": "draftUpdated",
                  "summary": "test",
                  "proposedSpec": {
                    "title": "Title",
                    "acceptanceCriteria": [
                      { "text": "AC without testable field" }
                    ]
                  }
                }
                ```
                """,
            Success = true
        };

        var output = Parser.Parse(response, CreatePlanningInput());
        output!.ProposedSpec!.AcceptanceCriteria![0].Testable.Should().BeTrue();
    }

    // ═══════════════════════════════════════════
    //  2. DispatchTable — Planning assignment priority
    // ═══════════════════════════════════════════

    [Fact]
    public void Decide_OpenPlanningAssignment_QueuedStatus_DispatchesPlanner()
    {
        var spec = MakeSpec(state: FlowState.Draft, processingStatus: ProcessingStatus.Pending);
        var planningAssignment = new Assignment
        {
            Id = "asg-plan", SpecId = "spec-001",
            AgentRole = AgentRole.Planner,
            Type = AssignmentType.Planning,
            Status = AssignmentStatus.Queued
        };

        var decision = DispatchTable.Decide(spec, [planningAssignment], []);

        decision.Kind.Should().Be(DispatchKind.Agent);
        decision.AgentRole.Should().Be(AgentRole.Planner);
        decision.AssignmentType.Should().Be(AssignmentType.Planning);
    }

    [Fact]
    public void Decide_OpenPlanningAssignment_OverridesNormalDraftDispatch()
    {
        // Without planning assignment, Draft/Pending → SpecValidator
        // With planning assignment, should dispatch Planner instead
        var spec = MakeSpec(state: FlowState.Draft, processingStatus: ProcessingStatus.Pending);
        var planningAssignment = new Assignment
        {
            Id = "asg-plan", SpecId = "spec-001",
            AgentRole = AgentRole.Planner,
            Type = AssignmentType.Planning,
            Status = AssignmentStatus.Queued
        };

        var withPlanning = DispatchTable.Decide(spec, [planningAssignment], []);
        var withoutPlanning = DispatchTable.Decide(spec, [], []);

        withPlanning.AgentRole.Should().Be(AgentRole.Planner);
        withoutPlanning.AgentRole.Should().Be(AgentRole.SpecValidator);
    }

    [Fact]
    public void Decide_RunningPlanningAssignment_WithActiveAssignment_Waits()
    {
        var spec = MakeSpec(state: FlowState.Draft, processingStatus: ProcessingStatus.Pending);
        var planningAssignment = new Assignment
        {
            Id = "asg-plan", SpecId = "spec-001",
            AgentRole = AgentRole.Planner,
            Type = AssignmentType.Planning,
            Status = AssignmentStatus.Running
        };

        var decision = DispatchTable.Decide(spec, [planningAssignment], []);

        decision.Kind.Should().Be(DispatchKind.Wait);
    }

    [Fact]
    public void Decide_CompletedPlanningAssignment_FallsThrough()
    {
        var spec = MakeSpec(state: FlowState.Draft, processingStatus: ProcessingStatus.Pending);
        var completedAssignment = new Assignment
        {
            Id = "asg-plan", SpecId = "spec-001",
            AgentRole = AgentRole.Planner,
            Type = AssignmentType.Planning,
            Status = AssignmentStatus.Completed
        };

        var decision = DispatchTable.Decide(spec, [completedAssignment], []);

        // Completed planning assignment should not trigger Planner dispatch
        decision.AgentRole.Should().Be(AgentRole.SpecValidator);
    }

    // ═══════════════════════════════════════════
    //  3. DummyPlanner — contract compliance
    // ═══════════════════════════════════════════

    [Fact]
    public async Task DummyPlanner_DraftUpdated_IncludesProposedSpec()
    {
        var planner = new FlowCore.Agents.Dummy.DummyPlanner();
        var input = CreatePlanningInput();

        var output = await planner.ExecuteAsync(input);

        output.Result.Should().Be(AgentResult.Success);
        output.ProposedEvent.Should().Be(FlowEvent.DraftUpdated);
        output.ProposedSpec.Should().NotBeNull();
        output.ProposedSpec!.Title.Should().Be("Original Title");
    }

    [Fact]
    public async Task DummyPlanner_FailedSpec_IncludesProposedSpec()
    {
        var planner = new FlowCore.Agents.Dummy.DummyPlanner();
        var input = new AgentInput
        {
            Spec = new Spec
            {
                Id = "spec-fail", ProjectId = "proj-001",
                Title = "Failed Title", Problem = "P", Goal = "G",
                State = FlowState.Failed,
                ProcessingStatus = ProcessingStatus.Error,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                Version = 5
            },
            Assignment = new Assignment
            {
                Id = "asg-001", SpecId = "spec-fail",
                AgentRole = AgentRole.Planner,
                Type = AssignmentType.Planning,
                Status = AssignmentStatus.Running
            },
            ProjectId = "proj-001", RunId = "run-001", CurrentVersion = 5
        };

        var output = await planner.ExecuteAsync(input);

        output.ProposedEvent.Should().Be(FlowEvent.DraftCreated);
        output.ProposedSpec.Should().NotBeNull();
        output.ProposedSpec!.Title.Should().Be("Failed Title");
    }

    // ═══════════════════════════════════════════
    //  4. PlannerOutputParser — Path B parsing
    // ═══════════════════════════════════════════

    [Fact]
    public void PathB_Parse_ValidMultiSpec_ReturnsAll()
    {
        var parser = new PlannerOutputParser();
        var json = """
            ```json
            {
              "specs": [
                {
                  "title": "Spec A",
                  "type": "feature",
                  "problem": "Problem A",
                  "goal": "Goal A",
                  "acceptanceCriteria": [
                    { "text": "AC-A1", "testable": true }
                  ],
                  "riskLevel": "low",
                  "dependsOn": []
                },
                {
                  "title": "Spec B",
                  "type": "task",
                  "problem": "Problem B",
                  "goal": "Goal B",
                  "acceptanceCriteria": [
                    { "text": "AC-B1" }
                  ],
                  "riskLevel": "medium",
                  "dependsOn": ["existing-001"],
                  "internalDependsOn": [0]
                }
              ],
              "summary": "2 specs created"
            }
            ```
            """;

        var result = parser.Parse(json);

        result.Should().NotBeNull();
        result!.Specs.Should().HaveCount(2);
        result.Summary.Should().Be("2 specs created");

        result.Specs[0].Title.Should().Be("Spec A");
        result.Specs[0].Type.Should().Be(SpecType.Feature);
        result.Specs[0].AcceptanceCriteria.Should().HaveCount(1);

        result.Specs[1].Title.Should().Be("Spec B");
        result.Specs[1].RiskLevel.Should().Be(RiskLevel.Medium);
        result.Specs[1].DependsOn.Should().Contain("existing-001");
        result.Specs[1].InternalDependsOn.Should().Contain(0);
    }

    [Fact]
    public void PathB_Parse_EmptySpecs_ReturnsNull()
    {
        var parser = new PlannerOutputParser();
        var json = """
            ```json
            { "specs": [], "summary": "nothing" }
            ```
            """;

        parser.Parse(json).Should().BeNull();
    }

    [Fact]
    public void PathB_Parse_BlankTitle_SkipsInvalidSpec()
    {
        var parser = new PlannerOutputParser();
        var json = """
            ```json
            {
              "specs": [
                { "title": "", "problem": "P" },
                { "title": "Valid", "problem": "P2" }
              ],
              "summary": "one valid"
            }
            ```
            """;

        var result = parser.Parse(json);
        result.Should().NotBeNull();
        result!.Specs.Should().HaveCount(1);
        result.Specs[0].Title.Should().Be("Valid");
    }

    [Fact]
    public void PathB_Parse_InvalidJson_ReturnsNull()
    {
        var parser = new PlannerOutputParser();
        parser.Parse("this is not json at all {broken").Should().BeNull();
    }

    [Fact]
    public void PathB_Parse_NegativeInternalDependsOn_Filtered()
    {
        var parser = new PlannerOutputParser();
        var json = """
            ```json
            {
              "specs": [
                {
                  "title": "Spec",
                  "internalDependsOn": [-1, 0, -5]
                }
              ],
              "summary": "test"
            }
            ```
            """;

        var result = parser.Parse(json);
        result!.Specs[0].InternalDependsOn.Should().BeEquivalentTo([0]);
    }

    [Fact]
    public void PathB_Parse_TestableDefaultsToTrue()
    {
        var parser = new PlannerOutputParser();
        var json = """
            ```json
            {
              "specs": [
                {
                  "title": "Spec",
                  "acceptanceCriteria": [
                    { "text": "AC without testable" }
                  ]
                }
              ],
              "summary": "test"
            }
            ```
            """;

        var result = parser.Parse(json);
        result!.Specs[0].AcceptanceCriteria[0].Testable.Should().BeTrue();
    }

    [Fact]
    public void PathB_Parse_UnknownRiskLevel_DefaultsToLow()
    {
        var parser = new PlannerOutputParser();
        var json = """
            ```json
            {
              "specs": [
                { "title": "Spec", "riskLevel": "unknown_level" }
              ],
              "summary": "test"
            }
            ```
            """;

        var result = parser.Parse(json);
        result!.Specs[0].RiskLevel.Should().Be(RiskLevel.Low);
    }
}
