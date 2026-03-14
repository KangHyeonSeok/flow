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
            var stopReason = response.StopReason;
            return new AgentOutput
            {
                Result = stopReason == CliStopReason.Timeout
                    ? AgentResult.RetryableFailure
                    : AgentResult.TerminalFailure,
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

            return new AgentOutput
            {
                Result = AgentResult.Success,
                BaseVersion = input.CurrentVersion, // LLM 결과 무시, 자동 설정
                ProposedEvent = flowEvent,
                Summary = dto.Summary,
                ProposedReviewRequest = prr,
                EvidenceRefs = evidenceRefs
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

    // ── 파싱용 내부 DTO ──

    private sealed class OutputDto
    {
        public string? ProposedEvent { get; set; }
        public string? Summary { get; set; }
        public ProposedReviewRequestDto? ProposedReviewRequest { get; set; }
        public List<EvidenceRefDto>? EvidenceRefs { get; set; }
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
}
