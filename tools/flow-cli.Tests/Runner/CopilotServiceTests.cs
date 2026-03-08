using FlowCLI.Services.Runner;
using FlowCLI.Services.SpecGraph;
using FluentAssertions;

namespace FlowCLI.Tests.Runner;

public class CopilotServiceTests
{
    [Fact]
    public void ResolveReviewModel_AlwaysUsesGpt5Mini_ForSmallSpec()
    {
        var config = new RunnerConfig();
        var spec = new SpecNode
        {
            Id = "F-300",
            Title = "Small review",
            Description = "짧은 설명",
            Status = "needs-review",
            Conditions =
            [
                new SpecCondition
                {
                    Id = "F-300-C1",
                    Status = "verified"
                }
            ]
        };

        var model = CopilotService.ResolveReviewModel(spec, config);

        model.Should().Be("gpt-5-mini");
    }

    [Fact]
    public void ResolveReviewModel_AlwaysUsesGpt5Mini_ForLargeSpec()
    {
        var config = new RunnerConfig();
        var spec = new SpecNode
        {
            Id = "F-301",
            Title = "Large review",
            Description = new string('y', 900),
            Status = "needs-review",
            CodeRefs = ["a.cs", "b.cs", "c.cs"],
            Conditions =
            [
                new SpecCondition { Id = "F-301-C1", Status = "verified", CodeRefs = ["1.cs", "2.cs"] },
                new SpecCondition { Id = "F-301-C2", Status = "verified", CodeRefs = ["3.cs"] },
                new SpecCondition { Id = "F-301-C3", Status = "verified" }
            ]
        };

        var model = CopilotService.ResolveReviewModel(spec, config);

        model.Should().Be("gpt-5-mini");
    }
}
