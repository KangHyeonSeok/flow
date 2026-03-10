using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests;

/// <summary>
/// SpecValidator 유효성 검사 테스트
/// </summary>
public class SpecValidatorTests
{
    private readonly SpecValidator _validator = new();

    // ─── 필수 필드 검사 ─────────────────────────────────────────────────

    [Fact]
    public void ValidateSpec_EmptyId_ReturnsError()
    {
        var spec = new SpecNode { Id = "", Title = "test", Description = "desc" };
        var result = _validator.ValidateSpec(spec);
        result.Errors.Should().Contain(e => e.Field == "id");
    }

    [Fact]
    public void ValidateSpec_EmptyTitle_ReturnsError()
    {
        var spec = new SpecNode { Id = "F-001", Title = "", Description = "desc" };
        var result = _validator.ValidateSpec(spec);
        result.Errors.Should().Contain(e => e.Field == "title");
    }

    [Fact]
    public void ValidateSpec_EmptyDescription_ReturnsError()
    {
        var spec = new SpecNode { Id = "F-001", Title = "test", Description = "" };
        var result = _validator.ValidateSpec(spec);
        result.Errors.Should().Contain(e => e.Field == "description");
    }

    // ─── ID 형식 검사 ───────────────────────────────────────────────────

    [Theory]
    [InlineData("F-001", true)]
    [InlineData("F-010", true)]
    [InlineData("F-999", true)]
    [InlineData("F-010-01", true)]
    [InlineData("F-010-99", true)]
    [InlineData("X-001", false)]
    [InlineData("F001", false)]
    [InlineData("F-1", false)]
    [InlineData("F-0001", false)]
    public void ValidateSpec_IdFormat(string id, bool valid)
    {
        var spec = new SpecNode { Id = id, Title = "test", Description = "desc" };
        var result = _validator.ValidateSpec(spec);

        if (valid)
            result.Errors.Should().NotContain(e => e.Field == "id" && e.Message.Contains("형식"));
        else
            result.Errors.Should().Contain(e => e.Field == "id" && e.Message.Contains("형식"));
    }

    // ─── 상태 유효성 검사 ───────────────────────────────────────────────

    [Theory]
    [InlineData("draft", true)]
    [InlineData("queued", true)]
    [InlineData("working", true)]
    [InlineData("needs-review", true)]
    [InlineData("verified", true)]
    [InlineData("deprecated", true)]
    [InlineData("done", true)]
    [InlineData("invalid", false)]
    public void ValidateSpec_Status(string status, bool valid)
    {
        var spec = new SpecNode { Id = "F-001", Title = "test", Description = "desc", Status = status };
        var result = _validator.ValidateSpec(spec);

        if (valid)
            result.Errors.Should().NotContain(e => e.Field == "status");
        else
            result.Errors.Should().Contain(e => e.Field == "status");
    }

    [Fact]
    public void ValidateSpec_ConditionWorkingStatus_ReturnsError()
    {
        var spec = new SpecNode
        {
            Id = "F-001",
            Title = "test",
            Description = "desc",
            Conditions =
            [
                new SpecCondition { Id = "F-001-C1", Description = "condition", Status = "working" }
            ]
        };

        var result = _validator.ValidateSpec(spec);

        result.Errors.Should().Contain(e => e.Field == "conditions[F-001-C1].status");
    }

    [Fact]
    public void ValidateSpec_InvalidActivityRole_ReturnsError()
    {
        var spec = new SpecNode
        {
            Id = "F-001",
            Title = "test",
            Description = "desc",
            Activity =
            [
                new SpecActivityEntry
                {
                    At = DateTime.UtcNow.ToString("o"),
                    Role = "runner",
                    Summary = "invalid role",
                    Outcome = "handoff"
                }
            ]
        };

        var result = _validator.ValidateSpec(spec);

        result.Errors.Should().Contain(e => e.Field == "activity[0].role");
    }

