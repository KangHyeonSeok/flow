using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowCLI.Services.Runner;

namespace FlowCLI.Services.SpecGraph;

/// <summary>
/// 파일 기반 Spec 저장소. docs/specs/ 하위에 {id}.json 형태로 저장.
/// </summary>
public class SpecStore
{
    private readonly string _specsDir;
    private readonly string _backupDir;
    private readonly string _evidenceDir;
    private readonly string _schemaVersionPath;
    private BrokenSpecDiagService? _diagService;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string SpecsDir => _specsDir;
    public string EvidenceDir => _evidenceDir;

    /// <summary>
    /// 손상 스펙 진단 서비스를 설정한다 (F-025-C1).
    /// 설정하면 GetAll()에서 파싱 실패 시 진단 레코드를 기록한다.
    /// </summary>
    public void SetDiagService(BrokenSpecDiagService diagService)
    {
        _diagService = diagService;
    }

    public SpecStore(string projectRoot)
    {
        _specsDir = Path.Combine(projectRoot, "docs", "specs");
        _backupDir = Path.Combine(_specsDir, ".backup");
        _evidenceDir = Path.Combine(projectRoot, "docs", "evidence");
        _schemaVersionPath = Path.Combine(_specsDir, ".schema-version");
    }

    /// <summary>
    /// 외부 스펙 저장소 경로를 직접 지정하는 생성자.
    /// ~/.flow/specs/{repo}/ 등 별도 체크아웃 경로 사용 시.
    /// </summary>
    public SpecStore(string specsDir, bool externalRepo)
    {
        _specsDir = specsDir;
        _backupDir = Path.Combine(_specsDir, ".backup");
        _evidenceDir = Path.Combine(Path.GetDirectoryName(_specsDir) ?? _specsDir, "evidence");
        _schemaVersionPath = Path.Combine(_specsDir, ".schema-version");
    }

    /// <summary>디렉토리 초기화</summary>
    public void Initialize()
    {
        Directory.CreateDirectory(_specsDir);
        Directory.CreateDirectory(_evidenceDir);

        if (!File.Exists(_schemaVersionPath))
            File.WriteAllText(_schemaVersionPath, "2");
    }

