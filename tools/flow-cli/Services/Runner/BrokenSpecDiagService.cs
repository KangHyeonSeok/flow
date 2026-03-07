using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowCLI.Services.SpecGraph;

namespace FlowCLI.Services.Runner;

/// <summary>
/// 손상 스펙 JSON 진단 캐시를 관리하는 서비스 (F-025).
/// .flow/spec-cache/broken-spec-diag.json 을 읽고 쓴다.
/// </summary>
public class BrokenSpecDiagService
{
    private readonly string _diagCachePath;
    private readonly RunnerLogService _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    /// <param name="specCacheDir">.flow/spec-cache/ 디렉토리 경로</param>
    public BrokenSpecDiagService(string specCacheDir, RunnerLogService log)
    {
        _diagCachePath = Path.Combine(specCacheDir, "broken-spec-diag.json");
        _log = log;
    }

    public string DiagCachePath => _diagCachePath;

    // ── 캐시 읽기/쓰기 ─────────────────────────────────────────

    private BrokenSpecDiagCache LoadCache()
    {
        try
        {
            if (File.Exists(_diagCachePath))
            {
                var json = File.ReadAllText(_diagCachePath);
                return JsonSerializer.Deserialize<BrokenSpecDiagCache>(json, JsonOpts)
                       ?? new BrokenSpecDiagCache();
            }
        }
        catch { /* 캐시 파일 자체가 손상된 경우 빈 캐시로 시작 */ }
        return new BrokenSpecDiagCache();
    }

