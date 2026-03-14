using System.Text.Json;
using FlowCore.Models;
using FlowCore.Serialization;
using FluentAssertions;

namespace FlowCore.Tests;

public class FlowJsonOptionsTests
{
    [Fact]
    public void CamelCase_PropertyNaming()
    {
        var spec = new Spec
        {
            Id = "spec-001", ProjectId = "proj-001", Title = "Test",
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(spec, FlowJsonOptions.Default);
        json.Should().Contain("\"projectId\"");
        json.Should().Contain("\"processingStatus\"");
        json.Should().NotContain("\"ProjectId\"");
    }

    [Fact]
    public void CamelCase_EnumSerialization()
    {
        var spec = new Spec
        {
            Id = "spec-001", ProjectId = "proj-001", Title = "Test",
            State = FlowState.Implementation, ProcessingStatus = ProcessingStatus.InProgress,
            RiskLevel = RiskLevel.High,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(spec, FlowJsonOptions.Default);
        json.Should().Contain("\"implementation\"");
        json.Should().Contain("\"inProgress\"");
        json.Should().Contain("\"high\"");
    }

    [Fact]
    public void NullFields_Excluded()
    {
        var spec = new Spec
        {
            Id = "spec-001", ProjectId = "proj-001", Title = "Test",
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(spec, FlowJsonOptions.Default);
        json.Should().NotContain("\"problem\"");
        json.Should().NotContain("\"goal\"");
    }

    [Fact]
    public void Korean_NotEscaped()
    {
        var spec = new Spec
        {
            Id = "spec-001", ProjectId = "proj-001", Title = "한국어 제목",
            State = FlowState.Draft, ProcessingStatus = ProcessingStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(spec, FlowJsonOptions.Default);
        json.Should().Contain("한국어 제목");
        json.Should().NotContain("\\u");
    }

    [Fact]
    public void Enum_RoundTrip()
    {
        var json = JsonSerializer.Serialize(FlowState.ArchitectureReview, FlowJsonOptions.Default);
        json.Should().Be("\"architectureReview\"");

        var deserialized = JsonSerializer.Deserialize<FlowState>(json, FlowJsonOptions.Default);
        deserialized.Should().Be(FlowState.ArchitectureReview);
    }

    [Fact]
    public void DateTimeOffset_Iso8601()
    {
        var dt = new DateTimeOffset(2026, 3, 14, 10, 0, 0, TimeSpan.Zero);
        var json = JsonSerializer.Serialize(dt, FlowJsonOptions.Default);
        json.Should().Contain("2026-03-14T10:00:00");
    }
}