    /// <summary>spec 생성</summary>
    public SpecNode Create(SpecNode spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Id))
            throw new ArgumentException("Spec ID는 필수입니다.");

        var path = GetSpecPath(spec.Id);
        if (File.Exists(path))
            throw new InvalidOperationException($"Spec '{spec.Id}'가 이미 존재합니다.");

        spec.CreatedAt = DateTime.UtcNow.ToString("o");
        spec.UpdatedAt = spec.CreatedAt;
        spec.SchemaVersion = 2;

        // F-021-C1: 초기 생성 changeLog 자동 기록
        if (!spec.ChangeLog.Any(e => string.Equals(e.Type, "create", StringComparison.OrdinalIgnoreCase)))
        {
            spec.ChangeLog.Insert(0, new SpecChangeLogEntry
            {
                Type = "create",
                At = spec.CreatedAt,
                Author = "planner",
                Summary = $"스펙 '{spec.Id}' 생성"
            });
        }

        SaveSpec(spec);
        return spec;
    }

    /// <summary>spec 조회</summary>
    public SpecNode? Get(string id)
    {
        var path = GetSpecPath(id);
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SpecNode>(json, ReadOptions);
    }

    /// <summary>모든 spec 목록 조회</summary>
    public List<SpecNode> GetAll()
    {
        if (!Directory.Exists(_specsDir))
            return new List<SpecNode>();

        var specs = new List<SpecNode>();
        foreach (var file in Directory.GetFiles(_specsDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var spec = JsonSerializer.Deserialize<SpecNode>(json, ReadOptions);
                if (spec != null)
                {
                    // F-025-C5: 이전에 broken으로 기록된 파일이 정상 복구된 경우 resolved 처리
                    _diagService?.MarkResolved(file);
                    specs.Add(spec);
                }
            }
            catch (Exception ex)
            {
                // F-025-C1: JSON 파싱 실패한 파일은 skip + 진단 캐시에 기록
                _diagService?.RecordBroken(file, ex);
            }
        }
        return specs;
    }

    /// <summary>spec 수정</summary>
    public SpecNode Update(SpecNode spec)
    {
        var path = GetSpecPath(spec.Id);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Spec '{spec.Id}'가 존재하지 않습니다.");

        spec.UpdatedAt = DateTime.UtcNow.ToString("o");
        SaveSpec(spec);
        return spec;
    }

    /// <summary>
    /// F-021-C1: 제자리 수정(in-place update). changeLog에 변경 이력을 자동으로 기록한다.
    /// 스펙의 핵심 목적과 범위가 유지될 때 사용. 대체/변형은 spec-supersede/spec-mutate 커맨드 사용.
    /// </summary>
    /// <param name="spec">수정된 스펙 (ID 유지)</param>
    /// <param name="changeSummary">변경 요약 (한 줄)</param>
    /// <param name="author">변경 주체 (사람 또는 runner ID)</param>
    /// <param name="changeType">변경 유형. 기본 "mutate". supersede/deprecate/restore도 가능.</param>
    /// <param name="relatedIds">관련 스펙 ID 목록 (선택)</param>
    public SpecNode UpdateInPlace(SpecNode spec, string changeSummary, string author,
        string changeType = "mutate", IEnumerable<string>? relatedIds = null)
    {
        spec.ChangeLog.Add(new SpecChangeLogEntry
        {
            Type = changeType,
            At = DateTime.UtcNow.ToString("o"),
            Author = author,
            Summary = changeSummary,
            RelatedIds = relatedIds?.ToList() ?? new List<string>()
        });
        return Update(spec);
    }

    /// <summary>spec 삭제</summary>
    public bool Delete(string id)
    {
        var path = GetSpecPath(id);
        if (!File.Exists(path))
            return false;

        File.Delete(path);

        // 증거 디렉토리도 삭제
        var evidencePath = Path.Combine(_evidenceDir, id);
        if (Directory.Exists(evidencePath))
            Directory.Delete(evidencePath, true);

        return true;
    }

    /// <summary>id 존재 여부</summary>
    public bool Exists(string id) => File.Exists(GetSpecPath(id));

    /// <summary>자동 ID 채번 (F-NNN 형식)</summary>
    public string NextId()
    {
        var existing = GetAll().Select(s => s.Id).ToHashSet();
        for (int i = 1; i <= 999; i++)
        {
            var id = $"F-{i:D3}";
            if (!existing.Contains(id))
                return id;
        }
        throw new InvalidOperationException("사용 가능한 ID가 없습니다 (F-001 ~ F-999).");
    }

    /// <summary>백업 생성</summary>
    public string Backup()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var backupPath = Path.Combine(_backupDir, timestamp);
        Directory.CreateDirectory(backupPath);

        foreach (var file in Directory.GetFiles(_specsDir, "*.json"))
        {
            File.Copy(file, Path.Combine(backupPath, Path.GetFileName(file)));
        }

        return backupPath;
    }

    /// <summary>백업에서 복구</summary>
    public int Restore(string timestamp)
    {
        var backupPath = Path.Combine(_backupDir, timestamp);
        if (!Directory.Exists(backupPath))
            throw new InvalidOperationException($"백업 '{timestamp}'이 존재하지 않습니다.");

        int count = 0;
        foreach (var file in Directory.GetFiles(backupPath, "*.json"))
        {
            File.Copy(file, Path.Combine(_specsDir, Path.GetFileName(file)), overwrite: true);
            count++;
        }
        return count;
    }

    /// <summary>백업 목록 조회</summary>
    public List<string> ListBackups()
    {
        if (!Directory.Exists(_backupDir))
            return new List<string>();

        return Directory.GetDirectories(_backupDir)
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Select(n => n!)
            .OrderDescending()
            .ToList();
    }

    private string GetSpecPath(string id) => Path.Combine(_specsDir, $"{id}.json");

    private void SaveSpec(SpecNode spec)
    {
        Directory.CreateDirectory(_specsDir);
        var json = JsonSerializer.Serialize(spec, WriteOptions);
        File.WriteAllText(GetSpecPath(spec.Id), json);
    }
}
