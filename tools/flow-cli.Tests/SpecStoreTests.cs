using FlowCLI.Services.SpecGraph;
using FluentAssertions;
using System.Text.Json;

namespace FlowCLI.Tests;

/// <summary>
/// SpecStore CRUD 테스트
/// </summary>
public class SpecStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SpecStore _store;

    public SpecStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-spec-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SpecStore(_tempDir);
        _store.Initialize();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Initialize_CreatesDirectories()
    {
        var specsDir = Path.Combine(_tempDir, "docs", "specs");
        var evidenceDir = Path.Combine(_tempDir, "docs", "evidence");

        Directory.Exists(specsDir).Should().BeTrue();
        Directory.Exists(evidenceDir).Should().BeTrue();
        File.Exists(Path.Combine(specsDir, ".schema-version")).Should().BeTrue();
        File.ReadAllText(Path.Combine(specsDir, ".schema-version")).Should().Be("2");
    }

    [Fact]
    public void Create_SavesSpecToFile()
    {
        var spec = CreateTestSpec("F-001", "로그인 기능");
        var created = _store.Create(spec);

        created.Id.Should().Be("F-001");
        created.CreatedAt.Should().NotBeNull();
        created.UpdatedAt.Should().NotBeNull();
        created.SchemaVersion.Should().Be(2);

        var loaded = _store.Get("F-001");
        loaded.Should().NotBeNull();
        loaded!.Title.Should().Be("로그인 기능");
    }

    [Fact]
    public void Create_DuplicateId_ThrowsException()
    {
        _store.Create(CreateTestSpec("F-001", "기능1"));

        var act = () => _store.Create(CreateTestSpec("F-001", "기능2"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*이미 존재*");
    }

    [Fact]
    public void Create_EmptyId_ThrowsException()
    {
        var spec = new SpecNode { Id = "", Title = "test" };
        var act = () => _store.Create(spec);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        _store.Get("F-999").Should().BeNull();
    }

        [Fact]
        public void Get_ObjectStyleCodeRefs_LoadsPathValues()
        {
                var specPath = Path.Combine(_tempDir, "docs", "specs", "F-080.json");
                var json = """
                {
                    "schemaVersion": 2,
                    "id": "F-080",
                    "nodeType": "feature",
                    "title": "객체형 codeRefs 호환",
                    "description": "하위 호환 테스트",
                    "status": "working",
                    "dependencies": [],
                    "conditions": [
                        {
                            "id": "F-080-C1",
                            "nodeType": "condition",
                            "description": "조건",
                            "status": "working",
                            "codeRefs": [
                                { "path": "tools/flow-cli/Program.cs", "description": "Program" }
                            ],
                            "evidence": []
                        }
                    ],
                    "codeRefs": [
                        { "path": "tools/flow-cli/FlowApp.cs", "description": "FlowApp" }
                    ],
                    "evidence": [],
                    "tags": []
                }
                """;

                File.WriteAllText(specPath, json);

                var loaded = _store.Get("F-080");

                loaded.Should().NotBeNull();
                loaded!.CodeRefs.Should().ContainSingle().Which.Should().Be("tools/flow-cli/FlowApp.cs");
                loaded.Conditions.Should().ContainSingle();
                loaded.Conditions[0].CodeRefs.Should().ContainSingle().Which.Should().Be("tools/flow-cli/Program.cs");
        }

    [Fact]
    public void GetAll_ReturnsAllSpecs()
    {
        _store.Create(CreateTestSpec("F-001", "기능1"));
        _store.Create(CreateTestSpec("F-002", "기능2"));
        _store.Create(CreateTestSpec("F-003", "기능3"));

        var all = _store.GetAll();
        all.Should().HaveCount(3);
    }

    [Fact]
    public void Update_ModifiesSpec()
    {
        _store.Create(CreateTestSpec("F-001", "원래 제목"));

        var spec = _store.Get("F-001")!;
        spec.Title = "수정된 제목";
        spec.Status = "working";
        _store.Update(spec);

        var updated = _store.Get("F-001");
        updated!.Title.Should().Be("수정된 제목");
        updated.Status.Should().Be("working");
    }

    [Fact]
    public void Update_NonExistent_ThrowsException()
    {
        var spec = CreateTestSpec("F-999", "없는 스펙");
        var act = () => _store.Update(spec);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Delete_RemovesSpec()
    {
        _store.Create(CreateTestSpec("F-001", "삭제 대상"));

        _store.Delete("F-001").Should().BeTrue();
        _store.Get("F-001").Should().BeNull();
        _store.Exists("F-001").Should().BeFalse();
    }

    [Fact]
    public void Delete_NonExistent_ReturnsFalse()
    {
        _store.Delete("F-999").Should().BeFalse();
    }

    [Fact]
    public void NextId_ReturnsSequentialId()
    {
        _store.NextId().Should().Be("F-001");

        _store.Create(CreateTestSpec("F-001", "기능1"));
        _store.NextId().Should().Be("F-002");

        _store.Create(CreateTestSpec("F-002", "기능2"));
        _store.NextId().Should().Be("F-003");
    }

    [Fact]
    public void Backup_And_Restore_Works()
    {
        _store.Create(CreateTestSpec("F-001", "백업 테스트"));
        var backupPath = _store.Backup();

        Directory.Exists(backupPath).Should().BeTrue();

        // 원본 삭제 후 복구
        _store.Delete("F-001");
        _store.Get("F-001").Should().BeNull();

        var timestamp = Path.GetFileName(backupPath);
        _store.Restore(timestamp);

        var restored = _store.Get("F-001");
        restored.Should().NotBeNull();
        restored!.Title.Should().Be("백업 테스트");
    }

    [Fact]
    public void ListBackups_ReturnsTimestamps()
    {
        _store.Create(CreateTestSpec("F-001", "기능1"));
        _store.Backup();

        var backups = _store.ListBackups();
        backups.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Exists_ReturnsTrueForExistingSpec()
    {
        _store.Create(CreateTestSpec("F-001", "존재하는 스펙"));
        _store.Exists("F-001").Should().BeTrue();
        _store.Exists("F-999").Should().BeFalse();
    }

    private static SpecNode CreateTestSpec(string id, string title, string? parent = null) => new()
    {
        Id = id,
        Title = title,
        Description = $"{title} 설명",
        Status = "draft",
        Parent = parent,
        NodeType = "feature"
    };
}
