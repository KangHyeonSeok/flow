using FlowCLI.Services.SpecGraph;
using FlowCLI.Services.TestSync;
using FluentAssertions;

namespace FlowCLI.Tests;

/// <summary>
/// F-014: 테스트 연결 및 CI 동기화 테스트
/// </summary>
public class TestSyncTests
{
    // ─── C1: 어노테이션 기반 매핑 ─────────────────────────────────────────────

    [Fact]
    public void ExtractSpecConditionId_XUnitTrait_ReturnConditionId()
    {
        var tc = new TestCaseResult
        {
            Id = "t1",
            Name = "TestMethod",
            Status = "passed",
            Traits = new Dictionary<string, string> { ["Spec"] = "F-014-C1" }
        };

        var condId = TestResultParser.ExtractSpecConditionId(tc);
        condId.Should().Be("F-014-C1");
    }

    [Fact]
    public void ExtractSpecConditionId_PytestMarker_ReturnsConditionId()
    {
        var tc = new TestCaseResult
        {
            Id = "t2",
            Name = "test_something",
            Status = "passed",
            Markers = new List<string> { "spec:F-014-C2" }
        };

        var condId = TestResultParser.ExtractSpecConditionId(tc);
        condId.Should().Be("F-014-C2");
    }

    [Fact]
    public void ExtractSpecConditionId_NameAnnotation_ReturnsConditionId()
    {
        var tc = new TestCaseResult
        {
            Id = "t3",
            Name = "should work correctly [spec:F-014-C3]",
            Status = "passed"
        };

        var condId = TestResultParser.ExtractSpecConditionId(tc);
        condId.Should().Be("F-014-C3");
    }

    [Fact]
    public void ExtractSpecConditionId_NoAnnotation_ReturnsNull()
    {
        var tc = new TestCaseResult
        {
            Id = "t4",
            Name = "plain test without annotation",
            Status = "passed"
        };

        var condId = TestResultParser.ExtractSpecConditionId(tc);
        condId.Should().BeNull();
    }

    // ─── C1: JSON 파싱 ───────────────────────────────────────────────────────

    [Fact]
    public void ParseJson_NormalizedFormat_ParsesCorrectly()
    {
        var json = """
        {
          "framework": "xunit",
          "tests": [
            {
              "id": "test-1",
              "name": "TestMethod",
              "status": "passed",
              "traits": { "Spec": "F-014-C1" }
            }
          ]
        }
        """;

        var result = TestResultParser.ParseJson(json);
        result.Framework.Should().Be("xunit");
        result.Tests.Should().HaveCount(1);
        result.Tests[0].Traits["Spec"].Should().Be("F-014-C1");
    }

    [Fact]
    public void ParseJson_JestFormat_ParsesCorrectly()
    {
        var json = """
        {
          "testResults": [
            {
              "testFilePath": "/app/tests/feature.test.js",
              "testResults": [
                {
                  "fullName": "feature test [spec:F-014-C1]",
                  "status": "passed",
                  "duration": 100
                }
              ]
            }
          ]
        }
        """;

        var result = TestResultParser.ParseJson(json);
        result.Framework.Should().Be("jest");
        result.Tests.Should().HaveCount(1);

        var condId = TestResultParser.ExtractSpecConditionId(result.Tests[0]);
        condId.Should().Be("F-014-C1");
    }

    [Fact]
    public void ParseJson_PytestFormat_ParsesCorrectly()
    {
        var json = """
        {
          "tests": [
            {
              "nodeid": "test_module.py::TestClass::test_method",
              "outcome": "passed",
              "duration": 0.05,
              "markers": [
                { "name": "spec", "args": ["F-014-C2"] }
              ]
            }
          ]
        }
        """;

        var result = TestResultParser.ParseJson(json);
        result.Framework.Should().Be("pytest");
        result.Tests.Should().HaveCount(1);

        var condId = TestResultParser.ExtractSpecConditionId(result.Tests[0]);
        condId.Should().Be("F-014-C2");
    }