    [Fact]
    public void ValidateSpec_InvalidActivityIssue_ReturnsError()
    {
        var spec = new SpecNode
        {
            Id = "F-001",
            Title = "test",
            Description = "desc",
            Activity =
            [
                new SpecActivityEntry
                {
                    At = DateTime.UtcNow.ToString("o"),
                    Role = "tester",
                    Summary = "invalid issue",
                    Outcome = "requeue",
                    Issues = ["unknown-issue"]
                }
            ]
        };

        var result = _validator.ValidateSpec(spec);

        result.Errors.Should().Contain(e => e.Field == "activity[0].issues[0]");
    }

    [Fact]
    public void ValidateSpec_InvalidActivityOutcome_ReturnsError()
    {
        var spec = new SpecNode
        {
            Id = "F-001",
            Title = "test",
            Description = "desc",
            Activity =
            [
                new SpecActivityEntry
                {
                    At = DateTime.UtcNow.ToString("o"),
                    Role = "tester",
                    Summary = "invalid outcome",
                    Outcome = "queued"
                }
            ]
        };

        var result = _validator.ValidateSpec(spec);

        result.Errors.Should().Contain(e => e.Field == "activity[0].outcome");
    }

    [Fact]
    public void ValidateSpec_InvalidConditionUpdateReason_ReturnsError()
    {
        var spec = new SpecNode
        {
            Id = "F-001",
            Title = "test",
            Description = "desc",
            Activity =
            [
                new SpecActivityEntry
                {
                    At = DateTime.UtcNow.ToString("o"),
                    Role = "tester",
                    Summary = "invalid condition update reason",
                    Outcome = "requeue",
                    ConditionUpdates =
                    [
                        new SpecConditionUpdate
                        {
                            ConditionId = "F-001-C1",
                            Status = "draft",
                            Reason = "unknown-reason"
                        }
                    ]
                }
            ]
        };

        var result = _validator.ValidateSpec(spec);

        result.Errors.Should().Contain(e => e.Field == "activity[0].conditionUpdates[0].reason");
    }

    [Fact]
    public void ValidateSpec_NewReviewActivityVocabulary_IsAccepted()
    {
        var spec = new SpecNode
        {
            Id = "F-001",
            Title = "test",
            Description = "desc",
            Status = "queued",
            Activity =
            [
                new SpecActivityEntry
                {
                    At = DateTime.UtcNow.ToString("o"),
                    Role = "tester",
                    Summary = "자동 재시도를 위해 queued로 복귀",
                    Outcome = "requeue",
                    Kind = "verification",
                    Issues = ["missing-evidence"],
                    StatusChange = new SpecActivityStatusChange
                    {
                        From = "working",
                        To = "queued"
                    },
                    ConditionUpdates =
                    [
                        new SpecConditionUpdate
                        {
                            ConditionId = "F-001-C1",
                            Status = "draft",
                            Reason = "reset-for-requeue"
                        }
                    ]
                },
                new SpecActivityEntry
                {
                    At = DateTime.UtcNow.ToString("o"),
                    Role = "system",
                    Summary = "429 감지 후 쿨다운 큐로 이동",
                    Outcome = "requeue",
                    Kind = "recovery",
                    Issues = ["rate-limited"],
                    StatusChange = new SpecActivityStatusChange
                    {
                        From = "working",
                        To = "queued"
                    },
                    ConditionUpdates =
                    [
                        new SpecConditionUpdate
                        {
                            ConditionId = "F-001-C3",
                            Status = "draft",
                            Reason = "rate-limited"
                        }
                    ]
                },
                new SpecActivityEntry
                {
                    At = DateTime.UtcNow.ToString("o"),
                    Role = "tester",
                    Summary = "사용자 수동 테스트 필요",
                    Outcome = "needs-review",
                    Kind = "verification",
                    Issues = ["user-test-required"],
                    StatusChange = new SpecActivityStatusChange
                    {
                        From = "working",
                        To = "needs-review"
                    },
                    ConditionUpdates =
                    [
                        new SpecConditionUpdate
                        {
                            ConditionId = "F-001-C2",
                            Status = "needs-review",
                            Reason = "user-test-required"
                        }
                    ]
                }
            ]
        };

        var result = _validator.ValidateSpec(spec);

        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("feature", true)]
    [InlineData("condition", true)]
    [InlineData("task", true)]
    [InlineData("other", false)]
    public void ValidateSpec_NodeType(string nodeType, bool valid)
    {
        var spec = new SpecNode { Id = "F-001", Title = "test", Description = "desc", NodeType = nodeType };
        var result = _validator.ValidateSpec(spec);

        if (valid)
            result.Errors.Should().NotContain(e => e.Field == "nodeType");
        else
            result.Errors.Should().Contain(e => e.Field == "nodeType");
    }

