using FlowCore.Agents;
using FlowCore.Models;
using FlowCore.Rules;
using FlowCore.Storage;
using FlowCore.Utilities;

namespace FlowCore.Runner;

/// <summary>
/// Flow runner: orchestration coordinator.
/// 상태 판단은 RuleEvaluator, agent는 event producer.
/// </summary>
public sealed class FlowRunner
{
    private readonly IFlowStore _store;
    private readonly Dictionary<AgentRole, IAgentAdapter> _agents;
    private readonly RunnerConfig _config;
    private readonly TimeProvider _time;
    private readonly ReviewResponseSubmitter _submitter;
    private readonly IWorktreeProvisioner? _worktreeProvisioner;

    public FlowRunner(
        IFlowStore store,
        IEnumerable<IAgentAdapter> agents,
        RunnerConfig config,
        TimeProvider? time = null,
        IWorktreeProvisioner? worktreeProvisioner = null)
    {
        _store = store;
        _agents = agents.ToDictionary(a => a.Role);
        _config = config;
        _time = time ?? TimeProvider.System;
        _submitter = new ReviewResponseSubmitter(store);
        _worktreeProvisioner = worktreeProvisioner;
    }

    /// <summary>한 번의 cycle을 실행한다. 처리한 spec 수를 반환한다.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var runId = FlowId.New("run");
        var allSpecs = await _store.LoadAllAsync(ct);
        var specById = allSpecs.ToDictionary(s => s.Id);

        // assignment를 spec별로 로드
        var assignmentsBySpec = new Dictionary<string, IReadOnlyList<Assignment>>();
        foreach (var spec in allSpecs)
        {
            var asgs = await ((IAssignmentStore)_store).LoadBySpecAsync(spec.Id, ct);
            assignmentsBySpec[spec.Id] = asgs;
        }

        // 1. timeout/interrupt 처리 (assignment + review request)
        await ProcessTimeouts(allSpecs, assignmentsBySpec, runId, ct);

        // 재로드 (timeout 처리로 상태가 변경되었을 수 있음)
        allSpecs = await _store.LoadAllAsync(ct);
        specById = allSpecs.ToDictionary(s => s.Id);
        foreach (var spec in allSpecs)
        {
            assignmentsBySpec[spec.Id] = await ((IAssignmentStore)_store).LoadBySpecAsync(spec.Id, ct);
        }

        // 2. dispatch 대상 필터링
        var candidates = allSpecs
            .Where(s => !DispatchTable.ShouldExclude(s, _time))
            .Where(s => !DispatchTable.HasIncompleteUpstream(s, specById))
            .ToList();

        // 3. backlog 정렬
        var sorted = DispatchTable.SortBacklog(candidates, assignmentsBySpec, _time);

        // 4. spec별 처리
        var processed = 0;
        foreach (var spec in sorted.Take(_config.MaxSpecsPerCycle))
        {
            var assignments = assignmentsBySpec.GetValueOrDefault(spec.Id) ?? [];
            var reviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync(spec.Id, ct);

            var decision = DispatchTable.Decide(spec, assignments, reviewRequests);
            if (decision.Kind == DispatchKind.Wait)
                continue;

            await LogActivity(spec, ActivityAction.SpecSelected, runId,
                "spec selected for processing", ct);
            await LogActivity(spec, ActivityAction.DispatchDecided, runId,
                decision.Reason ?? "dispatch decided", ct);

            var success = decision.Kind switch
            {
                DispatchKind.RuleOnly => await ProcessRuleOnly(spec, decision, runId, ct),
                DispatchKind.Agent => await ProcessAgent(spec, decision, assignments, reviewRequests, runId, ct),
                _ => false
            };

            if (success)
                processed++;
        }

