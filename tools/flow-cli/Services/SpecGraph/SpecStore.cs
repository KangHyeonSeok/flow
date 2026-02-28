using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    public SpecStore(string projectRoot)
    {
        _specsDir = Path.Combine(projectRoot, "docs", "specs");
        _backupDir = Path.Combine(_specsDir, ".backup");
        _evidenceDir = Path.Combine(projectRoot, "docs", "evidence");
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
                    specs.Add(spec);
            }
            catch
            {
                // JSON 파싱 실패한 파일은 skip (에러 핸들링 정책)
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
