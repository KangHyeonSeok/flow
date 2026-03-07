using FlowCLI.Services;

namespace FlowCLI.Tests.Fixtures;

/// <summary>
/// Test-only PathResolver that overrides RagDbPath to point to a temporary database file.
/// </summary>
internal class TestPathResolver : PathResolver
{
    private readonly string _testDbPath;

    public TestPathResolver(string testDbPath, string rootPath) : base(rootPath)
    {
        _testDbPath = testDbPath;
    }

    public override string RagDbPath => _testDbPath;
}
