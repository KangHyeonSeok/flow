using FlowCore.Models;
using FlowCore.Storage;
using FluentAssertions;

namespace FlowCore.Tests;

public class EvidenceStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileFlowStore _store;

    public EvidenceStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-evidence-test-{Guid.NewGuid():N}");
        _store = new FileFlowStore("test-project", _tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private IEvidenceStore EvidenceStore => _store;

    [Fact]
    public async Task SaveAndLoadManifest_RoundTrips()
    {
        // spec 디렉토리 생성을 위해 먼저 spec 저장
        var spec = new Spec
        {
            Id = "spec-ev-001",
            ProjectId = "test-project",
            Title = "Evidence Test",
            State = FlowState.Review,
            ProcessingStatus = ProcessingStatus.InReview,
            Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var manifest = new EvidenceManifest
        {
            SpecId = "spec-ev-001",
            RunId = "run-001",
            CreatedAt = DateTimeOffset.UtcNow,
            Refs =
            [
                new EvidenceRef { Kind = "test-result", RelativePath = "test-output.xml", Summary = "42 tests passed" },
                new EvidenceRef { Kind = "coverage", RelativePath = "coverage.html", Summary = "85% coverage" }
            ]
        };

        await EvidenceStore.SaveManifestAsync(manifest);
        var loaded = await EvidenceStore.LoadManifestAsync("spec-ev-001", "run-001");

        loaded.Should().NotBeNull();
        loaded!.SpecId.Should().Be("spec-ev-001");
        loaded.RunId.Should().Be("run-001");
        loaded.Refs.Should().HaveCount(2);
        loaded.Refs[0].Kind.Should().Be("test-result");
        loaded.Refs[1].RelativePath.Should().Be("coverage.html");
    }

    [Fact]
    public async Task LoadManifest_NotFound_ReturnsNull()
    {
        var result = await EvidenceStore.LoadManifestAsync("nonexistent", "run-999");
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadBySpec_MultipleRuns_ReturnsAll()
    {
        var spec = new Spec
        {
            Id = "spec-ev-002",
            ProjectId = "test-project",
            Title = "Multi Evidence",
            State = FlowState.Review,
            ProcessingStatus = ProcessingStatus.InReview,
            Version = 1
        };
        await _store.SaveAsync(spec, 0);

        // 의도적으로 시간 역순 저장 (RunId는 랜덤이므로 CreatedAt 순서가 보장되어야 함)
        var baseTime = new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero);
        for (int i = 1; i <= 3; i++)
        {
            await EvidenceStore.SaveManifestAsync(new EvidenceManifest
            {
                SpecId = "spec-ev-002",
                RunId = $"run-{i:D3}",
                CreatedAt = baseTime.AddMinutes(i),
                Refs = [new EvidenceRef { Kind = "log", RelativePath = $"log-{i}.txt" }]
            });
        }

        var all = await EvidenceStore.LoadBySpecAsync("spec-ev-002");
        all.Should().HaveCount(3);
        // 최신순 정렬 검증: run-003 (가장 나중) → run-002 → run-001
        all[0].RunId.Should().Be("run-003");
        all[1].RunId.Should().Be("run-002");
        all[2].RunId.Should().Be("run-001");
    }

    [Fact]
    public async Task LoadBySpec_NoEvidence_ReturnsEmpty()
    {
        var spec = new Spec
        {
            Id = "spec-ev-003",
            ProjectId = "test-project",
            Title = "No Evidence",
            State = FlowState.Draft,
            ProcessingStatus = ProcessingStatus.Pending,
            Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var all = await EvidenceStore.LoadBySpecAsync("spec-ev-003");
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadBySpec_CreatedAtOrderDiffersFromDirName_SortsByCreatedAt()
    {
        var spec = new Spec
        {
            Id = "spec-ev-order",
            ProjectId = "test-project",
            Title = "Order Test",
            State = FlowState.Review,
            ProcessingStatus = ProcessingStatus.InReview,
            Version = 1
        };
        await _store.SaveAsync(spec, 0);

        var baseTime = new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero);

        // RunId 알파벳순: aaa < mmm < zzz
        // CreatedAt순: zzz(oldest) < aaa(middle) < mmm(newest)
        // 디렉토리 이름순과 시간순이 다르도록 설정
        await EvidenceStore.SaveManifestAsync(new EvidenceManifest
        {
            SpecId = "spec-ev-order",
            RunId = "run-zzz",
            CreatedAt = baseTime.AddMinutes(1), // oldest
            Refs = [new EvidenceRef { Kind = "log", RelativePath = "z.txt" }]
        });
        await EvidenceStore.SaveManifestAsync(new EvidenceManifest
        {
            SpecId = "spec-ev-order",
            RunId = "run-aaa",
            CreatedAt = baseTime.AddMinutes(2), // middle
            Refs = [new EvidenceRef { Kind = "log", RelativePath = "a.txt" }]
        });
        await EvidenceStore.SaveManifestAsync(new EvidenceManifest
        {
            SpecId = "spec-ev-order",
            RunId = "run-mmm",
            CreatedAt = baseTime.AddMinutes(3), // newest
            Refs = [new EvidenceRef { Kind = "log", RelativePath = "m.txt" }]
        });

        var all = await EvidenceStore.LoadBySpecAsync("spec-ev-order");
        all.Should().HaveCount(3);
        // 최신순: mmm(3min) → aaa(2min) → zzz(1min)
        all[0].RunId.Should().Be("run-mmm", "newest first by CreatedAt");
        all[1].RunId.Should().Be("run-aaa");
        all[2].RunId.Should().Be("run-zzz", "oldest last by CreatedAt");
    }

    [Fact]
    public void GetEvidenceDir_ReturnsPathInsideSpecDir()
    {
        var dir = EvidenceStore.GetEvidenceDir("spec-001", "run-001");

        dir.Should().Contain("spec-001");
        dir.Should().Contain("evidence");
        dir.Should().Contain("run-001");
    }

    [Fact]
    public async Task EvidenceDir_IsInsideSpecDir_ArchiveMovesEvidence()
    {
        // spec + evidence 생성
        var spec = new Spec
        {
            Id = "spec-ev-archive",
            ProjectId = "test-project",
            Title = "Archive Test",
            State = FlowState.Failed,
            ProcessingStatus = ProcessingStatus.Error,
            Version = 1
        };
        await _store.SaveAsync(spec, 0);

        await EvidenceStore.SaveManifestAsync(new EvidenceManifest
        {
            SpecId = "spec-ev-archive",
            RunId = "run-001",
            CreatedAt = DateTimeOffset.UtcNow,
            Refs = [new EvidenceRef { Kind = "log", RelativePath = "build.log", Summary = "build failed" }]
        });

        // evidence가 spec 디렉토리 안에 있으므로 archive 시 함께 이동
        await _store.ArchiveAsync("spec-ev-archive");

        // 원본 spec 디렉토리는 삭제됨
        var original = await _store.LoadAsync("spec-ev-archive");
        original.Should().BeNull();

        // archived에서 로드 가능
        var archived = await _store.LoadArchivedAsync("spec-ev-archive");
        archived.Should().NotBeNull();
    }
}