    [Fact]
    public void ParseXUnitTrx_ExtractsSpecTrait()
    {
        var trx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <TestDefinitions>
            <UnitTest name="TestMethod" id="test-id-1">
              <Properties>
                <Property>
                  <Key>Spec</Key>
                  <Value>F-014-C1</Value>
                </Property>
              </Properties>
            </UnitTest>
          </TestDefinitions>
          <Results>
            <UnitTestResult testId="test-id-1" testName="TestMethod" outcome="Passed" />
          </Results>
        </TestRun>
        """;

        var result = TestResultParser.ParseXUnitTrx(trx);
        result.Framework.Should().Be("xunit");
        result.Tests.Should().HaveCount(1);
        result.Tests[0].Traits.Should().ContainKey("Spec");
        result.Tests[0].Traits["Spec"].Should().Be("F-014-C1");
    }

    // ─── C3: healthScore 계산 ────────────────────────────────────────────────

    [Fact]
    public void ComputeHealthScore_AllPassed_Returns1()
    {
        var score = TestSyncService.ComputeHealthScore(passed: 10, flaky: 0, total: 10);
        score.Should().Be(1.0);
    }

    [Fact]
    public void ComputeHealthScore_AllFailed_Returns0()
    {
        var score = TestSyncService.ComputeHealthScore(passed: 0, flaky: 0, total: 10);
        score.Should().Be(0.0);
    }

    [Fact]
    public void ComputeHealthScore_WithFlaky_AppliesPenalty()
    {
        // 8 passed, 2 flaky, 0 failed — total 10
        // flakyPenalty = 2 * 0.5 = 1.0
        // healthScore = (8 - 1.0) / 10 = 0.7
        var score = TestSyncService.ComputeHealthScore(passed: 8, flaky: 2, total: 10);
        score.Should().BeApproximately(0.7, 0.001);
    }

    [Fact]
    public void ComputeHealthScore_ZeroTotal_Returns0()
    {
        var score = TestSyncService.ComputeHealthScore(passed: 0, flaky: 0, total: 0);
        score.Should().Be(0.0);
    }

    [Fact]
    public void ComputeHealthScore_NeverExceedsOne()
    {
        var score = TestSyncService.ComputeHealthScore(passed: 100, flaky: 0, total: 50);
        score.Should().BeLessOrEqualTo(1.0);
    }

    [Fact]
    public void ComputeHealthScore_NeverBelowZero()
    {
        var score = TestSyncService.ComputeHealthScore(passed: 0, flaky: 10, total: 5);
        score.Should().BeGreaterOrEqualTo(0.0);
    }

    [Fact]
    public void ComputeFlakyScore_CalculatesCorrectly()
    {
        var score = TestSyncService.ComputeFlakyScore(flaky: 2, total: 10);
        score.Should().BeApproximately(0.2, 0.001);
    }

    // ─── C4: quarantine ──────────────────────────────────────────────────────

    [Fact]
    public void ShouldQuarantine_5ConsecutiveFlaky_WithHighFlakyScore_ReturnsTrue()
    {
        var test = new TestLink
        {
            TestId = "t1",
            FlakyHistory = new List<string> { "flaky", "flaky", "flaky", "flaky", "flaky" }
        };
        TestSyncService.ShouldQuarantine(test).Should().BeTrue();
    }

    [Fact]
    public void ShouldQuarantine_4ConsecutiveFlaky_ReturnsFalse()
    {
        var test = new TestLink
        {
            TestId = "t1",
            FlakyHistory = new List<string> { "passed", "flaky", "flaky", "flaky", "flaky" }
        };
        // last 5 are: passed, flaky, flaky, flaky, flaky — not all flaky
        TestSyncService.ShouldQuarantine(test).Should().BeFalse();
    }

    [Fact]
    public void ShouldQuarantine_LowFlakyScore_ReturnsFalse()
    {
        // 5 consecutive flaky but in a history of 100 runs (flakyScore < 0.1)
        var history = Enumerable.Repeat("passed", 95).Concat(Enumerable.Repeat("flaky", 5)).ToList();
        var test = new TestLink { TestId = "t1", FlakyHistory = history };
        // flakyScore = 5/100 = 0.05 < 0.1
        TestSyncService.ShouldQuarantine(test).Should().BeFalse();
    }

    [Fact]
    public void ShouldQuarantine_FewHistoryEntries_ReturnsFalse()
    {
        var test = new TestLink
        {
            TestId = "t1",
            FlakyHistory = new List<string> { "flaky", "flaky", "flaky" } // only 3, need 5
        };
        TestSyncService.ShouldQuarantine(test).Should().BeFalse();
    }

    // ─── C5: spec-validate --check-tests ─────────────────────────────────────

    [Fact]
    public void ValidateSpec_CheckTests_ConditionWithoutTests_ReturnsError()
    {
        var spec = new SpecNode
        {
            Id = "F-014",
            Title = "Test Feature",
            Description = "desc",
            NodeType = "feature",
            Conditions = new List<SpecCondition>
            {
                new() { Id = "F-014-C1", Description = "condition 1", Tests = new List<TestLink>() }
            }
        };

        var validator = new SpecValidator();
        var result = validator.ValidateSpec(spec, strict: false, checkTests: true);

        result.Errors.Should().Contain(e => e.Field.Contains("F-014-C1") && e.Field.Contains("tests"));
    }

    [Fact]
    public void ValidateSpec_CheckTests_ConditionWithTests_NoError()
    {
        var spec = new SpecNode
        {
            Id = "F-014",
            Title = "Test Feature",
            Description = "desc",
            NodeType = "feature",
            Conditions = new List<SpecCondition>
            {
                new()
                {
                    Id = "F-014-C1",
                    Description = "condition 1",
                    Tests = new List<TestLink>
                    {
                        new() { TestId = "t1", Name = "test", Status = "passed" }
                    }
                }
            }
        };

        var validator = new SpecValidator();
        var result = validator.ValidateSpec(spec, strict: false, checkTests: true);

        result.Errors.Should().NotContain(e => e.Field.Contains("tests"));
    }

    [Fact]
    public void ValidateSpec_CheckTests_TaskType_SkipsTestCheck()
    {
        var spec = new SpecNode
        {
            Id = "F-014",
            Title = "Task",
            Description = "desc",
            NodeType = "task",
            Conditions = new List<SpecCondition>
            {
                new() { Id = "F-014-C1", Description = "condition 1", Tests = new List<TestLink>() }
            }
        };

        var validator = new SpecValidator();
        var result = validator.ValidateSpec(spec, strict: false, checkTests: true);

        result.Errors.Should().NotContain(e => e.Field.Contains("tests"));
    }
}
