using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests;

/// <summary>
/// CodeRefValidator 코드 참조 검증 테스트
/// </summary>
public class CodeRefValidatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CodeRefValidator _validator;

    public CodeRefValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-coderef-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _validator = new CodeRefValidator(_tempDir);

        // 테스트 파일 생성
        var srcDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllLines(Path.Combine(srcDir, "Example.cs"), new[]
        {
            "namespace Test;",
            "public class Example",
            "{",
            "    public void Method1() { }",
            "    public void Method2() { }",
            "}"
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void CheckAll_ValidRef_NoErrors()
    {
        var specs = new List<SpecNode>
        {
            new()
            {
                Id = "F-001",
                Title = "테스트",
                Description = "desc",
                CodeRefs = new List<string> { "src/Example.cs" }
            }
        };

        var result = _validator.CheckAll(specs);
        result.ValidRefs.Should().Be(1);
        result.InvalidRefs.Should().Be(0);
        result.HealthPercent.Should().Be(100);
    }

    [Fact]
    public void CheckAll_ValidRefWithLineRange_NoErrors()
    {
        var specs = new List<SpecNode>
        {
            new()
            {
                Id = "F-001",
                Title = "테스트",
                Description = "desc",
                CodeRefs = new List<string> { "src/Example.cs#L1-L6" }
            }
        };

        var result = _validator.CheckAll(specs);
        result.ValidRefs.Should().Be(1);
        result.InvalidRefs.Should().Be(0);
    }

    [Fact]
    public void CheckAll_InvalidRef_FileNotFound()
    {
        var specs = new List<SpecNode>
        {
            new()
            {
                Id = "F-001",
                Title = "테스트",
                Description = "desc",
                CodeRefs = new List<string> { "src/NonExistent.cs" }
            }
        };

        var result = _validator.CheckAll(specs);
        result.InvalidRefs.Should().Be(1);
        result.InvalidItems.Should().Contain(i => i.Reason.Contains("존재하지 않음"));
    }

    [Fact]
    public void CheckAll_InvalidLineRange_OutOfBounds()
    {
        var specs = new List<SpecNode>
        {
            new()
            {
                Id = "F-001",
                Title = "테스트",
                Description = "desc",
                CodeRefs = new List<string> { "src/Example.cs#L100" }
            }
        };

        var result = _validator.CheckAll(specs);
        result.InvalidRefs.Should().Be(1);
        result.InvalidItems.Should().Contain(i => i.Reason.Contains("라인 범위 초과"));
    }

    [Fact]
    public void CheckAll_ConditionCodeRefs_AlsoChecked()
    {
        var specs = new List<SpecNode>
        {
            new()
            {
                Id = "F-001",
                Title = "테스트",
                Description = "desc",
                Conditions = new List<SpecCondition>
                {
                    new()
                    {
                        Id = "F-001-C1",
                        Description = "조건1",
                        CodeRefs = new List<string> { "src/Example.cs#L1-L3" }
                    },
                    new()
                    {
                        Id = "F-001-C2",
                        Description = "조건2",
                        CodeRefs = new List<string> { "src/Missing.cs" }
                    }
                }
            }
        };

        var result = _validator.CheckAll(specs);
        result.TotalRefs.Should().Be(2);
        result.ValidRefs.Should().Be(1);
        result.InvalidRefs.Should().Be(1);
    }

    [Fact]
    public void CheckAll_EmptyRefs_NoErrors()
    {
        var specs = new List<SpecNode>
        {
            new() { Id = "F-001", Title = "테스트", Description = "desc" }
        };

        var result = _validator.CheckAll(specs);
        result.TotalRefs.Should().Be(0);
        result.HealthPercent.Should().Be(100);
    }

    [Fact]
    public void CheckAll_BackslashPath_Normalized()
    {
        var specs = new List<SpecNode>
        {
            new()
            {
                Id = "F-001",
                Title = "테스트",
                Description = "desc",
                CodeRefs = new List<string> { "src\\Example.cs" }
            }
        };

        var result = _validator.CheckAll(specs);
        result.ValidRefs.Should().Be(1);
    }

    [Fact]
    public void CheckAll_MixedValidAndInvalid()
    {
        var specs = new List<SpecNode>
        {
            new()
            {
                Id = "F-001",
                Title = "테스트",
                Description = "desc",
                CodeRefs = new List<string>
                {
                    "src/Example.cs",
                    "src/Missing.cs",
                    "src/Example.cs#L3"
                }
            }
        };

        var result = _validator.CheckAll(specs);
        result.TotalRefs.Should().Be(3);
        result.ValidRefs.Should().Be(2);
        result.InvalidRefs.Should().Be(1);
        result.HealthPercent.Should().BeApproximately(66.7, 0.1);
    }
}