    // ─── Strict 모드: 수락 조건 3개+ ────────────────────────────────────

    [Fact]
    public void ValidateSpec_Strict_TooFewConditions_ReturnsError()
    {
        var spec = new SpecNode
        {
            Id = "F-001",
            Title = "test",
            Description = "desc",
            Conditions = new List<SpecCondition>
            {
                new() { Id = "F-001-C1", Description = "조건1" },
                new() { Id = "F-001-C2", Description = "조건2" }
            }
        };

        var result = _validator.ValidateSpec(spec, strict: true);
        result.Errors.Should().Contain(e => e.Field == "conditions");
    }

    [Fact]
    public void ValidateSpec_Strict_ThreeOrMoreConditions_NoError()
    {
        var spec = new SpecNode
        {
            Id = "F-001",
            Title = "test",
            Description = "desc",
            Conditions = new List<SpecCondition>
            {
                new() { Id = "F-001-C1", Description = "조건1" },
                new() { Id = "F-001-C2", Description = "조건2" },
                new() { Id = "F-001-C3", Description = "조건3" }
            }
        };

        var result = _validator.ValidateSpec(spec, strict: true);
        result.Errors.Should().NotContain(e => e.Field == "conditions");
    }

    // ─── 참조 무결성 검사 ───────────────────────────────────────────────

    [Fact]
    public void ValidateAll_InvalidParent_ReturnsError()
    {
        var specs = new List<SpecNode>
        {
            new() { Id = "F-001", Title = "기능1", Description = "설명", Parent = "F-999" }
        };

        var result = _validator.ValidateAll(specs);
        result.Errors.Should().Contain(e => e.Field == "parent" && e.Message.Contains("F-999"));
    }

    [Fact]
    public void ValidateAll_InvalidDependency_ReturnsError()
    {
        var specs = new List<SpecNode>
        {
            new() { Id = "F-001", Title = "기능1", Description = "설명", Dependencies = new List<string> { "F-999" } }
        };

        var result = _validator.ValidateAll(specs);
        result.Errors.Should().Contain(e => e.Field == "dependencies" && e.Message.Contains("F-999"));
    }

    [Fact]
    public void ValidateAll_SelfDependency_ReturnsError()
    {
        var specs = new List<SpecNode>
        {
            new() { Id = "F-001", Title = "기능1", Description = "설명", Dependencies = new List<string> { "F-001" } }
        };

        var result = _validator.ValidateAll(specs);
        result.Errors.Should().Contain(e => e.Message.Contains("자기 자신"));
    }

    [Fact]
    public void ValidateAll_SelfParent_ReturnsError()
    {
        var specs = new List<SpecNode>
        {
            new() { Id = "F-001", Title = "기능1", Description = "설명", Parent = "F-001" }
        };

        var result = _validator.ValidateAll(specs);
        result.Errors.Should().Contain(e => e.Message.Contains("자기 자신"));
    }

    [Fact]
    public void ValidateAll_ValidSpecs_NoErrors()
    {
        var specs = new List<SpecNode>
        {
            new() { Id = "F-001", Title = "루트", Description = "설명" },
            new() { Id = "F-010", Title = "자식", Description = "설명", Parent = "F-001", Dependencies = new List<string> { "F-001" } }
        };

        var result = _validator.ValidateAll(specs);
        result.IsValid.Should().BeTrue();
    }
}
