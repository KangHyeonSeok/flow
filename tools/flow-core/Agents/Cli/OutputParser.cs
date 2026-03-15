using System.Text.Json;
using FlowCore.Backend;
using FlowCore.Models;
using FlowCore.Serialization;

namespace FlowCore.Agents.Cli;

/// <summary>CliResponse → AgentOutput 변환 (JSON 블록 추출)</summary>
public sealed class OutputParser
{
    /// <summary>
    /// 응답을 파싱하여 AgentOutput을 반환.
    /// 실패 시 null (호출자가 RetryableFailure 처리).
    /// </summary>
    public AgentOutput? Parse(CliResponse response, AgentInput input)
    {
        if (!response.Success)
        {
            // timeout이든 exit code 에러든 재시도 가능하게 처리.
            // TerminalFailure는 파싱 후 도메인 로직에서만 결정한다.
            return new AgentOutput
            {
                Result = AgentResult.RetryableFailure,
                BaseVersion = input.CurrentVersion,
                Message = response.ErrorMessage ?? "backend failure"
            };
        }

        var json = ExtractJsonBlock(response.ResponseText);
        if (json == null)
            return null; // 호출자가 RetryableFailure 처리

        try
        {
            var dto = JsonSerializer.Deserialize<OutputDto>(json, FlowJsonOptions.Default);
            if (dto == null)
                return null;

            if (!Enum.TryParse<FlowEvent>(dto.ProposedEvent, ignoreCase: true, out var flowEvent))
                return null;

            // specValidationUserReviewRequested는 proposedReviewRequest 필수
            if (flowEvent == FlowEvent.SpecValidationUserReviewRequested
                && dto.ProposedReviewRequest == null)
                return null;

            ProposedReviewRequest? prr = null;
            if (dto.ProposedReviewRequest is { } prrDto)
            {
                prr = new ProposedReviewRequest
                {
                    Summary = prrDto.Summary,
                    Questions = prrDto.Questions,
                    Options = prrDto.Options?.Select(o => new ReviewRequestOption
                    {
                        Id = o.Id ?? string.Empty,
                        Label = o.Label ?? string.Empty,
                        Description = o.Description
                    }).ToList()
                };
            }

            IReadOnlyList<EvidenceRef>? evidenceRefs = null;
            if (dto.EvidenceRefs is { Count: > 0 } erDtos)
            {
                var validRefs = erDtos
                    .Where(e => IsValidRelativePath(e.RelativePath))
                    .Select(e => new EvidenceRef
                    {
                        Kind = e.Kind ?? "unknown",
                        RelativePath = e.RelativePath!,
                        Summary = e.Summary
                    }).ToList();
                if (validRefs.Count > 0)
                    evidenceRefs = validRefs;
            }

            // ProposedSpec 파싱 (Planner 전용)
            ProposedSpecDraft? proposedSpec = null;
            if (dto.ProposedSpec is { } psDto)
            {
                IReadOnlyList<AcceptanceCriterionDraft>? acDrafts = null;
                if (psDto.AcceptanceCriteria is { Count: > 0 } acDtos)
                {
                    acDrafts = acDtos
                        .Where(a => !string.IsNullOrWhiteSpace(a.Text))
                        .Select(a => new AcceptanceCriterionDraft
                        {
                            Text = a.Text!,
                            Testable = a.Testable ?? true,
                            Notes = a.Notes
                        }).ToList();
                }

                proposedSpec = new ProposedSpecDraft
                {
                    Title = psDto.Title,
                    Type = TryParseEnum<SpecType>(psDto.Type),
                    Problem = psDto.Problem,
                    Goal = psDto.Goal,
                    AcceptanceCriteria = acDrafts,
                    RiskLevel = TryParseEnum<RiskLevel>(psDto.RiskLevel),
                    DependsOn = psDto.DependsOn
                };
            }

            // DraftUpdated/DraftCreated requires ProposedSpec for Planner
            if ((flowEvent == FlowEvent.DraftUpdated || flowEvent == FlowEvent.DraftCreated)
                && input.Assignment.Type == AssignmentType.Planning
                && proposedSpec == null)
                return null;

            return new AgentOutput
            {
                Result = AgentResult.Success,
                BaseVersion = input.CurrentVersion, // LLM 결과 무시, 자동 설정
                ProposedEvent = flowEvent,
                Summary = dto.Summary,
                ProposedReviewRequest = prr,
                EvidenceRefs = evidenceRefs,
                ProposedSpec = proposedSpec
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>응답 텍스트에서 JSON 블록 추출</summary>
    public static string? ExtractJsonBlock(string text)
    {
        // 1차: ```json ... ``` 패턴
        var jsonStart = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (jsonStart >= 0)
        {
            var contentStart = text.IndexOf('\n', jsonStart);
            if (contentStart >= 0)
            {
                var jsonEnd = text.IndexOf("```", contentStart + 1, StringComparison.Ordinal);
                if (jsonEnd > contentStart)
                    return text[(contentStart + 1)..jsonEnd].Trim();
            }
        }

        // 2차: 첫 { ~ 마지막 } 매칭
        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            return text[firstBrace..(lastBrace + 1)];

        return null;
    }

    /// <summary>evidence RelativePath 유효성 검증: 빈 값, 절대 경로, path traversal 차단</summary>
    private static bool IsValidRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // 절대 경로 차단 (Unix: /..., Windows: C:\... or \\...)
        if (path.StartsWith('/') || path.StartsWith('\\'))
            return false;
        if (path.Length >= 2 && path[1] == ':')
            return false;

        // path traversal 차단
        var segments = path.Split('/', '\\');
        if (segments.Any(s => s == ".."))
            return false;

        return true;
    }

    private static T? TryParseEnum<T>(string? value) where T : struct, Enum
        => Enum.TryParse<T>(value, ignoreCase: true, out var result) ? result : null;

    // ── 파싱용 내부 DTO ──

    private sealed class OutputDto
    {
        public string? ProposedEvent { get; set; }
        public string? Summary { get; set; }
        public ProposedReviewRequestDto? ProposedReviewRequest { get; set; }
        public List<EvidenceRefDto>? EvidenceRefs { get; set; }
        public ProposedSpecDto? ProposedSpec { get; set; }
    }

    private sealed class EvidenceRefDto
    {
        public string? Kind { get; set; }
        public string? RelativePath { get; set; }
        public string? Summary { get; set; }
    }

    private sealed class ProposedReviewRequestDto
    {
        public string? Summary { get; set; }
        public List<string>? Questions { get; set; }
        public List<ReviewRequestOptionDto>? Options { get; set; }
    }

    private sealed class ReviewRequestOptionDto
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
        public string? Description { get; set; }
    }

    private sealed class ProposedSpecDto
    {
        public string? Title { get; set; }
        public string? Type { get; set; }
        public string? Problem { get; set; }
        public string? Goal { get; set; }
        public List<AcceptanceCriterionDraftDto>? AcceptanceCriteria { get; set; }
        public string? RiskLevel { get; set; }
        public List<string>? DependsOn { get; set; }
    }

    private sealed class AcceptanceCriterionDraftDto
    {
        public string? Text { get; set; }
        public bool? Testable { get; set; }
        public string? Notes { get; set; }
    }
}
