using System.Text.Json;
using FlowCore.Agents.Cli;
using FlowCore.Models;
using FlowCore.Serialization;

namespace FlowCore.Planning;

/// <summary>경로 B 전용: 사용자 요청 분해 응답 파싱</summary>
public sealed class PlannerOutputParser
{
    /// <summary>LLM 응답 텍스트를 파싱하여 PlanParseResult를 반환. 실패 시 null.</summary>
    public PlanParseResult? Parse(string responseText)
    {
        var json = OutputParser.ExtractJsonBlock(responseText);
        if (json == null) return null;

        try
        {
            var dto = JsonSerializer.Deserialize<PlanResponseDto>(json, FlowJsonOptions.Default);
            if (dto?.Specs == null || dto.Specs.Count == 0)
                return null;

            var specs = new List<SpecDraft>();
            foreach (var specDto in dto.Specs)
            {
                if (string.IsNullOrWhiteSpace(specDto.Title))
                    continue;

                var acDrafts = specDto.AcceptanceCriteria?
                    .Where(a => !string.IsNullOrWhiteSpace(a.Text))
                    .Select(a => new AcceptanceCriterionDraft
                    {
                        Text = a.Text!,
                        Testable = a.Testable ?? true,
                        Notes = a.Notes
                    }).ToList() ?? [];

                var internalDeps = specDto.InternalDependsOn?
                    .Where(i => i >= 0) // negative indices are invalid
                    .ToList() ?? [];

                specs.Add(new SpecDraft
                {
                    Title = specDto.Title!,
                    Type = TryParseEnum<SpecType>(specDto.Type) ?? SpecType.Task,
                    Problem = specDto.Problem,
                    Goal = specDto.Goal,
                    AcceptanceCriteria = acDrafts,
                    RiskLevel = TryParseEnum<RiskLevel>(specDto.RiskLevel) ?? RiskLevel.Low,
                    DependsOn = specDto.DependsOn ?? [],
                    InternalDependsOn = internalDeps
                });
            }

            if (specs.Count == 0) return null;

            return new PlanParseResult
            {
                Specs = specs,
                Summary = dto.Summary
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static T? TryParseEnum<T>(string? value) where T : struct, Enum
        => Enum.TryParse<T>(value, ignoreCase: true, out var result) ? result : null;

    // ── 내부 DTO ──

    private sealed class PlanResponseDto
    {
        public List<SpecDraftDto>? Specs { get; set; }
        public string? Summary { get; set; }
    }

    private sealed class SpecDraftDto
    {
        public string? Title { get; set; }
        public string? Type { get; set; }
        public string? Problem { get; set; }
        public string? Goal { get; set; }
        public List<AcDraftDto>? AcceptanceCriteria { get; set; }
        public string? RiskLevel { get; set; }
        public List<string>? DependsOn { get; set; }
        public List<int>? InternalDependsOn { get; set; }
    }

    private sealed class AcDraftDto
    {
        public string? Text { get; set; }
        public bool? Testable { get; set; }
        public string? Notes { get; set; }
    }
}

/// <summary>경로 B 파싱 결과</summary>
public sealed class PlanParseResult
{
    public IReadOnlyList<SpecDraft> Specs { get; init; } = [];
    public string? Summary { get; init; }
}

/// <summary>경로 B 개별 spec draft (InternalDependsOn 포함)</summary>
public sealed class SpecDraft
{
    public required string Title { get; init; }
    public SpecType Type { get; init; } = SpecType.Task;
    public string? Problem { get; init; }
    public string? Goal { get; init; }
    public IReadOnlyList<AcceptanceCriterionDraft> AcceptanceCriteria { get; init; } = [];
    public RiskLevel RiskLevel { get; init; } = RiskLevel.Low;
    public IReadOnlyList<string> DependsOn { get; init; } = [];
    public IReadOnlyList<int> InternalDependsOn { get; init; } = [];
}
