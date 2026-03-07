using FluentAssertions;
using FlowCLI.Services.Runner;
using FlowCLI.Services.SpecGraph;

namespace FlowCLI.Tests;

/// <summary>
/// GitHubIssueService 단위 테스트 (F-070-C11~C15).
/// GitHub API 호출 없이 내부 로직을 검증한다.
/// </summary>
public class GitHubIssueServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _specsDir;
    private readonly SpecStore _specStore;

    public GitHubIssueServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-test-{Guid.NewGuid():N}");
        _specsDir = Path.Combine(_tempDir, "docs", "specs");
        Directory.CreateDirectory(_specsDir);
        _specStore = new SpecStore(_specsDir, externalRepo: true);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
    }

    // ── ParseGitHubRepo 테스트 ───────────────────────────────

    [Fact]
    public void ParseGitHubRepo_ExplicitGitHubRepo_ReturnsParsed()
    {
        var (owner, repo) = GitHubIssueService.ParseGitHubRepo("myorg/myrepo", null);
        owner.Should().Be("myorg");
        repo.Should().Be("myrepo");
    }

    [Fact]
    public void ParseGitHubRepo_FromSpecRepository_Https()
    {
        var (owner, repo) = GitHubIssueService.ParseGitHubRepo(null, "https://github.com/user/flow-spec.git");
        owner.Should().Be("user");
        repo.Should().Be("flow-spec");
    }

    [Fact]
    public void ParseGitHubRepo_FromSpecRepository_SshUrl()
    {
        var (owner, repo) = GitHubIssueService.ParseGitHubRepo(null, "git@github.com:org/my-specs.git");
        owner.Should().Be("org");
        repo.Should().Be("my-specs");
    }

    [Fact]
    public void ParseGitHubRepo_NoConfig_Throws()
    {
        var act = () => GitHubIssueService.ParseGitHubRepo(null, null);
        act.Should().Throw<InvalidOperationException>();
    }

    // ── FindSpecReferences 테스트 (C12) ──────────────────────

    [Fact]
    public void FindSpecReferences_FoundInTitle()
    {
        _specStore.Create(new SpecNode { Id = "F-010", Title = "Test", Status = "working" });

        var service = CreateService();
        var issue = new GitHubIssueInfo
        {
            Number = 1,
            Title = "Fix F-010 bug",
            Body = "Some body",
            Labels = new List<string>()
        };

        var refs = service.FindSpecReferences(issue);
        refs.Should().Contain("F-010");
    }

    [Fact]
    public void FindSpecReferences_FoundInBody()
    {
        _specStore.Create(new SpecNode { Id = "F-020", Title = "Test", Status = "working" });

        var service = CreateService();
        var issue = new GitHubIssueInfo
        {
            Number = 2,
            Title = "Some issue",
            Body = "Related to F-020 implementation",
            Labels = new List<string>()
        };

        var refs = service.FindSpecReferences(issue);
        refs.Should().Contain("F-020");
    }

    [Fact]
    public void FindSpecReferences_FoundInLabel()
    {
        _specStore.Create(new SpecNode { Id = "F-030", Title = "Test", Status = "working" });

        var service = CreateService();
        var issue = new GitHubIssueInfo
        {
            Number = 3,
            Title = "Another issue",
            Body = "No spec reference here",
            Labels = new List<string> { "F-030", "bug" }
        };

        var refs = service.FindSpecReferences(issue);
        refs.Should().Contain("F-030");
    }

    [Fact]
    public void FindSpecReferences_NonExistentSpec_NotReturned()
    {
        var service = CreateService();
        var issue = new GitHubIssueInfo
        {
            Number = 4,
            Title = "Fix F-999 bug",
            Body = "",
            Labels = new List<string>()
        };

        var refs = service.FindSpecReferences(issue);
        refs.Should().BeEmpty();
    }

    // ── FindRelatedSpec 테스트 (C13) ─────────────────────────

    [Fact]
    public void FindRelatedSpec_MatchesByKeywords()
    {
        _specStore.Create(new SpecNode
        {
            Id = "F-040",
            Title = "Runner 자동 구현",
            Description = "자동 구현 에이전트 기능",
            Status = "working",
            Tags = new List<string> { "runner", "automation" }
        });

        var service = CreateService();
        var issue = new GitHubIssueInfo
        {
            Number = 5,
            Title = "Runner 자동 구현 문제",
            Body = "자동 구현 에이전트에서 오류 발생",
            Labels = new List<string>()
        };

        var related = service.FindRelatedSpec(issue);
        related.Should().NotBeNull();
        related!.Id.Should().Be("F-040");
    }

    [Fact]
    public void FindRelatedSpec_NoMatch_ReturnsNull()
    {
        _specStore.Create(new SpecNode
        {
            Id = "F-050",
            Title = "데이터베이스 설정",
            Description = "SQLite 데이터베이스 초기화",
            Status = "working"
        });

        var service = CreateService();
        var issue = new GitHubIssueInfo
        {
            Number = 6,
            Title = "Flutter UI 버그",
            Body = "Flutter 위젯 렌더링 오류",
            Labels = new List<string>()
        };

        var related = service.FindRelatedSpec(issue);
        related.Should().BeNull();
    }

    // ── ExtractKeywords 테스트 ───────────────────────────────

    [Fact]
    public void ExtractKeywords_IgnoresShortWords()
    {
        var keywords = GitHubIssueService.ExtractKeywords("I am a developer");
        keywords.Should().NotContain("i");
        keywords.Should().NotContain("a");
        keywords.Should().Contain("am");
        keywords.Should().Contain("developer");
    }

    [Fact]
    public void ExtractKeywords_RemovesStopWords()
    {
        var keywords = GitHubIssueService.ExtractKeywords("the runner should have been running");
        keywords.Should().NotContain("the");
        keywords.Should().NotContain("should");
        keywords.Should().NotContain("have");
        keywords.Should().NotContain("been");
        keywords.Should().Contain("runner");
        keywords.Should().Contain("running");
    }

    [Fact]
    public void ExtractKeywords_HandlesKorean()
    {
        var keywords = GitHubIssueService.ExtractKeywords("Runner 자동 구현 에이전트");
        keywords.Should().Contain("runner");
        keywords.Should().Contain("자동");
        keywords.Should().Contain("구현");
        keywords.Should().Contain("에이전트");
    }

    // ── CreateSpecFromIssueAsync 테스트 (C14) ────────────────

    [Fact]
    public async Task CreateSpecFromIssueAsync_CreatesSpecWithCorrectFields()
    {
        var service = CreateService();
        var issue = new GitHubIssueInfo
        {
            Number = 10,
            Title = "새로운 기능 요청",
            Body = "새로운 기능에 대한 설명입니다.",
            Labels = new List<string> { "enhancement", "priority-high" }
        };

        var spec = await service.CreateSpecFromIssueAsync(issue);

        spec.Should().NotBeNull();
        spec!.Title.Should().Be("새로운 기능 요청");
        spec.Status.Should().Be("draft");
        spec.Description.Should().Contain("GitHub 이슈 #10");
        spec.Tags.Should().Contain("auto-created");
        spec.Tags.Should().Contain("github-issue");
        spec.Tags.Should().Contain("enhancement");
        spec.Metadata.Should().ContainKey("sourceIssue");
        spec.Metadata.Should().ContainKey("githubIssues");

        // 저장소에 제대로 저장되었는지 확인
        var saved = _specStore.Get(spec.Id);
        saved.Should().NotBeNull();
        saved!.Id.Should().Be(spec.Id);
    }

    // ── helper ───────────────────────────────────────────────

    private GitHubIssueService CreateService()
    {
        var config = new RunnerConfig
        {
            SpecRepository = "https://github.com/test/test-spec.git",
            GitHubRepo = "test/test-repo",
            GitHubToken = "fake-token",
            GitHubIssuesEnabled = true,
            SpecLinkCommentTemplate = "Linked spec: {specId}",
            SpecLinkLabel = "spec-linked",
            AutoCreateSpecLabel = "spec-auto-created"
        };
        var log = new RunnerLogService(_tempDir, "logs", "test-instance");
        var copilot = new CopilotService(config, log);

        var flowRoot = _tempDir;
        var http = new HttpClient(); // Not actually called in unit tests

        return new GitHubIssueService(config, _specStore, copilot, log, flowRoot, http);
    }
}
