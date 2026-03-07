using Cocona;
using FlowCLI.Services.TestSync;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    private TestSyncService? _testSyncService;
    private TestSyncService TestSyncService => _testSyncService ??= new TestSyncService(SpecStore);

    // ─── spec-test-sync ──────────────────────────────────────────────────────

    /// <summary>
    /// 테스트 결과 파일을 파싱하여 스펙 조건에 매핑하고 동기화한다 (F-014-C1).
    /// 어노테이션 기반 매핑: [Trait("Spec","F-014-C1")] / @pytest.mark.spec("F-014-C1") / [spec:F-014-C1]
    /// </summary>
    [Command("spec-test-sync", Description = "테스트 결과를 스펙 조건에 동기화합니다 (xUnit/Jest/pytest)")]
    public void SpecTestSync(
        [Option("from", Description = "테스트 결과 파일 경로 (JSON/TRX/XML)")] string from = "",
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(from))
            {
                JsonOutput.Write(JsonOutput.ValidationError("spec-test-sync",
                    "--from 옵션은 필수입니다. 예: spec-test-sync --from results.json"), pretty);
                Environment.ExitCode = 1;
                return;
            }

            var filePath = Path.GetFullPath(from);
            if (!File.Exists(filePath))
            {
                JsonOutput.Write(JsonOutput.Error("spec-test-sync",
                    $"테스트 결과 파일을 찾을 수 없습니다: {from}",
                    new { path = filePath }, ErrorCodes.NotFound), pretty);
                Environment.ExitCode = 1;
                return;
            }

            var result = TestSyncService.Sync(filePath);

            var data = new
            {
                totalTests = result.TotalTests,
                mappedTests = result.MappedTests,
                unmappedTests = result.UnmappedTests,
                updatedSpecs = result.UpdatedSpecs,
                quarantinedTests = result.QuarantinedTests,
                mappings = result.Mappings,
                warnings = result.Warnings
            };

            if (result.MappedTests > 0)
            {
                JsonOutput.Write(JsonOutput.Success("spec-test-sync", data,
                    $"{result.MappedTests}개 테스트가 {result.UpdatedSpecs}개 스펙에 동기화되었습니다."), pretty);
            }
            else
            {
                JsonOutput.Write(JsonOutput.Error("spec-test-sync",
                    "어노테이션이 있는 테스트가 없습니다. [Trait(\"Spec\",\"F-001-C1\")] 또는 @pytest.mark.spec(\"F-001-C1\") 어노테이션을 추가하세요.",
                    data), pretty);
                Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-test-sync", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    // ─── spec-test-report ────────────────────────────────────────────────────

    /// <summary>
    /// 동기화된 테스트 데이터를 기반으로 스펙별 건강도 리포트를 출력한다 (F-014-C2).
    /// </summary>
    [Command("spec-test-report", Description = "스펙별 테스트 건강도 리포트를 출력합니다")]
    public void SpecTestReport(
        [Option("table", Description = "텍스트 테이블로 출력")] bool table = false,
        [Option("pretty", Description = "Pretty print JSON")] bool pretty = false)
    {
        try
        {
            var report = TestSyncService.GenerateReport();

            if (table)
            {
                Console.Write(TestSyncService.RenderReportTable(report));
                return;
            }

            JsonOutput.Write(JsonOutput.Success("spec-test-report", report,
                $"{report.TotalSpecs}개 스펙의 건강도 리포트 (Healthy:{report.HealthySpecs} Failed:{report.FailedSpecs} Unresolved:{report.UnresolvedSpecs})"),
                pretty);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("spec-test-report", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }
}
