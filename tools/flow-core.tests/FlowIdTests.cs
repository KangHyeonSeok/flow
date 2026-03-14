using FlowCore.Utilities;
using FluentAssertions;

namespace FlowCore.Tests;

public class FlowIdTests
{
    [Theory]
    [InlineData("spec")]
    [InlineData("asg")]
    [InlineData("rr")]
    [InlineData("evt")]
    public void New_GeneratesCorrectFormat(string prefix)
    {
        var id = FlowId.New(prefix);
        id.Should().StartWith($"{prefix}-");
        id.Should().HaveLength(prefix.Length + 1 + 8); // prefix + '-' + 8 hex chars
    }

    [Fact]
    public void New_GeneratesUniqueIds()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => FlowId.New("spec")).ToHashSet();
        ids.Should().HaveCount(100);
    }

    [Fact]
    public void New_HexCharactersOnly()
    {
        var id = FlowId.New("test");
        var hex = id[(id.IndexOf('-') + 1)..];
        hex.Should().MatchRegex("^[0-9a-f]{8}$");
    }
}
