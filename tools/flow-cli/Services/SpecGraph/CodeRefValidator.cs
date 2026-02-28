namespace FlowCLI.Services.SpecGraph;

/// <summary>
/// 코드 참조(codeRefs) 검증기.
/// - 파일 존재 여부 검사
/// - 경로 정규화 (POSIX, 대소문자 무시)
/// </summary>
public class CodeRefValidator
{
    private readonly string _projectRoot;

    public CodeRefValidator(string projectRoot)
    {
        _projectRoot = projectRoot;
    }

    /// <summary>
    /// 모든 스펙의 codeRefs를 검증합니다.
    /// </summary>
    public CodeRefCheckResult CheckAll(List<SpecNode> specs)
    {
        var result = new CodeRefCheckResult();

        foreach (var spec in specs)
        {
            // Feature 수준 codeRefs
            foreach (var codeRef in spec.CodeRefs)
            {
                result.TotalRefs++;
                var check = CheckRef(codeRef);
                if (check == null)
                    result.ValidRefs++;
                else
                {
                    result.InvalidRefs++;
                    result.InvalidItems.Add(new InvalidCodeRef
                    {
                        SpecId = spec.Id,
                        CodeRef = codeRef,
                        Reason = check
                    });
                }
            }

            // Condition 수준 codeRefs
            foreach (var cond in spec.Conditions)
            {
                foreach (var codeRef in cond.CodeRefs)
                {
                    result.TotalRefs++;
                    var check = CheckRef(codeRef);
                    if (check == null)
                        result.ValidRefs++;
                    else
                    {
                        result.InvalidRefs++;
                        result.InvalidItems.Add(new InvalidCodeRef
                        {
                            SpecId = $"{spec.Id}/{cond.Id}",
                            CodeRef = codeRef,
                            Reason = check
                        });
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 개별 codeRef를 검증합니다. null이면 유효, 문자열이면 에러 이유.
    /// 
    /// codeRef 형식: "path/to/file.cs#L10-L30" 또는 "path/to/file.cs"
    /// </summary>
    private string? CheckRef(string codeRef)
    {
        if (string.IsNullOrWhiteSpace(codeRef))
            return "빈 codeRef";

        // 라인 범위 분리
        var parts = codeRef.Split('#');
        var filePath = NormalizePath(parts[0]);

        // 절대 경로 생성
        var fullPath = Path.Combine(_projectRoot, filePath);

        // Windows 경로 정규화
        fullPath = fullPath.Replace('/', Path.DirectorySeparatorChar);

        if (!File.Exists(fullPath))
            return $"파일이 존재하지 않음: {filePath}";

        // 라인 범위가 있으면 파일 라인 수와 비교
        if (parts.Length > 1)
        {
            var lineSpec = parts[1];
            var lineRange = ParseLineRange(lineSpec);
            if (lineRange != null)
            {
                var lineCount = File.ReadAllLines(fullPath).Length;
                if (lineRange.Value.start > lineCount)
                    return $"라인 범위 초과: {lineSpec} (파일: {lineCount}줄)";
            }
        }

        return null;
    }

    /// <summary>
    /// 경로 정규화: POSIX 스타일, 정규화.
    /// </summary>
    private static string NormalizePath(string path)
    {
        // 백슬래시 → 슬래시
        path = path.Replace('\\', '/');
        // 앞뒤 공백 및 슬래시 제거
        path = path.Trim().TrimStart('/');
        return path;
    }

    /// <summary>
    /// "#L10-L30" 또는 "#L10" 형식의 라인 범위를 파싱합니다.
    /// </summary>
    private static (int start, int end)? ParseLineRange(string lineSpec)
    {
        // "L10-L30" or "L10"
        lineSpec = lineSpec.TrimStart('L', 'l');
        var parts = lineSpec.Split('-');

        if (int.TryParse(parts[0].TrimStart('L', 'l'), out var start))
        {
            var end = start;
            if (parts.Length > 1 && int.TryParse(parts[1].TrimStart('L', 'l'), out var endLine))
                end = endLine;
            return (start, end);
        }

        return null;
    }
}
