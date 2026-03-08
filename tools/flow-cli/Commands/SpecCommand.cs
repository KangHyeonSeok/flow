using System.Globalization;
using System.Text.Json;
using Cocona;
using FlowCLI.Services.Runner;
using FlowCLI.Services.SpecGraph;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    private SpecStore? _specStore;
    private SpecStore SpecStore => _specStore ??= new SpecStore(PathResolver.ProjectRoot);

    private SpecValidator? _specValidator;
    private SpecValidator SpecValidator => _specValidator ??= new SpecValidator();

    private GraphBuilder? _graphBuilder;
    private GraphBuilder GraphBuilder => _graphBuilder ??= new GraphBuilder();

    private ImpactAnalyzer? _impactAnalyzer;
    private ImpactAnalyzer ImpactAnalyzer => _impactAnalyzer ??= new ImpactAnalyzer();

    private StatusPropagator? _statusPropagator;
    private StatusPropagator StatusPropagator => _statusPropagator ??= new StatusPropagator();

    private CodeRefValidator? _codeRefValidator;
    private CodeRefValidator CodeRefValidator => _codeRefValidator ??= new CodeRefValidator(PathResolver.ProjectRoot);

    // ─── flow spec init ───────────────────────────────────────────────
    [Command("spec-init", Description = "스펙 디렉토리를 초기화합니다")]
    public void SpecInit(
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            SpecStore.Initialize();
            JsonOutput.Write(JsonOutput.Success("spec-init",
                new { specsDir = SpecStore.SpecsDir },
                "스펙 디렉토리가 초기화되었습니다."), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-init", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    // ─── flow spec create ─────────────────────────────────────────────
    [Command("spec-create", Description = "새 스펙을 생성합니다")]
    public void SpecCreate(
        [Option("id", Description = "스펙 ID (생략 시 자동 채번)")] string? id = null,
        [Option("title", Description = "제목")] string title = "",
        [Option("description", Description = "설명")] string? description = null,
        [Option("parent", Description = "상위 스펙 ID")] string? parent = null,
        [Option("status", Description = "상태 (draft|queued|working|needs-review|verified|deprecated|done)")] string status = "draft",
        [Option("tags", Description = "태그 (콤마 구분)")] string? tags = null,
        [Option("dependencies", Description = "의존 스펙 ID 목록 (콤마 구분)")] string? dependencies = null,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var specId = id ?? SpecStore.NextId();

            var spec = new SpecNode
            {
                Id = specId,
                Title = title,
                Description = description ?? title,
                Parent = parent,
                Status = status,
                Tags = tags?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList() ?? new(),
                Dependencies = dependencies?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList() ?? new()
            };

            var created = SpecStore.Create(spec);

            JsonOutput.Write(JsonOutput.Success("spec-create",
                created,
                $"스펙 '{specId}'가 생성되었습니다."), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-create", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    // ─── flow spec get ────────────────────────────────────────────────
    [Command("spec-get", Description = "스펙을 조회합니다")]
    public void SpecGet(
        [Argument(Description = "스펙 ID")] string id,
        [Option("json", Description = "Raw JSON 출력")] bool json = false,
        [Option("pretty", Description = "Pretty print JSON (--json 과 함께 사용)")] bool pretty = false)
    {
        try
        {
            var spec = SpecStore.Get(id);
            if (spec == null)
            {
                if (json)
                    JsonOutput.Write(JsonOutput.Error("spec-get", $"스펙 '{id}'을(를) 찾을 수 없습니다."), pretty);
                else
                    Console.WriteLine($"ERROR: 스펙 '{id}'을(를) 찾을 수 없습니다.");
                Environment.ExitCode = 1;
                return;
            }
            if (json)
            {
                JsonOutput.Write(JsonOutput.Success("spec-get", spec), pretty);
            }
            else
            {
                Console.WriteLine(FormatSpecForAI(spec));
            }
        }
        catch (Exception ex)
        {
            if (json)
                JsonOutput.Write(JsonOutput.Error("spec-get", ex.Message), pretty);
            else
                Console.WriteLine($"ERROR: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    // ─── flow spec append-review ─────────────────────────────────────
    [Command("spec-append-review", Description = "리뷰 JSON을 metadata.review에 반영합니다. 미해결 질문이 있으면 needs-review 상태로 사용자 입력을 대기하고, 없으면 queued로 재배치합니다")]
    public void SpecAppendReview(
        [Argument(Description = "스펙 ID")] string id,
        [Option("input-file", Description = "리뷰 JSON 파일 경로")] string inputFile = "",
        [Option("reviewer", Description = "리뷰어 ID")] string reviewer = "copilot-cli-review",
        [Option("reviewed-at", Description = "리뷰 시각 (ISO-8601, 생략 시 현재 UTC)")] string? reviewedAt = null,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(inputFile))
            {
                JsonOutput.Write(JsonOutput.Error("spec-append-review", "--input-file 값은 필수입니다."), pretty);
                Environment.ExitCode = 1;
                return;
            }

            if (!File.Exists(inputFile))
            {
                JsonOutput.Write(JsonOutput.Error("spec-append-review", $"리뷰 JSON 파일 '{inputFile}'을(를) 찾을 수 없습니다."), pretty);
                Environment.ExitCode = 1;
                return;
            }

            var spec = SpecStore.Get(id);
            if (spec == null)
            {
                JsonOutput.Write(JsonOutput.Error("spec-append-review", $"스펙 '{id}'을(를) 찾을 수 없습니다."), pretty);
                Environment.ExitCode = 1;
                return;
            }

            var rawJson = File.ReadAllText(inputFile);
            if (!RunnerService.TryParseReviewAnalysisJson(rawJson, out var analysis, out var parseError))
            {
                JsonOutput.Write(JsonOutput.Error("spec-append-review", parseError ?? "리뷰 JSON 파싱에 실패했습니다."), pretty);
                Environment.ExitCode = 1;
                return;
            }

            var reviewedAtUtc = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(reviewedAt))
            {
                if (!DateTime.TryParse(reviewedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out reviewedAtUtc))
                {
                    JsonOutput.Write(JsonOutput.Error("spec-append-review", $"reviewed-at 값 '{reviewedAt}'은(는) 올바른 ISO-8601 형식이 아닙니다."), pretty);
                    Environment.ExitCode = 1;
                    return;
                }

                reviewedAtUtc = reviewedAtUtc.ToUniversalTime();
            }

            RunnerService.ApplyReviewAnalysis(spec, analysis, reviewer, reviewedAtUtc);
            var updated = SpecStore.Update(spec);

            JsonOutput.Write(JsonOutput.Success("spec-append-review",
                new
                {
                    id = updated.Id,
                    status = updated.Status,
                    reviewDisposition = GetMetadataString(updated, "reviewDisposition"),
                    openQuestionCount = CountOpenQuestions(updated),
                    inputFile
                },
                $"스펙 '{updated.Id}'에 review 메타데이터를 반영했습니다."), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-append-review", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    private static string FormatSpecForAI(SpecNode spec)
    {
        var sb = new System.Text.StringBuilder();

        // 헤더
        sb.AppendLine($"[{spec.Id}] {spec.Title}");
        sb.AppendLine(new string('─', 60));

        // 기본 정보
        var meta = new List<string> { $"status:{spec.Status}", $"type:{spec.NodeType}" };
        if (!string.IsNullOrEmpty(spec.Parent)) meta.Add($"parent:{spec.Parent}");
        if (spec.Dependencies.Count > 0) meta.Add($"deps:{string.Join(",", spec.Dependencies)}");
        sb.AppendLine(string.Join(" | ", meta));

        if (spec.Tags.Count > 0)
            sb.AppendLine($"tags: {string.Join(", ", spec.Tags)}");

        // 설명
        if (!string.IsNullOrWhiteSpace(spec.Description))
        {
            sb.AppendLine();
            sb.AppendLine("## Description");
            sb.AppendLine(spec.Description);
        }

        // 수락 조건
        if (spec.Conditions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## Conditions ({spec.Conditions.Count})");
            foreach (var c in spec.Conditions)
            {
                var codeInfo = c.CodeRefs.Count > 0 ? $" [refs:{c.CodeRefs.Count}]" : "";
                var evidenceInfo = c.Evidence.Count > 0 ? $" [ev:{c.Evidence.Count}]" : "";
                sb.AppendLine($"  {c.Id} [{c.Status}]{codeInfo}{evidenceInfo}");
                sb.AppendLine($"    {c.Description}");
            }
        }

        // 코드 참조
        if (spec.CodeRefs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Code Refs");
            foreach (var r in spec.CodeRefs)
                sb.AppendLine($"  {r}");
        }

        // 증거
        if (spec.Evidence.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Evidence");
            foreach (var e in spec.Evidence)
            {
                var summary = e.Summary != null ? $": {e.Summary}" : "";
                sb.AppendLine($"  [{e.Type}] {e.Path}{summary}");
            }
        }

        // F-021-C5: 관계 요약 (supersedes/mutates/supersededBy/mutatedBy)
        var hasRelations = spec.Supersedes.Count > 0 || spec.SupersededBy.Count > 0
            || spec.Mutates.Count > 0 || spec.MutatedBy.Count > 0;
        if (hasRelations)
        {
            sb.AppendLine();
            sb.AppendLine("## Relationships");
            if (spec.Supersedes.Count > 0)
                sb.AppendLine($"  supersedes:   {string.Join(", ", spec.Supersedes)}  ← 이 스펙이 대체함");
            if (spec.SupersededBy.Count > 0)
                sb.AppendLine($"  supersededBy: {string.Join(", ", spec.SupersededBy)}  ← 이 스펙을 대체한 신규 스펙");
            if (spec.Mutates.Count > 0)
                sb.AppendLine($"  mutates:      {string.Join(", ", spec.Mutates)}  ← 이 스펙이 변형함");
            if (spec.MutatedBy.Count > 0)
                sb.AppendLine($"  mutatedBy:    {string.Join(", ", spec.MutatedBy)}  ← 이 스펙을 변형한 스펙");

            // 권장 후속 조치
            if (spec.SupersededBy.Count > 0 && spec.Status != "deprecated")
                sb.AppendLine($"  ⚠ 권장 조치: supersededBy 스펙({string.Join(", ", spec.SupersededBy)}) 검토 후 deprecated 전환 고려");
            else if (spec.Supersedes.Count > 0)
                sb.AppendLine($"  ✓ 이 스펙이 최신 기준 ({string.Join(", ", spec.Supersedes)}을(를) 대체)");
        }

        // F-021-C1: 변경 이력 요약
        if (spec.ChangeLog.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"## Change Log ({spec.ChangeLog.Count} entries)");
            foreach (var entry in spec.ChangeLog.TakeLast(5))
            {
                var related = entry.RelatedIds.Count > 0 ? $" → {string.Join(",", entry.RelatedIds)}" : "";
                sb.AppendLine($"  [{entry.Type}] {entry.At[..Math.Min(19, entry.At.Length)]} by {entry.Author}{related}");
                sb.AppendLine($"    {entry.Summary}");
            }
            if (spec.ChangeLog.Count > 5)
                sb.AppendLine($"  ... 및 {spec.ChangeLog.Count - 5}개 이전 항목");
        }

        return sb.ToString().TrimEnd();
    }

    private static bool GetMetadataBool(SpecNode spec, string key)
    {
        if (spec.Metadata == null || !spec.Metadata.TryGetValue(key, out var rawValue) || rawValue == null)
        {
            return false;
        }

        if (rawValue is bool boolValue)
        {
            return boolValue;
        }

        return bool.TryParse(rawValue.ToString(), out var parsed) && parsed;
    }

    private static string? GetMetadataString(SpecNode spec, string key)
    {
        if (spec.Metadata == null || !spec.Metadata.TryGetValue(key, out var rawValue) || rawValue == null)
        {
            return null;
        }

        return rawValue.ToString();
    }

    private static int CountOpenQuestions(SpecNode spec)
    {
        if (spec.Metadata == null || !spec.Metadata.TryGetValue("questions", out var rawQuestions) || rawQuestions == null)
        {
            return 0;
        }

        if (rawQuestions is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Count(question =>
                question.ValueKind == JsonValueKind.Object
                && question.TryGetProperty("status", out var status)
                && string.Equals(status.GetString(), "open", StringComparison.OrdinalIgnoreCase));
        }

        if (rawQuestions is IEnumerable<object> questions)
        {
            return questions.Count(question =>
            {
                if (question is JsonElement element)
                {
                    return element.ValueKind == JsonValueKind.Object
                        && element.TryGetProperty("status", out var status)
                        && string.Equals(status.GetString(), "open", StringComparison.OrdinalIgnoreCase);
                }

                return question?.ToString()?.Contains("open", StringComparison.OrdinalIgnoreCase) == true;
            });
        }

        return 0;
    }

    // ─── flow spec list ───────────────────────────────────────────────
    [Command("spec-list", Description = "모든 스펙을 목록 조회합니다")]
    public void SpecList(
        [Option("status", Description = "상태 필터")] string? status = null,
        [Option("tag", Description = "태그 필터")] string? tag = null,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var specs = SpecStore.GetAll();

            if (!string.IsNullOrEmpty(status))
                specs = specs.Where(s => s.Status == status).ToList();

            if (!string.IsNullOrEmpty(tag))
                specs = specs.Where(s => s.Tags.Contains(tag)).ToList();

            var summary = specs.Select(s => new
            {
                s.Id,
                s.Title,
                s.Status,
                s.Parent,
                Dependencies = s.Dependencies.Count,
                Conditions = s.Conditions.Count,
                s.Tags
            }).ToList();

            JsonOutput.Write(JsonOutput.Success("spec-list",
                new { count = summary.Count, specs = summary }), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-list", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    // ─── flow spec delete ─────────────────────────────────────────────
    [Command("spec-delete", Description = "스펙을 삭제합니다")]
    public void SpecDelete(
        [Argument(Description = "스펙 ID")] string id,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            if (SpecStore.Delete(id))
                JsonOutput.Write(JsonOutput.Success("spec-delete", new { id }, $"스펙 '{id}'가 삭제되었습니다."), pretty);
            else
            {
                JsonOutput.Write(JsonOutput.Error("spec-delete", $"스펙 '{id}'을(를) 찾을 수 없습니다."), pretty);
                Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-delete", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    // ─── flow spec supersede ──────────────────────────────────────────
    /// <summary>
    /// F-021-C2: 기존 스펙의 목적/범위/수락 기준이 실질적으로 바뀌는 경우 신규 스펙을 생성하고
    /// 기존 스펙과 양방향 supersedes↔supersededBy 관계를 설정한다.
    /// 기존 스펙이 활성 상태이거나 downstream 참조가 있으면 F-021-C3 경고를 함께 반환한다.
    /// </summary>
    [Command("spec-supersede", Description = "기존 스펙을 대체하는 신규 스펙을 생성하고 양방향 관계를 설정합니다 (F-021-C2)")]
    public void SpecSupersede(
        [Argument(Description = "대체할 기존 스펙 ID")] string oldId,
        [Option("title", Description = "신규 스펙 제목")] string title = "",
        [Option("description", Description = "신규 스펙 설명")] string? description = null,
        [Option("id", Description = "신규 스펙 ID (생략 시 자동 채번)")] string? newId = null,
        [Option("parent", Description = "신규 스펙 상위 ID")] string? parent = null,
        [Option("status", Description = "신규 스펙 초기 상태 (기본: draft)")] string status = "draft",
        [Option("tags", Description = "태그 (콤마 구분)")] string? tags = null,
        [Option("author", Description = "변경 주체 (기본: planner)")] string author = "planner",
        [Option("summary", Description = "변경 사유 요약")] string? summary = null,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var oldSpec = SpecStore.Get(oldId);
            if (oldSpec == null)
            {
                JsonOutput.Write(JsonOutput.Error("spec-supersede", $"기존 스펙 '{oldId}'을(를) 찾을 수 없습니다."), pretty);
                Environment.ExitCode = 1;
                return;
            }

            var specId = newId ?? SpecStore.NextId();
            var changeSummary = summary ?? $"'{oldId}' 스펙을 대체하는 신규 스펙 '{specId}' 생성";

            // 신규 스펙 생성 (supersedes 관계 설정)
            var newSpec = new SpecNode
            {
                Id = specId,
                Title = string.IsNullOrWhiteSpace(title) ? $"{oldSpec.Title} (대체)" : title,
                Description = description ?? oldSpec.Description,
                Parent = parent ?? oldSpec.Parent,
                Status = status,
                Tags = tags?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList()
                        ?? new List<string>(oldSpec.Tags),
                Dependencies = new List<string>(oldSpec.Dependencies),
                Supersedes = new List<string> { oldId },
                ChangeLog = new List<SpecChangeLogEntry>
                {
                    new() { Type = "supersede", At = DateTime.UtcNow.ToString("o"),
                            Author = author, Summary = changeSummary, RelatedIds = new List<string> { oldId } }
                }
            };
            var created = SpecStore.Create(newSpec);

            // 기존 스펙에 supersededBy 역방향 포인터 추가 (F-021-C2)
            if (!oldSpec.SupersededBy.Contains(specId))
                oldSpec.SupersededBy.Add(specId);
            SpecStore.UpdateInPlace(oldSpec,
                $"신규 스펙 '{specId}'에 의해 대체됨",
                author, "supersede", new[] { specId });

            // F-021-C3: 안전 전환 분석
            var specs = SpecStore.GetAll();
            var graph = GraphBuilder.Build(specs);
            var transition = StatusPropagator.PropagateSupersede(graph, oldId, specId);

            JsonOutput.Write(JsonOutput.Success("spec-supersede",
                new
                {
                    newSpec = created,
                    oldSpecId = oldId,
                    transition = new
                    {
                        recommendedAction = transition.RecommendedAction,
                        isActiveSpec = transition.IsActiveSpec,
                        hasActiveDownstream = transition.HasActiveDownstream,
                        downstreamIds = transition.DownstreamIds,
                        transitionNotes = transition.TransitionNotes
                    }
                },
                $"신규 스펙 '{specId}'가 생성되었습니다. 기존 스펙 '{oldId}' 전환 권장: {transition.RecommendedAction}"), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-supersede", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    // ─── flow spec mutate ─────────────────────────────────────────────
    /// <summary>
    /// F-021-C2: 기존 스펙을 부분적으로 변형하거나 확장하는 경우 신규 스펙을 생성하고
    /// 기존 스펙과 양방향 mutates↔mutatedBy 관계를 설정한다.
    /// </summary>
    [Command("spec-mutate", Description = "기존 스펙을 부분 변형하는 신규 스펙을 생성하고 양방향 관계를 설정합니다 (F-021-C2)")]
    public void SpecMutate(
        [Argument(Description = "변형 대상 기존 스펙 ID")] string targetId,
        [Option("title", Description = "신규 스펙 제목")] string title = "",
        [Option("description", Description = "신규 스펙 설명")] string? description = null,
        [Option("id", Description = "신규 스펙 ID (생략 시 자동 채번)")] string? newId = null,
        [Option("parent", Description = "신규 스펙 상위 ID")] string? parent = null,
        [Option("status", Description = "신규 스펙 초기 상태 (기본: draft)")] string status = "draft",
        [Option("tags", Description = "태그 (콤마 구분)")] string? tags = null,
        [Option("author", Description = "변경 주체 (기본: planner)")] string author = "planner",
        [Option("summary", Description = "변경 사유 요약")] string? summary = null,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var targetSpec = SpecStore.Get(targetId);
            if (targetSpec == null)
            {
                JsonOutput.Write(JsonOutput.Error("spec-mutate", $"대상 스펙 '{targetId}'을(를) 찾을 수 없습니다."), pretty);
                Environment.ExitCode = 1;
                return;
            }

            var specId = newId ?? SpecStore.NextId();
            var changeSummary = summary ?? $"'{targetId}' 스펙을 부분 변형하는 신규 스펙 '{specId}' 생성";

            // 신규 스펙 생성 (mutates 관계 설정)
            var newSpec = new SpecNode
            {
                Id = specId,
                Title = string.IsNullOrWhiteSpace(title) ? $"{targetSpec.Title} (변형)" : title,
                Description = description ?? targetSpec.Description,
                Parent = parent ?? targetSpec.Parent,
                Status = status,
                Tags = tags?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList()
                        ?? new List<string>(targetSpec.Tags),
                Mutates = new List<string> { targetId },
                ChangeLog = new List<SpecChangeLogEntry>
                {
                    new() { Type = "mutate", At = DateTime.UtcNow.ToString("o"),
                            Author = author, Summary = changeSummary, RelatedIds = new List<string> { targetId } }
                }
            };
            var created = SpecStore.Create(newSpec);

            // 기존 스펙에 mutatedBy 역방향 포인터 추가 (F-021-C2)
            if (!targetSpec.MutatedBy.Contains(specId))
                targetSpec.MutatedBy.Add(specId);
            SpecStore.UpdateInPlace(targetSpec,
                $"신규 스펙 '{specId}'에 의해 변형됨",
                author, "mutate", new[] { specId });

            JsonOutput.Write(JsonOutput.Success("spec-mutate",
                new
                {
                    newSpec = created,
                    targetSpecId = targetId,
                    relation = $"{specId}.mutates → {targetId} / {targetId}.mutatedBy ∋ {specId}"
                },
                $"신규 스펙 '{specId}'가 생성되었습니다. '{targetId}'와 mutates 관계가 설정되었습니다."), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-mutate", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    // ─── flow spec validate ───────────────────────────────────────────
    [Command("spec-validate", Description = "스펙 유효성을 검사합니다")]
    public void SpecValidate(
        [Option("strict", Description = "엄격 모드 (수락 조건 3개+, cycle 시 exit 1)")] bool strict = false,
        [Option("id", Description = "특정 스펙만 검증")] string? id = null,
        [Option("check-tests", Description = "조건에 연결된 테스트 없으면 경고 출력 및 exit 1 (F-014-C5)")] bool checkTests = false,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var specs = SpecStore.GetAll();

            ValidationResult result;
            if (!string.IsNullOrEmpty(id))
            {
                var spec = specs.FirstOrDefault(s => s.Id == id);
                if (spec == null)
                {
                    JsonOutput.Write(JsonOutput.Error("spec-validate", $"스펙 '{id}'을(를) 찾을 수 없습니다."), pretty);
                    Environment.ExitCode = 1;
                    return;
                }
                result = SpecValidator.ValidateSpec(spec, strict, checkTests);
            }
            else
            {
                result = SpecValidator.ValidateAll(specs, strict, checkTests);
            }

            // 순환 참조 감지
            var graph = GraphBuilder.Build(specs);
            if (graph.CycleNodes.Count > 0)
            {
                result.Errors.Add(new ValidationError
                {
                    SpecId = "*",
                    Field = "dependencies",
                    Message = $"순환 참조 감지: {string.Join(", ", graph.CycleNodes)}"
                });
            }

            // orphan 노드 감지
            foreach (var orphan in graph.OrphanNodes)
            {
                result.Warnings.Add(new ValidationWarning
                {
                    SpecId = orphan,
                    Message = "존재하지 않는 parent를 참조합니다 (orphan node)."
                });
            }

            var data = new
            {
                isValid = result.IsValid,
                errorCount = result.Errors.Count,
                warningCount = result.Warnings.Count,
                errors = result.Errors,
                warnings = result.Warnings,
                cycleNodes = graph.CycleNodes,
                orphanNodes = graph.OrphanNodes
            };

            if (result.IsValid)
                JsonOutput.Write(JsonOutput.Success("spec-validate", data, "모든 스펙이 유효합니다."), pretty);
            else
            {
                JsonOutput.Write(JsonOutput.Error("spec-validate",
                    $"{result.Errors.Count}개의 에러가 발견되었습니다.", data), pretty);
                if (strict || checkTests) Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-validate", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    // ─── flow spec graph ──────────────────────────────────────────────
    [Command("spec-graph", Description = "스펙 그래프를 생성합니다")]
    public void SpecGraph(
        [Option("output", Description = "JSON 출력 파일 경로")] string? output = null,
        [Option("tree", Description = "트리 텍스트로 출력")] bool tree = false,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var specs = SpecStore.GetAll();
            var graph = GraphBuilder.Build(specs);

            if (tree)
            {
                var treeText = GraphBuilder.RenderTree(graph);
                Console.WriteLine(treeText);
                return;
            }

            if (!string.IsNullOrEmpty(output))
            {
                var dir = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(graph, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                File.WriteAllText(output, json);

                JsonOutput.Write(JsonOutput.Success("spec-graph",
                    new
                    {
                        outputPath = output,
                        nodeCount = graph.Nodes.Count,
                        rootCount = graph.Roots.Count,
                        cycleCount = graph.CycleNodes.Count
                    },
                    $"그래프가 '{output}'에 저장되었습니다."), pretty);
            }
            else
            {
                JsonOutput.Write(JsonOutput.Success("spec-graph",
                    new
                    {
                        nodeCount = graph.Nodes.Count,
                        roots = graph.Roots,
                        tree = graph.Tree,
                        dag = graph.Dag,
                        topologicalOrder = graph.TopologicalOrder,
                        cycleNodes = graph.CycleNodes,
                        orphanNodes = graph.OrphanNodes,
                        // F-021-C5: 대체/변형 관계 엣지
                        supersedesGraph = graph.SupersedesGraph,
                        mutatesGraph = graph.MutatesGraph
                    }), pretty);
            }
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-graph", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    // ─── flow spec impact ─────────────────────────────────────────────
    [Command("spec-impact", Description = "스펙 변경의 영향 범위를 분석합니다")]
    public void SpecImpact(
        [Argument(Description = "변경된 스펙 ID")] string id,
        [Option("depth", Description = "최대 전파 depth")] int depth = 10,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var specs = SpecStore.GetAll();
            var graph = GraphBuilder.Build(specs);
            var impact = ImpactAnalyzer.Analyze(graph, id, depth);

            JsonOutput.Write(JsonOutput.Success("spec-impact",
                new
                {
                    source = id,
                    maxDepth = depth,
                    impactedCount = impact.ImpactedNodes.Count,
                    impactedNodes = impact.ImpactedNodes
                },
                $"'{id}' 변경 시 {impact.ImpactedNodes.Count}개 스펙이 영향받습니다."), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-impact", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    // ─── flow spec propagate ──────────────────────────────────────────
    [Command("spec-propagate", Description = "상태 변경을 전파합니다")]
    public void SpecPropagate(
        [Argument(Description = "변경된 스펙 ID")] string id,
        [Option("status", Description = "새 상태")] string status = "needs-review",
        [Option("apply", Description = "실제로 적용 (기본: dry-run)")] bool apply = false,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var specs = SpecStore.GetAll();
            var graph = GraphBuilder.Build(specs);
            var changes = StatusPropagator.Propagate(graph, id, status);

            if (apply)
            {
                foreach (var (specId, _, newStatus) in changes)
                {
                    var spec = SpecStore.Get(specId);
                    if (spec != null)
                    {
                        spec.Status = newStatus;
                        SpecStore.Update(spec);
                    }
                }
            }

            JsonOutput.Write(JsonOutput.Success("spec-propagate",
                new
                {
                    source = id,
                    newStatus = status,
                    applied = apply,
                    changeCount = changes.Count,
                    changes = changes.Select(c => new { id = c.Id, from = c.OldStatus, to = c.NewStatus })
                },
                apply
                    ? $"{changes.Count}개 스펙의 상태가 변경되었습니다."
                    : $"{changes.Count}개 스펙의 상태 변경이 예상됩니다 (dry-run)."), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-propagate", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    // ─── flow spec check-refs ─────────────────────────────────────────
    [Command("spec-check-refs", Description = "코드 참조(codeRefs)를 검증합니다")]
    public void SpecCheckRefs(
        [Option("strict", Description = "엄격 모드 (유효하지 않은 참조 시 exit 1)")] bool strict = false,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var specs = SpecStore.GetAll();
            var result = CodeRefValidator.CheckAll(specs);

            var data = new
            {
                totalRefs = result.TotalRefs,
                validRefs = result.ValidRefs,
                invalidRefs = result.InvalidRefs,
                healthPercent = result.HealthPercent,
                invalidItems = result.InvalidItems
            };

            if (result.InvalidRefs == 0)
                JsonOutput.Write(JsonOutput.Success("spec-check-refs", data,
                    $"모든 코드 참조가 유효합니다 ({result.TotalRefs}개)."), pretty);
            else
            {
                JsonOutput.Write(JsonOutput.Error("spec-check-refs",
                    $"{result.InvalidRefs}개의 유효하지 않은 참조가 있습니다.", data), pretty);
                if (strict) Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-check-refs", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    // ─── flow spec-order ─────────────────────────────────────────────
    [Command("spec-order", Description = "의존성 그래프 기반으로 스펙 구현 순서를 결정합니다")]
    public void SpecOrder(
        [Option("from", Description = "이 스펙 기준 부분 순서 산출")] string? from = null,
        [Option("ai", Description = "LLM으로 최적화된 순서 제안")] bool ai = false,
        [Option("model", Description = "AI 호출 모델 (--ai 전용)")] string? model = null,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var specs = SpecStore.GetAll();
            var orderer = new SpecOrderer();
            var baseOrder = orderer.ComputeOrder(specs, from);

            if (baseOrder.HasCycles)
            {
                JsonOutput.Write(JsonOutput.Error("spec-order",
                    "순환 참조가 감지되었습니다. 순서를 계산할 수 없습니다.",
                    new { cycleNodes = baseOrder.CycleNodes }), pretty);
                Environment.ExitCode = 1;
                return;
            }

            // ── AI 최적화 모드 ────────────────────────────────────────
            if (ai)
            {
                var aiResult = TryRunAiOrder(orderer, baseOrder, model, pretty);
                if (aiResult != null) return;
                // AI 실패 시 기본 순서로 폴백 (콘솔 경고는 TryRunAiOrder 내에서 출력)
            }

            // ── 기본 위상정렬 결과 출력 ───────────────────────────────
            var msg = from != null
                ? $"[{from}] 기준 {baseOrder.TotalSpecs}개 스펙의 구현 순서 (Phase {baseOrder.Phases.Count}단계)"
                : $"{baseOrder.TotalSpecs}개 스펙의 구현 순서 (Phase {baseOrder.Phases.Count}단계)";

            JsonOutput.Write(JsonOutput.Success("spec-order", new
            {
                from,
                totalSpecs = baseOrder.TotalSpecs,
                phaseCount = baseOrder.Phases.Count,
                phases = baseOrder.Phases,
                aiOptimized = false
            }, msg), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-order", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    /// <summary>
    /// AI 최적화 순서를 시도한다.
    /// 성공 시 결과를 출력하고 true 반환. 실패 시 경고 출력 후 null 반환.
    /// </summary>
    private bool? TryRunAiOrder(SpecOrderer orderer, SpecOrderResult baseOrder, string? model, bool pretty)
    {
        try
        {
            var config = FlowConfigService.Load();
            var effectiveModel = model ?? config.CopilotModel;
            var copilotCmd = !string.IsNullOrEmpty(config.CopilotCliPath)
                ? config.CopilotCliPath
                : config.CopilotCommand;

            var prompt = orderer.BuildAiPrompt(baseOrder);

            // 프롬프트를 임시 파일에 저장 (특수문자 안전 처리)
            var tempFile = Path.Combine(Path.GetTempPath(),
                $".flow-spec-order-{Guid.NewGuid():N}.txt");
            File.WriteAllText(tempFile, prompt, System.Text.Encoding.UTF8);

            // PowerShell을 통해 실행해서 인수 이스케이프 문제 방지
            var psScript = $"""
$prompt = Get-Content '{tempFile.Replace("'", "''")}' -Raw
Remove-Item '{tempFile.Replace("'", "''")}' -Force -ErrorAction SilentlyContinue
& {copilotCmd} -p $prompt --model {effectiveModel} --yolo 2>&1
""";

            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "pwsh",
                    Arguments = $"-NoProfile -NonInteractive -Command \"{psScript.Replace("\"", "\\\"")}\"",
                    WorkingDirectory = PathResolver.ProjectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var output = new System.Text.StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            var timeoutMs = config.CopilotTimeoutMinutes * 60 * 1000;
            process.WaitForExit(timeoutMs > 0 ? timeoutMs : 120_000);

            // 출력에서 JSON 블록 추출
            var rawOutput = output.ToString();
            var aiSuggestion = TryParseAiSuggestion(rawOutput);

            if (aiSuggestion == null)
            {
                Console.Error.WriteLine("[spec-order] AI 응답 파싱 실패 - 기본 순서로 폴백합니다.");
                return null;
            }

            // ── 의존성 제약 위반 검증 (C4) ──────────────────────────
            var violations = orderer.ValidateDependencyConstraints(baseOrder, aiSuggestion.Phases);

            if (violations.Count > 0)
            {
                // 위반 발견: 해당 제안 자동 거부
                JsonOutput.Write(JsonOutput.Error("spec-order",
                    $"AI 제안에 {violations.Count}개의 의존성 제약 위반이 감지되어 자동 거부합니다. 기본 순서를 사용합니다.",
                    new
                    {
                        from = baseOrder.FromId,
                        totalSpecs = baseOrder.TotalSpecs,
                        phaseCount = baseOrder.Phases.Count,
                        phases = baseOrder.Phases,
                        aiOptimized = false,
                        aiRejected = true,
                        violations
                    }), pretty);
                return true;
            }

            // 위반 없음: AI 제안 채택
            var totalPhases = aiSuggestion.Phases.Count;
            JsonOutput.Write(JsonOutput.Success("spec-order", new
            {
                from = baseOrder.FromId,
                totalSpecs = baseOrder.TotalSpecs,
                phaseCount = totalPhases,
                phases = aiSuggestion.Phases,
                aiOptimized = true,
                reasoning = aiSuggestion.Reasoning
            }, $"AI 최적화 순서 적용 완료 (Phase {totalPhases}단계). {aiSuggestion.Reasoning}"), pretty);

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[spec-order --ai] 오류: {ex.Message} - 기본 순서로 폴백합니다.");
            return null;
        }
    }

    /// <summary>
    /// AI 출력 텍스트에서 JSON 블록을 파싱합니다.
    /// </summary>
    private static AiOrderSuggestion? TryParseAiSuggestion(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
            return null;

        // JSON 블록 추출: 첫 번째 '{' ~ 마지막 '}'
        var start = rawOutput.IndexOf('{');
        var end = rawOutput.LastIndexOf('}');

        if (start < 0 || end <= start)
            return null;

        var jsonBlock = rawOutput[start..(end + 1)];

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<AiOrderSuggestion>(jsonBlock,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    // ─── flow spec backup ─────────────────────────────────────────────
    [Command("spec-backup", Description = "스펙 파일을 백업합니다")]
    public void SpecBackup(
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var backupPath = SpecStore.Backup();
            JsonOutput.Write(JsonOutput.Success("spec-backup",
                new { path = backupPath },
                $"백업이 완료되었습니다: {backupPath}"), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-backup", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    // ─── flow spec restore ────────────────────────────────────────────
    [Command("spec-restore", Description = "백업에서 스펙을 복구합니다")]
    public void SpecRestore(
        [Argument(Description = "백업 timestamp")] string timestamp,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var count = SpecStore.Restore(timestamp);
            JsonOutput.Write(JsonOutput.Success("spec-restore",
                new { timestamp, restoredCount = count },
                $"{count}개의 스펙이 복구되었습니다."), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-restore", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    // ─── flow spec-push ──────────────────────────────────────────────
    [Command("spec-push", Description = "스펙 변경사항을 원격 저장소에 push합니다")]
    public async Task SpecPushAsync(
        [Option('m', Description = "커밋 메시지")] string? message = null,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var gitService = new SpecGitService();
            var result = await gitService.PushAsync(SpecStore.SpecsDir, message);

            if (result.AlreadyUpToDate)
            {
                JsonOutput.Write(JsonOutput.Success("spec-push",
                    new { status = "up-to-date" },
                    "Already up to date."), pretty);
            }
            else
            {
                JsonOutput.Write(JsonOutput.Success("spec-push",
                    new { status = "pushed", commit = result.CommitHash, message = result.CommitMessage },
                    $"push 완료: {result.CommitHash} \"{result.CommitMessage}\""), pretty);
            }
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-push", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    // ─── flow spec-index ─────────────────────────────────────────────
    [Command("spec-index", Description = "스펙 내용을 RAG 벡터 DB에 인덱싱합니다")]
    public void SpecIndex(
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var specs = SpecStore.GetAll();
            int inserted = 0, updated = 0;

            foreach (var spec in specs)
            {
                // Build indexable text from spec fields
                var conditionText = spec.Conditions.Count > 0
                    ? "\n조건:\n" + string.Join("\n", spec.Conditions.Select(c => $"- {c.Description}"))
                    : "";
                var tagText = spec.Tags.Count > 0
                    ? "\n태그: " + string.Join(", ", spec.Tags)
                    : "";
                var content = $"[{spec.Id}] {spec.Title}\n{spec.Description}{conditionText}{tagText}\n상태: {spec.Status}";

                var (_, wasInserted) = DatabaseService.UpsertSpecDocument(spec.Id, content);
                if (wasInserted) inserted++;
                else updated++;
            }

            JsonOutput.Write(JsonOutput.Success("spec-index", new
            {
                total = specs.Count,
                inserted,
                updated
            }, $"스펙 인덱싱 완료: {inserted}개 추가, {updated}개 업데이트"), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-index", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
