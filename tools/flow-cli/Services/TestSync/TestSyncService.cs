using FlowCLI.Services.SpecGraph;

namespace FlowCLI.Services.TestSync;

/// <summary>
/// 테스트 결과를 스펙 JSON과 동기화하고 건강도를 계산한다 (F-014).
/// </summary>
public class TestSyncService
{
    private const int MaxFlakyHistory = 10;
    private const int QuarantineConsecutiveFlaky = 5;
    private const double QuarantineFlakyScoreThreshold = 0.1;
    private const double FlakyPenaltyFactor = 0.5;

    private readonly SpecStore _specStore;

    public TestSyncService(SpecStore specStore)
    {
        _specStore = specStore;
    }

    // ─── C1: spec-test-sync ──────────────────────────────────────────────────

    /// <summary>
    /// 테스트 결과 파일을 파싱하여 스펙 조건에 매핑하고 저장한다 (F-014-C1).
    /// 어노테이션: [Trait("Spec","F-014-C1")] / @pytest.mark.spec("F-014-C1") / [spec:F-014-C1]
    /// </summary>
    public TestSyncResult Sync(string testResultFilePath)
    {
        var run = TestResultParser.Parse(testResultFilePath);
        return SyncFromRun(run);
    }

    /// <summary>
    /// 파싱된 TestRunResult를 스펙에 매핑하고 저장한다.
    /// </summary>
    public TestSyncResult SyncFromRun(TestRunResult run)
    {
        var syncResult = new TestSyncResult
        {
            TotalTests = run.Tests.Count
        };

        var specs = _specStore.GetAll();
        // Build lookup: conditionId → (specId, conditionIndex)
        var conditionIndex = BuildConditionIndex(specs);

        var modifiedSpecs = new HashSet<string>();
        var runAt = run.RunAt ?? DateTime.UtcNow.ToString("o");

        foreach (var tc in run.Tests)
        {
            var condId = TestResultParser.ExtractSpecConditionId(tc);

            if (string.IsNullOrEmpty(condId))
            {
                syncResult.UnmappedTests++;
                syncResult.Warnings.Add($"테스트 '{tc.Name}' 에 스펙 어노테이션이 없습니다.");
                continue;
            }

            if (!conditionIndex.TryGetValue(condId, out var mapping))
            {
                syncResult.UnmappedTests++;
                syncResult.Warnings.Add($"테스트 '{tc.Name}' 의 어노테이션 '{condId}' 에 해당하는 조건이 없습니다.");
                continue;
            }

            var specId = mapping.specId;
            var condIdx = mapping.condIdx;
            var spec = specs.First(s => s.Id == specId);
            var condition = spec.Conditions[condIdx];

            // 동일 testId 가 있으면 업데이트, 없으면 추가
            var existing = condition.Tests.FirstOrDefault(t => t.TestId == tc.Id);
            if (existing == null)
            {
                existing = new TestLink { TestId = tc.Id };
                condition.Tests.Add(existing);
            }

            existing.Name = tc.Name;
            existing.Suite = tc.Suite;
            existing.Status = tc.Status;
            existing.DurationMs = tc.DurationMs;
            existing.ErrorMessage = tc.ErrorMessage;
            existing.RunAt = runAt;

            // flakyHistory 갱신 (최대 MaxFlakyHistory 개 유지)
            existing.FlakyHistory.Add(tc.Status);
            if (existing.FlakyHistory.Count > MaxFlakyHistory)
                existing.FlakyHistory.RemoveAt(0);

            // C4: quarantine 감지
            existing.Quarantined = ShouldQuarantine(existing);
            if (existing.Quarantined)
            {
                syncResult.QuarantinedTests++;
                syncResult.Warnings.Add(
                    $"테스트 '{tc.Name}' 가 연속 {QuarantineConsecutiveFlaky}회 flaky 감지 → quarantine 태그 부여. 별도 job 분리 권고.");
            }

            modifiedSpecs.Add(specId);
            syncResult.MappedTests++;
            syncResult.Mappings.Add(new TestMappingEntry
            {
                TestId = tc.Id,
                TestName = tc.Name,
                ConditionId = condId,
                SpecId = specId,
                Status = tc.Status,
                Quarantined = existing.Quarantined
            });
        }

        // 변경된 스펙 저장
        foreach (var specId in modifiedSpecs)
        {
            var spec = specs.First(s => s.Id == specId);
            _specStore.Update(spec);
        }

        syncResult.UpdatedSpecs = modifiedSpecs.Count;
        return syncResult;
    }

    // ─── C2: spec-test-report ────────────────────────────────────────────────

    /// <summary>
    /// 저장된 테스트 데이터를 기반으로 건강도 리포트를 생성한다 (F-014-C2).
    /// </summary>
    public TestHealthReport GenerateReport()
    {
        var specs = _specStore.GetAll();
        var report = new TestHealthReport
        {
            TotalSpecs = specs.Count
        };

        foreach (var spec in specs)
        {
            if (spec.Conditions.Count == 0) continue;

            var entry = BuildSpecHealthEntry(spec);
            report.Specs.Add(entry);

            if (entry.Unresolved > 0)
                report.UnresolvedSpecs++;
            else if (entry.Failed > 0)
                report.FailedSpecs++;
            else
                report.HealthySpecs++;
        }

        return report;
    }

