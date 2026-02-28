using System.Text.Json;
using Cocona;
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
        [Option("status", Description = "상태 (draft|active|needs-review|verified|deprecated)")] string status = "draft",
        [Option("tags", Description = "태그 (콤마 구분)")] string? tags = null,
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
                Tags = tags?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList() ?? new()
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
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var spec = SpecStore.Get(id);
            if (spec == null)
            {
                JsonOutput.Write(JsonOutput.Error("spec-get", $"스펙 '{id}'을(를) 찾을 수 없습니다."), pretty);
                Environment.ExitCode = 1;
                return;
            }
            JsonOutput.Write(JsonOutput.Success("spec-get", spec), pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-get", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
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

    // ─── flow spec validate ───────────────────────────────────────────
    [Command("spec-validate", Description = "스펙 유효성을 검사합니다")]
    public void SpecValidate(
        [Option("strict", Description = "엄격 모드 (수락 조건 3개+, cycle 시 exit 1)")] bool strict = false,
        [Option("id", Description = "특정 스펙만 검증")] string? id = null,
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
                result = SpecValidator.ValidateSpec(spec, strict);
            }
            else
            {
                result = SpecValidator.ValidateAll(specs, strict);
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
                if (strict) Environment.ExitCode = 1;
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
                        orphanNodes = graph.OrphanNodes
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
}
