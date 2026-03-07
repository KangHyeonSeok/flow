using FluentAssertions;
using FlowCLI.Tests.Fixtures;

namespace FlowCLI.Tests.Services;

/// <summary>
/// Optional field tests (OF-01 ~ OF-04).
/// Verifies includePlan and includeResult parameter behavior.
/// 
/// When includePlan/includeResult=false, the SQL SELECT doesn't fetch those columns,
/// so the TaskRecord fields remain at their default ("").
/// </summary>
[Collection("DatabaseTests")]
public class DatabaseServiceQueryTests_OptionalFields : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public DatabaseServiceQueryTests_OptionalFields(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// OF-01: includePlan=true — PlanText should be populated.
    /// </summary>
    [Fact]
    public void Query_WithIncludePlan_ReturnsPlanText()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(top: 5, includePlan: true);

        results.Should().NotBeEmpty();
        // At least one record should have non-empty PlanText
        results.Should().Contain(r => !string.IsNullOrEmpty(r.PlanText),
            because: "sample data has PlanText values");
    }

    /// <summary>
    /// OF-02: includePlan=false (default) — PlanText should be empty/default.
    /// </summary>
    [Fact]
    public void Query_WithoutIncludePlan_ExcludesPlanText()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(top: 5, includePlan: false);

        results.Should().NotBeEmpty();
        // PlanText should remain at default ("") since the column is not fetched
        results.Should().OnlyContain(r => string.IsNullOrEmpty(r.PlanText),
            because: "plan_text column is not included in SELECT when includePlan=false");
    }

    /// <summary>
    /// OF-03: includeResult=true — ResultText should be populated.
    /// </summary>
    [Fact]
    public void Query_WithIncludeResult_ReturnsResultText()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(top: 5, includeResult: true);

        results.Should().NotBeEmpty();
        results.Should().Contain(r => !string.IsNullOrEmpty(r.ResultText),
            because: "sample data has ResultText values");
    }

    /// <summary>
    /// OF-04: Both includePlan=true and includeResult=true.
    /// </summary>
    [Fact]
    public void Query_WithBothFlags_ReturnsBothFields()
    {
        using var service = _fixture.CreateService();
        var results = service.Query(top: 5, includePlan: true, includeResult: true);

        results.Should().NotBeEmpty();
        results.Should().Contain(r => !string.IsNullOrEmpty(r.PlanText),
            because: "includePlan=true should fetch plan_text");
        results.Should().Contain(r => !string.IsNullOrEmpty(r.ResultText),
            because: "includeResult=true should fetch result_text");
    }
}