    /// <summary>
    /// 동기화된 테스트 결과 파일을 스펙/조건 evidence에 연결한다.
    /// </summary>
    public int AppendEvidence(string artifactPath, TestSyncResult syncResult, string? platform = null, string? capturedAt = null)
    {
        if (syncResult.Mappings.Count == 0)
        {
            return 0;
        }

        var specs = _specStore.GetAll().ToDictionary(spec => spec.Id, StringComparer.OrdinalIgnoreCase);
        var groupedBySpec = syncResult.Mappings.GroupBy(mapping => mapping.SpecId, StringComparer.OrdinalIgnoreCase);
        var updatedCount = 0;
        var normalizedPath = NormalizeEvidencePath(artifactPath);
        var timestamp = capturedAt ?? DateTime.UtcNow.ToString("o");

        foreach (var specGroup in groupedBySpec)
        {
            if (!specs.TryGetValue(specGroup.Key, out var spec))
            {
                continue;
            }

            spec.Evidence.RemoveAll(evidence => evidence.Type == "test-result" && evidence.Path == normalizedPath);
            spec.Evidence.Add(new SpecEvidence
            {
                Type = "test-result",
                Path = normalizedPath,
                CapturedAt = timestamp,
                Platform = platform,
                Summary = BuildEvidenceSummary(specGroup)
            });

            foreach (var conditionGroup in specGroup.GroupBy(mapping => mapping.ConditionId, StringComparer.OrdinalIgnoreCase))
            {
                var condition = spec.Conditions.FirstOrDefault(c => string.Equals(c.Id, conditionGroup.Key, StringComparison.OrdinalIgnoreCase));
                if (condition == null)
                {
                    continue;
                }

                condition.Evidence.RemoveAll(evidence => evidence.Type == "test-result" && evidence.Path == normalizedPath);
                condition.Evidence.Add(new SpecEvidence
                {
                    Type = "test-result",
                    Path = normalizedPath,
                    CapturedAt = timestamp,
                    Platform = platform,
                    Summary = BuildEvidenceSummary(conditionGroup)
                });
            }

            _specStore.Update(spec);
            updatedCount++;
        }

        return updatedCount;
    }

    /// <summary>
    /// 텍스트 테이블 형태로 건강도 리포트를 렌더링한다 (F-014-C2).
    /// </summary>
    public static string RenderReportTable(TestHealthReport report)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("┌──────────┬─────────────────────────────────────────┬────────┬────────┬────────────┐");
        sb.AppendLine("│ Spec     │ Title                                   │ Health │ Failed │ Unresolved │");
        sb.AppendLine("├──────────┼─────────────────────────────────────────┼────────┼────────┼────────────┤");

        foreach (var e in report.Specs.OrderByDescending(s => s.HealthScore))
        {
            var title = e.Title.Length > 39 ? e.Title[..36] + "..." : e.Title.PadRight(39);
            var health = $"{e.HealthScore:F2}".PadLeft(6);
            var failed = e.Failed.ToString().PadLeft(6);
            var unresolved = e.Unresolved.ToString().PadLeft(10);
            sb.AppendLine($"│ {e.SpecId,-8} │ {title} │{health} │{failed} │{unresolved} │");
        }