        return processed;
    }

    /// <summary>daemon 모드: RunOnce를 주기적으로 반복한다.</summary>
    public async Task RunDaemonAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await RunOnceAsync(ct);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.PollIntervalSeconds), _time, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>사용자 ReviewRequest 응답을 제출하고 후속 처리를 수행한다.</summary>
    public async Task<bool> SubmitReviewResponseAsync(
        string specId, string reviewRequestId,
        ReviewResponse response, CancellationToken ct = default)
    {
        var runId = FlowId.New("run");
        var result = await _submitter.SubmitResponseAsync(specId, reviewRequestId, response, ct);

        switch (result.Kind)
        {
            case SubmitResultKind.Success when result.ProposedEvent.HasValue:
            {
                var spec = await _store.LoadAsync(specId, ct);
                if (spec == null) return false;
                var assignments = await ((IAssignmentStore)_store).LoadBySpecAsync(specId, ct);
                var reviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync(specId, ct);
                var success = await EvaluateAndApply(spec, result.ProposedEvent.Value, ActorKind.User,
                    assignments, reviewRequests, runId, ct);

                // RR을 Answered로 커밋: 상태 전이가 성공한 후에만
                if (success && result.ValidatedReviewRequest != null)
                {
                    await _submitter.CommitAsync(result.ValidatedReviewRequest, ct);
                }
                return success;
            }
            case SubmitResultKind.NeedsPlannerReregistration:
            {
                var success = await HandleFailedSpecReregistrationAsync(specId, response, runId, ct);

                // RR을 Answered로 커밋: 재등록/아카이브가 성공한 후에만
                if (success && result.ValidatedReviewRequest != null)
                {
                    await _submitter.CommitAsync(result.ValidatedReviewRequest, ct);
                }
                return success;
            }
            default:
                return false;
        }
    }

    private async Task<bool> HandleFailedSpecReregistrationAsync(
        string failedSpecId, ReviewResponse response,
        string runId, CancellationToken ct)
    {
        var failedSpec = await _store.LoadAsync(failedSpecId, ct);
        if (failedSpec == null || failedSpec.State != FlowState.Failed)
        {
            return false;
        }

        // "discard" 옵션: 재등록 없이 아카이브
        if (response.SelectedOptionId == "discard")
        {
            return await ArchiveFailedSpecAsync(failedSpec, runId, ct);
        }

        // Planner 호출하여 재등록
        if (!_agents.TryGetValue(AgentRole.Planner, out var planner))
        {
            await LogActivity(failedSpec, ActivityAction.EventRejected, runId,
                "failed spec re-registration aborted: no Planner agent registered", ct);
            return false;
        }

        var recentActivity = await ((IActivityStore)_store).LoadRecentAsync(failedSpecId, 20, ct);
        var reviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync(failedSpecId, ct);

        var asgId = FlowId.New("asg");
        var plannerAssignment = new Assignment
        {
            Id = asgId, SpecId = failedSpecId,
            AgentRole = AgentRole.Planner,
            Type = AssignmentType.Planning,
            Status = AssignmentStatus.Running,
            StartedAt = _time.GetUtcNow(),
            TimeoutSeconds = _config.DefaultTimeoutSeconds
        };
        await ((IAssignmentStore)_store).SaveAsync(plannerAssignment, ct);

        var input = new AgentInput
        {
            Spec = failedSpec,
            Assignment = plannerAssignment,
            RecentActivity = recentActivity,
            ReviewRequests = reviewRequests,
            ProjectId = failedSpec.ProjectId,
            RunId = runId,
            CurrentVersion = failedSpec.Version
        };

        await LogActivity(failedSpec, ActivityAction.AgentInvoked, runId,
            "invoking Planner for failed spec re-registration", ct);

        var output = await planner.ExecuteAsync(input, ct);

        if (output.Result != AgentResult.Success)
        {
            plannerAssignment.Status = AssignmentStatus.Failed;
            plannerAssignment.FinishedAt = _time.GetUtcNow();
            plannerAssignment.ResultSummary = output.Message ?? output.Summary;
            await ((IAssignmentStore)_store).SaveAsync(plannerAssignment, ct);

            await LogActivity(failedSpec, ActivityAction.EventRejected, runId,
                $"Planner returned {output.Result}: {output.Message ?? output.Summary}", ct);
            return false;
        }

        plannerAssignment.Status = AssignmentStatus.Completed;
        plannerAssignment.FinishedAt = _time.GetUtcNow();
        plannerAssignment.ResultSummary = output.Summary;
        await ((IAssignmentStore)_store).SaveAsync(plannerAssignment, ct);

        // Planner contract: 재등록 시 ProposedSpec 필수 — 원본 복사 금지
        if (output.ProposedSpec == null)
        {
            await LogActivity(failedSpec, ActivityAction.EventRejected, runId,
                "Planner returned DraftCreated without ProposedSpec for re-registration — rejected", ct);
            return false;
        }

        var proposed = output.ProposedSpec;
        var newSpecId = FlowId.New("spec");
        var newSpec = new Spec
        {
            Id = newSpecId,
            ProjectId = failedSpec.ProjectId,
            Title = proposed.Title ?? failedSpec.Title,
            Type = proposed.Type ?? failedSpec.Type,
            Problem = proposed.Problem ?? failedSpec.Problem,
            Goal = proposed.Goal ?? failedSpec.Goal,
            AcceptanceCriteria = failedSpec.AcceptanceCriteria,
            RiskLevel = proposed.RiskLevel ?? failedSpec.RiskLevel,
            DerivedFrom = failedSpecId,
            State = FlowState.Draft,
            ProcessingStatus = ProcessingStatus.Pending,
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
            Version = 1
        };

        if (proposed.AcceptanceCriteria is { Count: > 0 } acDrafts)
        {
            newSpec.AcceptanceCriteria = acDrafts.Select(draft => new AcceptanceCriterion
            {
                Id = FlowId.New("ac"),
                Text = draft.Text,
                Testable = draft.Testable,
                Notes = draft.Notes
            }).ToList();
        }

        if (proposed.DependsOn is { Count: > 0 } deps)
        {
            newSpec.Dependencies = new Dependency { DependsOn = deps };
        }

        await _store.SaveAsync(newSpec, 0, ct);

        await LogActivity(newSpec, ActivityAction.DraftCreated, runId,
            $"re-registered from failed spec {failedSpecId}", ct);

        // 원본 아카이브
        return await ArchiveFailedSpecAsync(failedSpec, runId, ct);
    }

    private async Task<bool> ArchiveFailedSpecAsync(Spec spec, string runId, CancellationToken ct)
    {
        var assignments = await ((IAssignmentStore)_store).LoadBySpecAsync(spec.Id, ct);
        var reviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync(spec.Id, ct);
        var success = await EvaluateAndApply(spec, FlowEvent.SpecArchived, ActorKind.Runner,
            assignments, reviewRequests, runId, ct);

        if (success)
        {
            await _store.ArchiveAsync(spec.Id, ct);
        }
        return success;
    }

    private async Task ProcessTimeouts(
        IReadOnlyList<Spec> allSpecs,
        Dictionary<string, IReadOnlyList<Assignment>> assignmentsBySpec,
        string runId,
        CancellationToken ct)
    {
        var now = _time.GetUtcNow();

        foreach (var spec in allSpecs)
        {
            // ── stale assignment timeout ──
            if (spec.ProcessingStatus == ProcessingStatus.InProgress)
            {
                var assignments = assignmentsBySpec.GetValueOrDefault(spec.Id) ?? [];
                foreach (var asg in assignments)
                {
                    if (asg.Status != AssignmentStatus.Running) continue;
                    if (!asg.StartedAt.HasValue || !asg.TimeoutSeconds.HasValue) continue;
                    if (asg.StartedAt.Value.AddSeconds(asg.TimeoutSeconds.Value) >= now) continue;

                    // stale assignment detected
                    var reviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync(spec.Id, ct);
                    await EvaluateAndApply(spec, FlowEvent.AssignmentTimedOut, ActorKind.Runner,
                        assignments, reviewRequests, runId, ct);

                    // AssignmentResumed to go back to Pending
                    var updatedSpec = await _store.LoadAsync(spec.Id, ct);
                    if (updatedSpec != null && updatedSpec.ProcessingStatus == ProcessingStatus.Error)
                    {
                        var updatedAssignments = await ((IAssignmentStore)_store).LoadBySpecAsync(spec.Id, ct);
                        var updatedReviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync(spec.Id, ct);
                        await EvaluateAndApply(updatedSpec, FlowEvent.AssignmentResumed, ActorKind.Runner,
                            updatedAssignments, updatedReviewRequests, runId, ct);
                    }
                    break; // one timeout per spec per cycle
                }
            }

            // ── review request deadline timeout ──
            if (spec.State == FlowState.Review && spec.ProcessingStatus == ProcessingStatus.UserReview)
            {
                var reviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync(spec.Id, ct);
                var hasTimedOutRR = reviewRequests.Any(rr =>
                    rr.Status == ReviewRequestStatus.Open
                    && rr.DeadlineAt.HasValue
                    && rr.DeadlineAt.Value < now);

                if (hasTimedOutRR)
                {
                    var assignments = assignmentsBySpec.GetValueOrDefault(spec.Id) ?? [];
                    await EvaluateAndApply(spec, FlowEvent.ReviewRequestTimedOut, ActorKind.Runner,
                        assignments, reviewRequests, runId, ct);
                }
            }
        }
    }

    private async Task<bool> ProcessRuleOnly(
        Spec spec, DispatchDecision decision, string runId, CancellationToken ct)
    {
        if (decision.RuleOnlyEvent is not { } ruleEvent)
            return false;

        var assignments = await ((IAssignmentStore)_store).LoadBySpecAsync(spec.Id, ct);
        var reviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync(spec.Id, ct);

        var success = await EvaluateAndApply(spec, ruleEvent, ActorKind.Runner,
            assignments, reviewRequests, runId, ct);
        if (!success) return false;

        // rule-only 후 재로드하여 같은 cycle에서 다음 단계 처리
        var updatedSpec = await _store.LoadAsync(spec.Id, ct);
        if (updatedSpec == null) return true;

        var updatedAssignments = await ((IAssignmentStore)_store).LoadBySpecAsync(spec.Id, ct);
        var updatedReviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync(spec.Id, ct);
        var nextDecision = DispatchTable.Decide(updatedSpec, updatedAssignments, updatedReviewRequests);

        if (nextDecision.Kind == DispatchKind.Agent)
        {
            await ProcessAgent(updatedSpec, nextDecision, updatedAssignments, updatedReviewRequests, runId, ct);
        }

        return true;
    }

    private async Task<bool> ProcessAgent(
        Spec spec, DispatchDecision decision,
        IReadOnlyList<Assignment> assignments,
        IReadOnlyList<ReviewRequest> reviewRequests,
        string runId, CancellationToken ct)
    {
        if (decision.AgentRole is not { } agentRole) return false;
        if (!_agents.TryGetValue(agentRole, out var agent)) return false;

        // Planning assignment 재사용: DispatchTable이 기존 open Planning assignment를 감지한 경우
        var isPlanningDispatch = decision.AssignmentType == AssignmentType.Planning
            && agentRole == AgentRole.Planner;

        // 2-pass 대상 판단: ArchitectureReview/Implementation/TestValidation + Pending
        var is2PassTarget = !isPlanningDispatch
            && spec.State is (FlowState.ArchitectureReview
                or FlowState.Implementation or FlowState.TestValidation)
            && spec.ProcessingStatus == ProcessingStatus.Pending;

        // Draft/Pending: assignment 생성 필요 (RuleEvaluator의 side effect가 아닌 직접 생성)
        var isDraftPrecheck = !isPlanningDispatch
            && spec.State == FlowState.Draft
            && spec.ProcessingStatus == ProcessingStatus.Pending;

        Assignment currentAssignment;

        if (isPlanningDispatch)
        {
            // 기존 open Planning assignment 재사용
            currentAssignment = assignments.FirstOrDefault(a =>
                a.Type == AssignmentType.Planning
                && a.Status is AssignmentStatus.Queued or AssignmentStatus.Running)!;

            if (currentAssignment == null)
            {
                await LogActivity(spec, ActivityAction.EventRejected, runId,
                    "Planning dispatch but no open Planning assignment found", ct);
                return false;
            }

            // Queued → Running 전환 (TimeoutSeconds는 init-only이므로 새 객체 생성)
            if (currentAssignment.Status == AssignmentStatus.Queued)
            {
                currentAssignment = new Assignment
                {
                    Id = currentAssignment.Id,
                    SpecId = currentAssignment.SpecId,
                    AgentRole = currentAssignment.AgentRole,
                    Type = currentAssignment.Type,
                    Status = AssignmentStatus.Running,
                    StartedAt = _time.GetUtcNow(),
                    TimeoutSeconds = _config.DefaultTimeoutSeconds,
                    Worktree = currentAssignment.Worktree
                };
                await ((IAssignmentStore)_store).SaveAsync(currentAssignment, ct);
            }
        }
        else if (is2PassTarget)
        {
            // 2-pass: Pending → InProgress
            var success = await EvaluateAndApply(spec, FlowEvent.AssignmentStarted, ActorKind.Runner,
                assignments, reviewRequests, runId, ct);
            if (!success) return false;

            // spec 재로드
            spec = (await _store.LoadAsync(spec.Id, ct))!;
            assignments = await ((IAssignmentStore)_store).LoadBySpecAsync(spec.Id, ct);

            // 가장 최근 Running assignment를 찾는다
            currentAssignment = assignments.LastOrDefault(a => a.Status == AssignmentStatus.Running)!;
            if (currentAssignment == null)
            {
                // 2-pass 후 Running assignment가 없으면 생성
                var asgId = FlowId.New("asg");
                currentAssignment = new Assignment
                {
                    Id = asgId,
                    SpecId = spec.Id,
                    AgentRole = agentRole,
                    Type = decision.AssignmentType!.Value,
                    Status = AssignmentStatus.Running,
                    StartedAt = _time.GetUtcNow(),
                    TimeoutSeconds = _config.DefaultTimeoutSeconds
                };
                await ((IAssignmentStore)_store).SaveAsync(currentAssignment, ct);
                spec.Assignments = spec.Assignments.Append(asgId).ToList();
                var saveResult = await _store.SaveAsync(spec, spec.Version, ct);
                if (!saveResult.IsSuccess) return false;
                spec = (await _store.LoadAsync(spec.Id, ct))!;
            }
        }
        else if (isDraftPrecheck)
        {
            // Draft/Pending: assignment 직접 생성
            var asgId = FlowId.New("asg");
            currentAssignment = new Assignment
            {
                Id = asgId,
                SpecId = spec.Id,
                AgentRole = agentRole,
                Type = decision.AssignmentType!.Value,
                Status = AssignmentStatus.Running,
                StartedAt = _time.GetUtcNow(),
                TimeoutSeconds = _config.DefaultTimeoutSeconds
            };
            await ((IAssignmentStore)_store).SaveAsync(currentAssignment, ct);
            spec.Assignments = spec.Assignments.Append(asgId).ToList();
            var saveResult = await _store.SaveAsync(spec, spec.Version, ct);
            if (!saveResult.IsSuccess)
            {
                await LogActivity(spec, ActivityAction.ConflictDetected, runId,
                    $"CAS conflict adding draft assignment: {saveResult.Status}", ct);
                return false;
            }
            spec = (await _store.LoadAsync(spec.Id, ct))!;
            assignments = await ((IAssignmentStore)_store).LoadBySpecAsync(spec.Id, ct);
        }
        else
        {
            // Review/InReview: 기존 Running assignment를 사용
            currentAssignment = assignments.LastOrDefault(a => a.Status == AssignmentStatus.Running)!;
            if (currentAssignment == null)
            {
                // assignment가 없으면 생성
                var asgId = FlowId.New("asg");
                currentAssignment = new Assignment
                {
                    Id = asgId,
                    SpecId = spec.Id,
                    AgentRole = agentRole,
                    Type = decision.AssignmentType!.Value,
                    Status = AssignmentStatus.Running,
                    StartedAt = _time.GetUtcNow(),
                    TimeoutSeconds = _config.DefaultTimeoutSeconds
                };
                await ((IAssignmentStore)_store).SaveAsync(currentAssignment, ct);
                spec.Assignments = spec.Assignments.Append(asgId).ToList();
                var saveResult = await _store.SaveAsync(spec, spec.Version, ct);
                if (!saveResult.IsSuccess) return false;
                spec = (await _store.LoadAsync(spec.Id, ct))!;
                assignments = await ((IAssignmentStore)_store).LoadBySpecAsync(spec.Id, ct);
            }
        }

        // Worktree provisioning: Implementation/TestValidation은 worktree 필요
        if (_worktreeProvisioner != null
            && currentAssignment.Type is AssignmentType.Implementation or AssignmentType.TestValidation
            && currentAssignment.Worktree == null)
        {
            var provisionResult = await _worktreeProvisioner.CreateAsync(spec.Id, ct);
            if (!provisionResult.Success)
            {
                await LogActivity(spec, ActivityAction.AgentCompleted, runId,
                    $"worktree provisioning failed for {currentAssignment.Type}", ct, currentAssignment.Id);
                currentAssignment.Status = AssignmentStatus.Failed;
                currentAssignment.FinishedAt = _time.GetUtcNow();
                currentAssignment.ResultSummary = "worktree provisioning failed";
                await ((IAssignmentStore)_store).SaveAsync(currentAssignment, ct);
                return false;
            }

            // Assignment.Worktree는 init-only → 새 객체 생성
            currentAssignment = new Assignment
            {
                Id = currentAssignment.Id,
                SpecId = currentAssignment.SpecId,
                AgentRole = currentAssignment.AgentRole,
                Type = currentAssignment.Type,
                Status = currentAssignment.Status,
                StartedAt = currentAssignment.StartedAt,
                TimeoutSeconds = currentAssignment.TimeoutSeconds,
                Worktree = new AssignmentWorktree
                {
                    Id = provisionResult.WorktreeId ?? FlowId.New("wt"),
                    Path = provisionResult.Path!,
                    Branch = provisionResult.Branch
                }
            };
            await ((IAssignmentStore)_store).SaveAsync(currentAssignment, ct);
        }

        // Agent 호출
        var recentActivity = await ((IActivityStore)_store).LoadRecentAsync(spec.Id, 20, ct);
        var allReviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync(spec.Id, ct);

        var input = new AgentInput
        {
            Spec = spec,
            Assignment = currentAssignment,
            RecentActivity = recentActivity,
            ReviewRequests = allReviewRequests,
            ProjectId = spec.ProjectId,
            RunId = runId,
            CurrentVersion = spec.Version
        };

        await LogActivity(spec, ActivityAction.AgentInvoked, runId,
            $"invoking {agentRole}", ct, currentAssignment.Id);

        var output = await agent.ExecuteAsync(input, ct);

        await LogActivity(spec, ActivityAction.AgentCompleted, runId,
            $"{agentRole} completed: {output.Result}", ct, currentAssignment.Id);

        // Agent 결과 처리
        return await HandleAgentResult(spec, currentAssignment, output, assignments, runId, ct);
    }

    private async Task<bool> HandleAgentResult(
        Spec spec, Assignment assignment, AgentOutput output,
        IReadOnlyList<Assignment> assignments,
        string runId, CancellationToken ct)
    {
        switch (output.Result)
        {
            case AgentResult.Success:
            {
                if (output.ProposedEvent is not { } proposedEvent)
                    return false;

                // agent BaseVersion 검증: agent가 본 version과 현재 spec version이 다르면 거부
                if (output.BaseVersion != spec.Version)
                {
                    await LogActivity(spec, ActivityAction.EventRejected, runId,
                        $"agent BaseVersion mismatch: agent saw v{output.BaseVersion}, current v{spec.Version}", ct);
                    return false;
                }

                // Planner contract: DraftUpdated/DraftCreated는 ProposedSpec 필수
                if (proposedEvent is FlowEvent.DraftUpdated or FlowEvent.DraftCreated
                    && assignment.Type == AssignmentType.Planning)
                {
                    if (output.ProposedSpec == null)
                    {
                        await LogActivity(spec, ActivityAction.EventRejected, runId,
                            $"Planner returned {proposedEvent} without ProposedSpec — rejected", ct);
                        return false;
                    }
                    ApplyProposedSpec(spec, output.ProposedSpec);
                }

                // assignment를 Completed로 마킹 (RejectIfActiveAssignment 방지)
                assignment.Status = AssignmentStatus.Completed;
                assignment.FinishedAt = _time.GetUtcNow();
                assignment.ResultSummary = output.Summary;
                await ((IAssignmentStore)_store).SaveAsync(assignment, ct);

                // 재로드 assignments (completed 반영)
                var updatedAssignments = await ((IAssignmentStore)_store).LoadBySpecAsync(spec.Id, ct);
                var reviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync(spec.Id, ct);

                var actorKind = AgentRoleToActorKind(assignment.AgentRole);
                var committed = await EvaluateAndApply(spec, proposedEvent, actorKind,
                    updatedAssignments, reviewRequests, runId, ct,
                    output.ProposedReviewRequest);

                // Evidence manifest 저장: CAS 커밋 성공 후에만 저장
                if (committed && output.EvidenceRefs is { Count: > 0 } refs)
                {
                    var manifest = new EvidenceManifest
                    {
                        SpecId = spec.Id,
                        RunId = runId,
                        CreatedAt = _time.GetUtcNow(),
                        Refs = refs
                    };
                    try
                    {
                        await ((IEvidenceStore)_store).SaveManifestAsync(manifest, ct);
                    }
                    catch { /* best-effort */ }
                }

                return committed;
            }

            case AgentResult.RetryableFailure:
            {
                // assignment를 Failed로 마킹
                assignment.Status = AssignmentStatus.Failed;
                assignment.FinishedAt = _time.GetUtcNow();
                assignment.ResultSummary = output.Message;
                await ((IAssignmentStore)_store).SaveAsync(assignment, ct);

                // retry 처리: RuleEvaluator를 우회하여 직접 spec 업데이트
                var retryCount = GetRetryCount(spec);
                if (retryCount >= _config.MaxRetries)
                {
                    // retry 초과 → execution failure로 전환
                    var updatedAssignments = await ((IAssignmentStore)_store).LoadBySpecAsync(spec.Id, ct);
                    var reviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync(spec.Id, ct);

                    return await EvaluateAndApply(spec,
                        GetTerminalFailureEvent(spec), GetTerminalFailureActor(spec),
                        updatedAssignments, reviewRequests, runId, ct);
                }

                spec.ProcessingStatus = ProcessingStatus.Pending;
                IncrementRetryCounter(spec);
                spec.RetryCounters.RetryNotBefore = _time.GetUtcNow()
                    .AddSeconds(retryCount * _config.RetryBackoffBaseSeconds);
                spec.UpdatedAt = _time.GetUtcNow();

                var saveResult = await _store.SaveAsync(spec, spec.Version, ct);
                if (!saveResult.IsSuccess)
                {
                    await LogActivity(spec, ActivityAction.ConflictDetected, runId,
                        "CAS conflict on retry scheduling", ct);
                    return false;
                }
                await LogActivity(spec, ActivityAction.RetryScheduled, runId,
                    $"retry scheduled (count={retryCount + 1}, notBefore={spec.RetryCounters.RetryNotBefore})", ct);
                return true;
            }

            case AgentResult.TerminalFailure:
            {
                assignment.Status = AssignmentStatus.Failed;
                assignment.FinishedAt = _time.GetUtcNow();
                assignment.ResultSummary = output.Message;
                await ((IAssignmentStore)_store).SaveAsync(assignment, ct);

                var updatedAssignments = await ((IAssignmentStore)_store).LoadBySpecAsync(spec.Id, ct);
                var reviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync(spec.Id, ct);

                return await EvaluateAndApply(spec,
                    GetTerminalFailureEvent(spec), GetTerminalFailureActor(spec),
                    updatedAssignments, reviewRequests, runId, ct);
            }

            case AgentResult.NoOp:
                await LogActivity(spec, ActivityAction.AgentCompleted, runId,
                    $"agent returned NoOp: {output.Message}", ct);
                return true;

            default:
                return false;
        }
    }

    private async Task<bool> EvaluateAndApply(
        Spec spec, FlowEvent ev, ActorKind actor,
        IReadOnlyList<Assignment> assignments,
        IReadOnlyList<ReviewRequest> reviewRequests,
        string runId, CancellationToken ct,
        ProposedReviewRequest? proposedReviewRequest = null)
    {
        var previousState = spec.State;
        var previousProcessingStatus = spec.ProcessingStatus;

        var ruleInput = new RuleInput
        {
            Spec = spec.ToSnapshot(),
            Event = ev,
            Actor = actor,
            Assignments = assignments,
            ReviewRequests = reviewRequests,
            BaseVersion = spec.Version
        };

        var ruleOutput = RuleEvaluator.Evaluate(ruleInput);

        if (!ruleOutput.Accepted)
        {
            await LogActivity(spec, ActivityAction.EventRejected, runId,
                $"event {ev} rejected: {ruleOutput.RejectionReason}", ct);
            return false;
        }

        // Apply mutation in-memory
        if (ruleOutput.Mutation is { } mutation)
        {
            if (mutation.NewState.HasValue) spec.State = mutation.NewState.Value;
            if (mutation.NewProcessingStatus.HasValue) spec.ProcessingStatus = mutation.NewProcessingStatus.Value;
            if (mutation.NewRetryCounters != null) spec.RetryCounters = mutation.NewRetryCounters;
            spec.Version = mutation.NewVersion;
            spec.UpdatedAt = _time.GetUtcNow();
        }

        // Enrich side effects with ProposedReviewRequest from agent
        var sideEffects = ruleOutput.SideEffects;
        if (proposedReviewRequest != null)
        {
            sideEffects = EnrichCreateReviewRequestEffects(sideEffects, proposedReviewRequest);
        }

        // Execute side effects
        var executor = new SideEffectExecutor(_store, _config, _time);
        var sideEffectResult = await executor.ExecuteAsync(
            sideEffects, spec, runId, ct);

        // Save spec (CAS 1x) — expectedVersion is the pre-mutation version
        var expectedVersion = (ruleOutput.Mutation?.NewVersion ?? spec.Version) - 1;
        var saveResult = await _store.SaveAsync(spec, expectedVersion, ct);

        if (!saveResult.IsSuccess)
        {
            // CAS 실패: side effect에서 생성한 파일 rollback
            await executor.RollbackCreatedFilesAsync(sideEffectResult, spec, ct);

            await LogActivity(spec, ActivityAction.ConflictDetected, runId,
                $"CAS conflict: expected={expectedVersion}, current={saveResult.CurrentVersion}", ct);
            return false;
        }

        // Log activity (best-effort)
        await LogActivity(spec, ActivityAction.StateTransitionCommitted, runId,
            $"{ev} → {spec.State}/{spec.ProcessingStatus}", ct);

        foreach (var activityEvent in sideEffectResult.ActivityEvents)
        {
            try { await ((IActivityStore)_store).AppendAsync(activityEvent, ct); }
            catch { /* best-effort */ }
        }

        // Worktree cleanup: spec이 종료 상태로 전이되면 worktree를 정리
        if (_worktreeProvisioner != null
            && spec.State is FlowState.Failed or FlowState.Completed or FlowState.Active or FlowState.Archived
            && previousState is FlowState.ArchitectureReview or FlowState.Implementation
                or FlowState.TestValidation or FlowState.Review)
        {
            try { await _worktreeProvisioner.CleanupAsync(spec.Id, ct); }
            catch { /* best-effort */ }
        }

        // Dependency cascade: 상태 변경이 downstream에 영향을 줄 수 있으면 cascade 적용
        if (spec.State != previousState || spec.ProcessingStatus != previousProcessingStatus)
        {
            await ApplyDependencyCascade(spec, previousState, previousProcessingStatus, runId, ct);
        }

        return true;
    }

    /// <summary>
    /// 상태 변경 후 downstream spec들에 dependency cascade를 적용한다.
    /// </summary>
    private async Task ApplyDependencyCascade(
        Spec changedSpec,
        FlowState previousState,
        ProcessingStatus previousProcessingStatus,
        string runId, CancellationToken ct)
    {
        // 모든 spec 로드하여 downstream 찾기
        var allSpecs = await _store.LoadAllAsync(ct);
        var downstreamSpecs = allSpecs
            .Where(s => s.Dependencies.DependsOn.Contains(changedSpec.Id))
            .Select(s => s.ToSnapshot())
            .ToList();

        if (downstreamSpecs.Count == 0) return;

        // AllUpstreamSpecs: downstream의 dependsOn에 포함된 모든 upstream의 현재 snapshot
        var allUpstreamIds = downstreamSpecs
            .SelectMany(ds => ds.DependsOn)
            .Distinct()
            .ToHashSet();
        var allUpstreamSpecs = allSpecs
            .Where(s => allUpstreamIds.Contains(s.Id))
            .Select(s => s.ToSnapshot())
            .ToList();

        var cascadeInput = new DependencyInput
        {
            ChangedSpec = changedSpec.ToSnapshot(),
            PreviousState = previousState,
            PreviousProcessingStatus = previousProcessingStatus,
            DownstreamSpecs = downstreamSpecs,
            AllUpstreamSpecs = allUpstreamSpecs
        };

        var effects = DependencyEvaluator.Evaluate(cascadeInput);

        foreach (var effect in effects)
        {
            var targetSpec = await _store.LoadAsync(effect.TargetSpecId, ct);
            if (targetSpec == null) continue;

            var targetAssignments = await ((IAssignmentStore)_store).LoadBySpecAsync(effect.TargetSpecId, ct);
            var targetReviewRequests = await ((IReviewRequestStore)_store).LoadBySpecAsync(effect.TargetSpecId, ct);

            await EvaluateAndApply(targetSpec, effect.Event, ActorKind.Runner,
                targetAssignments, targetReviewRequests, runId, ct);
        }
    }

    private async Task LogActivity(
        Spec spec, ActivityAction action, string runId,
        string message, CancellationToken ct, string? assignmentId = null)
    {
        try
        {
            var evt = new ActivityEvent
            {
                EventId = FlowId.New("evt"),
                Timestamp = _time.GetUtcNow(),
                SpecId = spec.Id,
                Actor = "runner",
                Action = action,
                SourceType = "runner",
                BaseVersion = spec.Version,
                State = spec.State,
                ProcessingStatus = spec.ProcessingStatus,
                Message = message,
                CorrelationId = runId,
                AssignmentId = assignmentId
            };
            await ((IActivityStore)_store).AppendAsync(evt, ct);
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// ProposedSpec을 현재 spec에 반영한다. null 필드는 기존 값을 유지한다.
    /// AC ID는 runner가 새로 부여한다.
    /// </summary>
    private static void ApplyProposedSpec(Spec spec, ProposedSpecDraft proposed)
    {
        if (proposed.Title != null) spec.Title = proposed.Title;
        if (proposed.Type.HasValue) spec.Type = proposed.Type.Value;
        if (proposed.Problem != null) spec.Problem = proposed.Problem;
        if (proposed.Goal != null) spec.Goal = proposed.Goal;
        if (proposed.RiskLevel.HasValue) spec.RiskLevel = proposed.RiskLevel.Value;
        if (proposed.DependsOn != null) spec.Dependencies = new Dependency { DependsOn = proposed.DependsOn };

        if (proposed.AcceptanceCriteria is { Count: > 0 } acDrafts)
        {
            spec.AcceptanceCriteria = acDrafts.Select(draft => new AcceptanceCriterion
            {
                Id = FlowId.New("ac"),
                Text = draft.Text,
                Testable = draft.Testable,
                Notes = draft.Notes
            }).ToList();
        }
    }

    private static ActorKind AgentRoleToActorKind(AgentRole role) => role switch
    {
        AgentRole.Planner => ActorKind.Planner,
        AgentRole.Architect => ActorKind.Architect,
        AgentRole.Developer => ActorKind.Developer,
        AgentRole.TestValidator => ActorKind.TestValidator,
        AgentRole.SpecValidator => ActorKind.SpecValidator,
        AgentRole.SpecManager => ActorKind.SpecManager,
        _ => ActorKind.Runner
    };

    private static int GetRetryCount(Spec spec) => spec.State switch
    {
        FlowState.ArchitectureReview => spec.RetryCounters.ArchitectReviewLoopCount,
        FlowState.Implementation => spec.RetryCounters.ImplementationRetryCount,
        FlowState.TestValidation => spec.RetryCounters.TestValidationRetryCount,
        FlowState.Review => spec.RetryCounters.ReworkLoopCount,
        _ => spec.RetryCounters.ReworkLoopCount
    };

    private static void IncrementRetryCounter(Spec spec)
    {
        switch (spec.State)
        {
            case FlowState.ArchitectureReview:
                spec.RetryCounters.ArchitectReviewLoopCount++;
                break;
            case FlowState.Implementation:
                spec.RetryCounters.ImplementationRetryCount++;
                break;
            case FlowState.TestValidation:
                spec.RetryCounters.TestValidationRetryCount++;
                break;
            default:
                spec.RetryCounters.ReworkLoopCount++;
                break;
        }
    }

    /// <summary>
    /// 실행 실패(retry 초과, TerminalFailure)에 사용할 이벤트를 결정한다.
    /// Review → SpecValidationFailed (기존 도메인 이벤트), 그 외 → ExecutionFailed (시스템 실행 오류).
    /// </summary>
    private static FlowEvent GetTerminalFailureEvent(Spec spec) => spec.State == FlowState.Review
        ? FlowEvent.SpecValidationFailed
        : FlowEvent.ExecutionFailed;

    private static ActorKind GetTerminalFailureActor(Spec spec) => spec.State == FlowState.Review
        ? ActorKind.SpecValidator
        : ActorKind.Runner;

    /// <summary>
    /// CreateReviewRequest side effect에 agent의 ProposedReviewRequest 정보를 병합한다.
    /// </summary>
    private static IReadOnlyList<SideEffect> EnrichCreateReviewRequestEffects(
        IReadOnlyList<SideEffect> effects, ProposedReviewRequest proposed)
    {
        var result = new List<SideEffect>(effects.Count);
        foreach (var effect in effects)
        {
            if (effect.Kind == SideEffectKind.CreateReviewRequest)
            {
                result.Add(SideEffect.CreateReviewRequest(
                    effect.Reason ?? "",
                    effect.SpecId,
                    proposed.Questions ?? effect.Questions,
                    effect.DeadlineSeconds,
                    proposed.Options ?? effect.ReviewRequestOptions,
                    proposed.Summary ?? effect.ReviewRequestSummary));
            }
            else
            {
                result.Add(effect);
            }
        }
        return result;
    }
}
