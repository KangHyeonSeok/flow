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