        sb.AppendLine("└──────────┴─────────────────────────────────────────┴────────┴────────┴────────────┘");
        sb.AppendLine($"  Specs: {report.TotalSpecs}  Healthy: {report.HealthySpecs}  Failed: {report.FailedSpecs}  Unresolved: {report.UnresolvedSpecs}");
        return sb.ToString();
    }

    // ─── C3: healthScore ─────────────────────────────────────────────────────

    /// <summary>
    /// healthScore = (passed - flakyPenalty) / total, 범위: 0.0~1.0 (F-014-C3).
    /// flakyPenalty = flaky * 0.5
    /// </summary>
    public static double ComputeHealthScore(int passed, int flaky, int total)
    {
        if (total <= 0) return 0.0;
        var flakyPenalty = flaky * FlakyPenaltyFactor;
        var adjusted = passed - flakyPenalty;
        return Math.Round(Math.Max(0.0, Math.Min(1.0, adjusted / total)), 4);
    }

    /// <summary>
    /// flakyScore = flaky / total
    /// </summary>
    public static double ComputeFlakyScore(int flaky, int total)
    {
        if (total <= 0) return 0.0;
        return Math.Round((double)flaky / total, 4);
    }

    // ─── C4: quarantine ──────────────────────────────────────────────────────

    /// <summary>
    /// flakyScore >= 0.1 이고 연속 5회 flaky 이면 quarantine (F-014-C4).
    /// </summary>
    public static bool ShouldQuarantine(TestLink test)
    {
        var history = test.FlakyHistory;
        if (history.Count < QuarantineConsecutiveFlaky) return false;

        // 연속 마지막 5회가 모두 flaky 인지 확인
        var last5 = history.TakeLast(QuarantineConsecutiveFlaky);
        if (!last5.All(s => s == "flaky")) return false;

        // flakyScore >= 0.1 인지 확인
        var total = history.Count;
        var flaky = history.Count(s => s == "flaky");
        var flakyScore = ComputeFlakyScore(flaky, total);

        return flakyScore >= QuarantineFlakyScoreThreshold;
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private string NormalizeEvidencePath(string artifactPath)
    {
        var docsDir = Path.GetDirectoryName(_specStore.EvidenceDir) ?? _specStore.EvidenceDir;
        var projectRoot = Path.GetDirectoryName(docsDir) ?? docsDir;
        return Path.GetRelativePath(projectRoot, artifactPath).Replace('\\', '/');
    }

    private static string BuildEvidenceSummary(IEnumerable<TestMappingEntry> mappings)
    {
        var passed = 0;
        var failed = 0;
        var skipped = 0;
        var flaky = 0;

        foreach (var mapping in mappings)
        {
            switch (mapping.Status)
            {
                case "passed":
                    passed++;
                    break;
                case "failed":
                    failed++;
                    break;
                case "skipped":
                    skipped++;
                    break;
                case "flaky":
                    flaky++;
                    break;
            }
        }

        return $"Runner automated tests: passed={passed}, failed={failed}, skipped={skipped}, flaky={flaky}";
    }

    private static Dictionary<string, (string specId, int condIdx)> BuildConditionIndex(List<SpecNode> specs)
    {
        var index = new Dictionary<string, (string specId, int condIdx)>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specs)
        {
            for (var i = 0; i < spec.Conditions.Count; i++)
            {
                var cond = spec.Conditions[i];
                if (!string.IsNullOrEmpty(cond.Id))
                    index[cond.Id] = (spec.Id, i);
            }
        }
        return index;
    }

    private static SpecHealthEntry BuildSpecHealthEntry(SpecNode spec)
    {
        var entry = new SpecHealthEntry
        {
            SpecId = spec.Id,
            Title = spec.Title
        };

        int totalPassed = 0, totalFailed = 0, totalFlaky = 0, totalTests = 0, unresolved = 0;

        foreach (var cond in spec.Conditions)
        {
            var ce = BuildConditionHealthEntry(cond);
            entry.Conditions.Add(ce);

            totalPassed += cond.Tests.Count(t => t.Status == "passed");
            totalFailed += cond.Tests.Count(t => t.Status == "failed");
            totalFlaky += cond.Tests.Count(t => t.Status == "flaky");
            totalTests += cond.Tests.Count;

            if (cond.Tests.Count == 0)
                unresolved++;
        }

        entry.Total = totalTests;
        entry.Passed = totalPassed;
        entry.Failed = totalFailed;
        entry.Flaky = totalFlaky;
        entry.Unresolved = unresolved;
        entry.HealthScore = ComputeHealthScore(totalPassed, totalFlaky, totalTests);
        entry.Trend = ComputeTrend(spec);

        return entry;
    }

    private static ConditionHealthEntry BuildConditionHealthEntry(SpecCondition cond)
    {
        var tests = cond.Tests;
        var total = tests.Count;
        var passed = tests.Count(t => t.Status == "passed");
        var failed = tests.Count(t => t.Status == "failed");
        var flaky = tests.Count(t => t.Status == "flaky");
        var quarantined = tests.Any(t => t.Quarantined);

        var healthScore = ComputeHealthScore(passed, flaky, total);
        var flakyScore = ComputeFlakyScore(flaky, total);

        var status = total == 0 ? "unresolved"
            : quarantined ? "quarantined"
            : failed > 0 ? "failed"
            : flakyScore > 0 ? "flaky"
            : "healthy";

        return new ConditionHealthEntry
        {
            ConditionId = cond.Id,
            Description = cond.Description,
            HealthScore = healthScore,
            FlakyScore = flakyScore,
            Quarantined = quarantined,
            TestCount = total,
            Status = status
        };
    }

    private static string ComputeTrend(SpecNode spec)
    {
        // trend는 각 테스트의 flakyHistory 마지막 2회를 비교하여 판단
        var allHistories = spec.Conditions.SelectMany(c => c.Tests.Select(t => t.FlakyHistory)).ToList();
        if (!allHistories.Any()) return "stable";

        int improvements = 0, degradations = 0;
        foreach (var h in allHistories)
        {
            if (h.Count < 2) continue;
            var prev = h[^2];
            var curr = h[^1];
            if ((prev == "failed" || prev == "flaky") && curr == "passed") improvements++;
            else if (prev == "passed" && (curr == "failed" || curr == "flaky")) degradations++;
        }

        if (improvements > degradations) return "up";
        if (degradations > improvements) return "down";
        return "stable";
    }
}