    private void SaveCache(BrokenSpecDiagCache cache)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_diagCachePath)!);
            var json = JsonSerializer.Serialize(cache, JsonOpts);
            File.WriteAllText(_diagCachePath, json);
        }
        catch (Exception ex)
        {
            _log.Warn("diag-cache", $"진단 캐시 저장 실패: {ex.Message}");
        }
    }

    // ── 진단 기록 ──────────────────────────────────────────────

    /// <summary>
    /// 파싱 실패 스펙 파일의 진단 레코드를 기록/갱신한다.
    /// </summary>
    public void RecordBroken(string filePath, Exception ex)
    {
        var specId = Path.GetFileNameWithoutExtension(filePath);
        var now = DateTime.UtcNow.ToString("o");
        var errorMessage = ex.Message;
        int? line = null;
        int? column = null;

        // JSON 파일 내용으로 line/column 계산 시도
        try
        {
            var content = File.ReadAllText(filePath);
            (line, column) = ExtractLineCol(content, errorMessage);
        }
        catch { /* 파일 읽기 실패 시 무시 */ }

        string? fileMtime = null;
        try
        {
            fileMtime = File.GetLastWriteTimeUtc(filePath).ToString("o");
        }
        catch { /* ignore */ }

        var cache = LoadCache();
        var existing = cache.Records.Find(r => r.FilePath == filePath);
        if (existing != null)
        {
            existing.ErrorMessage = errorMessage;
            existing.Line = line;
            existing.Column = column;
            existing.DetectedAt = now;
            existing.FileMtime = fileMtime;
            existing.Status = "unresolved";
            existing.LastCheckedAt = now;
        }
        else
        {
            cache.Records.Add(new BrokenSpecDiagRecord
            {
                SpecId = specId,
                FilePath = filePath,
                ErrorMessage = errorMessage,
                Line = line,
                Column = column,
                DetectedAt = now,
                FileMtime = fileMtime,
                Status = "unresolved",
                LastCheckedAt = now,
            });
        }

        SaveCache(cache);
        _log.Warn("diag-cache", $"손상 스펙 기록: {specId} — {errorMessage}", specId);
    }

    /// <summary>
    /// 스펙이 정상 복구되었을 때 resolved로 마킹하고 lastCheckedAt을 갱신한다.
    /// </summary>
    public void MarkResolved(string filePath)
    {
        var cache = LoadCache();
        var record = cache.Records.Find(r => r.FilePath == filePath && r.Status == "unresolved");
        if (record != null)
        {
            record.Status = "resolved";
            record.ResolvedAt = DateTime.UtcNow.ToString("o");
            record.LastCheckedAt = record.ResolvedAt;
            SaveCache(cache);
            _log.Info("diag-cache", $"손상 스펙 복구 완료: {record.SpecId}", record.SpecId);
        }
    }

    /// <summary>
    /// 복구 실패가 누적되어 수동 검토 대상으로 승격한다.
    /// </summary>
    public void MarkEscalated(string specId, string failReason)
    {
        var cache = LoadCache();
        var record = cache.Records.Find(r => r.SpecId == specId && r.Status == "unresolved");
        if (record != null)
        {
            record.Status = "escalated";
            record.FailReason = failReason;
            record.LastCheckedAt = DateTime.UtcNow.ToString("o");
            SaveCache(cache);
            _log.Warn("diag-cache", $"손상 스펙 수동 검토 승격: {specId} — {failReason}", specId);
        }
    }

    /// <summary>
    /// 미해결 진단 레코드 반환.
    /// </summary>
    public List<BrokenSpecDiagRecord> GetUnresolved()
        => LoadCache().Records.Where(r => r.Status == "unresolved").ToList();

    // ── Fresh Scan ─────────────────────────────────────────────

    /// <summary>
    /// specsDir를 직접 스캔하여 파싱 불가 파일을 발견하면 진단 캐시에 기록/갱신한다.
    /// 이전에 broken이었다가 현재 정상인 파일은 resolved로 마킹한다.
    /// </summary>
    public List<BrokenSpecDiagRecord> ScanAndUpdate(string specsDir)
    {
        if (!Directory.Exists(specsDir))
            return new List<BrokenSpecDiagRecord>();

        var found = new List<BrokenSpecDiagRecord>();

        foreach (var filePath in Directory.GetFiles(specsDir, "*.json"))
        {
            try
            {
                var content = File.ReadAllText(filePath);
                JsonSerializer.Deserialize<object>(content);
                // 파싱 성공 → 이전에 broken으로 기록되었으면 resolved 처리
                MarkResolved(filePath);
            }
            catch (Exception ex)
            {
                RecordBroken(filePath, ex);
                var specId = Path.GetFileNameWithoutExtension(filePath);
                found.Add(new BrokenSpecDiagRecord { SpecId = specId, FilePath = filePath, ErrorMessage = ex.Message });
            }
        }

        return found;
    }

    // ── 유틸리티 ───────────────────────────────────────────────

    /// <summary>
    /// JSON 내용과 오류 메시지에서 line/column을 계산한다.
    /// JsonException의 LineNumber/BytePositionInLine 속성을 우선 사용.
    /// </summary>
    private static (int? line, int? column) ExtractLineCol(string content, string errorMessage)
    {
        // "line N, position M" 형식 파싱 (JsonException 메시지)
        var lcMatch = System.Text.RegularExpressions.Regex.Match(
            errorMessage, @"line\s+(\d+),?\s+position\s+(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (lcMatch.Success)
        {
            return (int.Parse(lcMatch.Groups[1].Value), int.Parse(lcMatch.Groups[2].Value));
        }

        // "at position N" 형식 파싱 (일부 파서)
        var posMatch = System.Text.RegularExpressions.Regex.Match(
            errorMessage, @"at position (\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (posMatch.Success && int.TryParse(posMatch.Groups[1].Value, out var pos)
            && pos >= 0 && pos <= content.Length)
        {
            var before = content[..pos];
            var lineNum = before.Count(c => c == '\n') + 1;
            var lastNl = before.LastIndexOf('\n');
            var colNum = lastNl >= 0 ? pos - lastNl : pos + 1;
            return (lineNum, colNum);
        }

        return (null, null);
    }
}
