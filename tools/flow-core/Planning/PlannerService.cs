using FlowCore.Backend;
using FlowCore.Models;
using FlowCore.Storage;
using FlowCore.Utilities;

namespace FlowCore.Planning;

/// <summary>경로 B: 사용자 자연어 요청을 draft spec 여러 개로 분해하는 서비스</summary>
public sealed class PlannerService
{
    private readonly IFlowStore _store;
    private readonly BackendRegistry _registry;
    private readonly PlannerPromptBuilder _promptBuilder;
    private readonly PlannerOutputParser _outputParser;
    private readonly TimeProvider _time;
    private readonly string _projectId;

    public PlannerService(
        IFlowStore store,
        BackendRegistry registry,
        string projectId,
        TimeProvider? time = null)
    {
        _store = store;
        _registry = registry;
        _projectId = projectId;
        _promptBuilder = new PlannerPromptBuilder();
        _outputParser = new PlannerOutputParser();
        _time = time ?? TimeProvider.System;
    }

    /// <summary>사용자 요청을 분해하여 Draft/Pending spec들을 생성한다.</summary>
    public async Task<PlanResult> PlanAsync(string userRequest, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userRequest))
        {
            return new PlanResult { Success = false, ErrorMessage = "empty user request" };
        }

        // 1. backend 획득
        var backend = _registry.GetBackend(AgentRole.Planner);
        if (backend == null)
        {
            return new PlanResult { Success = false, ErrorMessage = "no backend configured for Planner" };
        }

        // 2. 현재 spec 목록 로드
        var existingSpecs = await _store.LoadAllAsync(ct);

        // 3. 프롬프트 생성
        var prompt = _promptBuilder.BuildPrompt(userRequest, existingSpecs);

        // 4. backend 호출
        var definition = _registry.GetDefinition(AgentRole.Planner);
        var options = new CliBackendOptions
        {
            AllowFileEdits = false,
            AllowedTools = definition?.AllowedTools ?? ["Read", "Glob", "Grep"],
            IdleTimeout = TimeSpan.FromSeconds(definition?.IdleTimeoutSeconds ?? 300),
            HardTimeout = TimeSpan.FromSeconds(definition?.HardTimeoutSeconds ?? 1800)
        };

        CliResponse response;
        try
        {
            response = await backend.RunPromptAsync(prompt, options, ct);
        }
        catch (Exception ex)
        {
            return new PlanResult { Success = false, ErrorMessage = $"backend error: {ex.Message}" };
        }

        if (!response.Success)
        {
            return new PlanResult
            {
                Success = false,
                ErrorMessage = response.ErrorMessage ?? "backend returned failure"
            };
        }

        // 5. 파싱
        var parseResult = _outputParser.Parse(response.ResponseText);
        if (parseResult == null)
        {
            return new PlanResult
            {
                Success = false,
                ErrorMessage = "failed to parse Planner response"
            };
        }

        // 6. SpecDraft → Spec 변환 및 저장
        var createdSpecs = new List<Spec>();
        var now = _time.GetUtcNow();

        foreach (var draft in parseResult.Specs)
        {
            var specId = FlowId.New("spec");
            var spec = new Spec
            {
                Id = specId,
                ProjectId = _projectId,
                Title = draft.Title,
                Type = draft.Type,
                Problem = draft.Problem,
                Goal = draft.Goal,
                RiskLevel = draft.RiskLevel,
                State = FlowState.Draft,
                ProcessingStatus = ProcessingStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1
            };

            // AC 변환
            if (draft.AcceptanceCriteria.Count > 0)
            {
                spec.AcceptanceCriteria = draft.AcceptanceCriteria.Select(ac =>
                    new AcceptanceCriterion
                    {
                        Id = FlowId.New("ac"),
                        Text = ac.Text,
                        Testable = ac.Testable,
                        Notes = ac.Notes
                    }).ToList();
            }

            // 외부 DependsOn
            if (draft.DependsOn.Count > 0)
            {
                spec.Dependencies = new Dependency { DependsOn = draft.DependsOn };
            }

            createdSpecs.Add(spec);
        }

        // 7. InternalDependsOn → 실제 spec ID로 치환
        for (var i = 0; i < parseResult.Specs.Count; i++)
        {
            var draft = parseResult.Specs[i];
            if (draft.InternalDependsOn.Count == 0) continue;

            var spec = createdSpecs[i];
            var existingDeps = spec.Dependencies.DependsOn.ToList();

            foreach (var idx in draft.InternalDependsOn)
            {
                // 범위 검증: 자기 자신 참조와 범위 초과 무시
                if (idx < 0 || idx >= createdSpecs.Count || idx == i)
                    continue;
                var targetId = createdSpecs[idx].Id;
                if (!existingDeps.Contains(targetId))
                    existingDeps.Add(targetId);
            }

            spec.Dependencies = new Dependency { DependsOn = existingDeps };
        }

        // 8. 저장
        foreach (var spec in createdSpecs)
        {
            await _store.SaveAsync(spec, 0, ct);
        }

        return new PlanResult
        {
            Success = true,
            CreatedSpecs = createdSpecs,
            Summary = parseResult.Summary
        };
    }
}

/// <summary>PlannerService 결과</summary>
public sealed class PlanResult
{
    public bool Success { get; init; }
    public IReadOnlyList<Spec> CreatedSpecs { get; init; } = [];
    public string? Summary { get; init; }
    public string? ErrorMessage { get; init; }
}
