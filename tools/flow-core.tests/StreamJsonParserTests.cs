using FlowCore.Backend;
using FluentAssertions;

namespace FlowCore.Tests;

public class StreamJsonParserTests
{
    [Fact]
    public void Parse_ResultEvent_ExtractsResponseText()
    {
        var raw = """
            {"type":"content_block_start","index":0}
            {"type":"content_block_delta","delta":{"text":"분석 중..."}}
            {"type":"result","result":"최종 결과입니다.","stop_reason":"end_turn"}
            """;

        var response = StreamJsonParser.Parse(raw);

        response.Success.Should().BeTrue();
        response.ResponseText.Should().Be("최종 결과입니다.");
        response.StopReason.Should().Be(CliStopReason.Completed);
    }

    [Fact]
    public void Parse_NoResultEvent_FallsBackToDelta()
    {
        var raw = """
            {"type":"content_block_delta","delta":{"text":"Hello "}}
            {"type":"content_block_delta","delta":{"text":"World"}}
            """;

        var response = StreamJsonParser.Parse(raw);

        response.Success.Should().BeTrue();
        response.ResponseText.Should().Be("Hello World");
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsError()
    {
        var response = StreamJsonParser.Parse("");

        response.Success.Should().BeFalse();
        response.StopReason.Should().Be(CliStopReason.Error);
        response.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public void Parse_WhitespaceOnly_ReturnsError()
    {
        var response = StreamJsonParser.Parse("   \n  \n  ");

        response.Success.Should().BeFalse();
        response.StopReason.Should().Be(CliStopReason.Error);
    }

    [Fact]
    public void Parse_MalformedJson_SkipsAndContinues()
    {
        var raw = """
            not json at all
            {"type":"content_block_delta","delta":{"text":"good"}}
            {broken json
            {"type":"result","result":"final"}
            """;

        var response = StreamJsonParser.Parse(raw);

        response.Success.Should().BeTrue();
        response.ResponseText.Should().Be("final");
    }

    [Fact]
    public void Parse_ToolUseEvents_Ignored()
    {
        var raw = """
            {"type":"tool_use","name":"read_file","input":{"path":"test.cs"}}
            {"type":"result","result":"done"}
            """;

        var response = StreamJsonParser.Parse(raw);

        response.Success.Should().BeTrue();
        response.ResponseText.Should().Be("done");
    }

    [Fact]
    public void Parse_NoResultNoDelta_ReturnsError()
    {
        var raw = """
            {"type":"tool_use","name":"read_file","input":{}}
            {"type":"content_block_start","index":0}
            """;

        var response = StreamJsonParser.Parse(raw);

        response.Success.Should().BeFalse();
        response.StopReason.Should().Be(CliStopReason.Error);
        response.ErrorMessage.Should().Contain("no result");
    }
}
