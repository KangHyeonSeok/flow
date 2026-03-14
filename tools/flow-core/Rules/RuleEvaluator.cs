using FlowCore.Models;

namespace FlowCore.Rules;

/// <summary>
/// 순수 함수 기반 상태 전이 규칙 평가기.
/// I/O 없이 (spec, assignments, reviewRequests, event, actor) → (accepted, mutation, sideEffects)를 계산한다.
/// </summary>
public static class RuleEvaluator
{
    private const int MaxRetryCount = 3;

    /// <summary>이벤트별 허용 actor 매핑 (flow-state-rule.md §4.7)</summary>
    private static readonly Dictionary<FlowEvent, ActorKind[]> EventActorPermissions = new()
    {
        { FlowEvent.DraftCreated,                        [ActorKind.Planner] },
        { FlowEvent.DraftUpdated,                        [ActorKind.Planner] },
        { FlowEvent.AcPrecheckPassed,                    [ActorKind.SpecValidator] },
        { FlowEvent.AcPrecheckRejected,                  [ActorKind.SpecValidator] },
        { FlowEvent.ArchitectReviewPassed,               [ActorKind.Architect] },
        { FlowEvent.ArchitectReviewRejected,             [ActorKind.Architect] },
        { FlowEvent.AssignmentStarted,                   [ActorKind.Runner] },
        { FlowEvent.ImplementationSubmitted,             [ActorKind.Developer] },
        { FlowEvent.TestValidationPassed,                [ActorKind.TestValidator] },
        { FlowEvent.TestValidationRejected,              [ActorKind.TestValidator] },
        { FlowEvent.SpecValidationPassed,                [ActorKind.SpecValidator] },
        { FlowEvent.SpecValidationReworkRequested,       [ActorKind.SpecValidator] },
        { FlowEvent.SpecValidationUserReviewRequested,   [ActorKind.SpecValidator] },
        { FlowEvent.SpecValidationFailed,                [ActorKind.SpecValidator] },
        { FlowEvent.UserReviewSubmitted,                 [ActorKind.User] },
        { FlowEvent.SpecCompleted,                       [ActorKind.User] },
        { FlowEvent.CancelRequested,                     [ActorKind.User] },
        { FlowEvent.RollbackRequested,                   [ActorKind.User] },
        { FlowEvent.DependencyBlocked,                   [ActorKind.Runner, ActorKind.SpecManager] },
        { FlowEvent.DependencyFailed,                    [ActorKind.Runner, ActorKind.SpecManager] },
        { FlowEvent.DependencyResolved,                  [ActorKind.Runner, ActorKind.SpecManager] },
        { FlowEvent.AssignmentTimedOut,                   [ActorKind.Runner] },
        { FlowEvent.AssignmentResumed,                   [ActorKind.Runner, ActorKind.SpecManager] },
        { FlowEvent.ReviewRequestTimedOut,               [ActorKind.Runner] },
        { FlowEvent.SpecArchived,                        [ActorKind.Runner] },
        { FlowEvent.ExecutionFailed,                     [ActorKind.Runner] },
    };

    public static RuleOutput Evaluate(RuleInput input)
    {
        // 1. version conflict 검증
        if (input.BaseVersion != input.Spec.Version)
        {
            return RuleOutput.Reject(RejectionReason.ConflictError,
                [SideEffect.Log("version conflict detected")]);
        }

        // 2. actor permission 검증
        if (EventActorPermissions.TryGetValue(input.Event, out var allowed)
            && !allowed.Contains(input.Actor))
        {
            return RuleOutput.Reject(RejectionReason.UnauthorizedActor,
                [SideEffect.Log($"actor {input.Actor} not authorized for event {input.Event}")]);
        }

        // 3. 이벤트별 전이 계산
        return input.Event switch
        {
            // 초안 단계
            FlowEvent.DraftCreated => EvalDraftCreated(input),
            FlowEvent.DraftUpdated => EvalDraftUpdated(input),
            FlowEvent.AcPrecheckPassed => EvalAcPrecheckPassed(input),
            FlowEvent.AcPrecheckRejected => EvalAcPrecheckRejected(input),

            // 대기 → 구현/구현 검토
            FlowEvent.AssignmentStarted => EvalAssignmentStarted(input),

            // 구현 검토 단계
            FlowEvent.ArchitectReviewPassed => EvalArchitectReviewPassed(input),
            FlowEvent.ArchitectReviewRejected => EvalArchitectReviewRejected(input),

            // 구현 단계
            FlowEvent.ImplementationSubmitted => EvalImplementationSubmitted(input),

            // 테스트 검증 단계
            FlowEvent.TestValidationPassed => EvalTestValidationPassed(input),
            FlowEvent.TestValidationRejected => EvalTestValidationRejected(input),

            // 검토 단계
            FlowEvent.SpecValidationPassed => EvalSpecValidationPassed(input),
            FlowEvent.SpecValidationReworkRequested => EvalSpecValidationReworkRequested(input),
            FlowEvent.SpecValidationUserReviewRequested => EvalSpecValidationUserReviewRequested(input),
            FlowEvent.SpecValidationFailed => EvalSpecValidationFailed(input),
            FlowEvent.UserReviewSubmitted => EvalUserReviewSubmitted(input),
            FlowEvent.ReviewRequestTimedOut => EvalReviewRequestTimedOut(input),

            // 활성/완료 단계
            FlowEvent.SpecCompleted => EvalSpecCompleted(input),
            FlowEvent.RollbackRequested => EvalRollbackRequested(input),

            // 취소
            FlowEvent.CancelRequested => EvalCancelRequested(input),

            // dependency
            FlowEvent.DependencyBlocked => EvalDependencyBlocked(input),
            FlowEvent.DependencyFailed => EvalDependencyFailed(input),
            FlowEvent.DependencyResolved => EvalDependencyResolved(input),

            // timeout/resume
            FlowEvent.AssignmentTimedOut => EvalAssignmentTimedOut(input),
            FlowEvent.AssignmentResumed => EvalAssignmentResumed(input),

            // execution failure (agent crash, timeout, provisioning failure)
            FlowEvent.ExecutionFailed => EvalExecutionFailed(input),

            // archive
            FlowEvent.SpecArchived => EvalSpecArchived(input),

            _ => RuleOutput.Reject(RejectionReason.InvalidStateForEvent)
        };
    }

    // ── assignment/review request query helpers ──

    private static IReadOnlyList<Assignment> GetActiveAssignments(RuleInput input) =>
        input.Assignments.Where(a => a.Status is AssignmentStatus.Running or AssignmentStatus.Queued).ToList();

    private static IReadOnlyList<ReviewRequest> GetOpenReviewRequests(RuleInput input) =>
        input.ReviewRequests.Where(r => r.Status == ReviewRequestStatus.Open).ToList();

    /// <summary>running/queued assignment가 있으면 phase 전환을 거부한다.</summary>
    private static RuleOutput? RejectIfActiveAssignment(RuleInput input)
    {
        var active = GetActiveAssignments(input);
        if (active.Count > 0)
        {
            return RuleOutput.Reject(RejectionReason.ActiveAssignmentExists,
                [SideEffect.Log($"cannot transition: {active.Count} active assignment(s) exist (ids: {string.Join(", ", active.Select(a => a.Id))})")]);
        }
        return null;
    }

    /// <summary>active assignment를 CancelAssignment side effect로 변환한다.</summary>
    private static List<SideEffect> BuildCancelActiveAssignmentEffects(RuleInput input)
    {
        return GetActiveAssignments(input)
            .Select(a => SideEffect.CancelAssignment(a.Id, $"cancel active assignment {a.Id}"))
            .ToList();
    }

    /// <summary>active assignment를 FailAssignment side effect로 변환한다.</summary>
    private static List<SideEffect> BuildFailActiveAssignmentEffects(RuleInput input, string? reason = null)
    {
        return GetActiveAssignments(input)
            .Select(a => SideEffect.FailAssignment(a.Id, reason ?? $"fail active assignment {a.Id}"))
            .ToList();
    }

    /// <summary>open review request를 CloseReviewRequest side effect로 변환한다.</summary>
    private static List<SideEffect> BuildCloseOpenReviewRequestEffects(RuleInput input, string? description = null)
    {
        return GetOpenReviewRequests(input)
            .Select(r => SideEffect.CloseReviewRequest(r.Id, description ?? $"close review request {r.Id}"))
            .ToList();
    }

    // ── 초안 ──

    private static RuleOutput EvalDraftCreated(RuleInput input)
    {
        var spec = input.Spec;
        if (spec.State != FlowState.Draft)
            return RejectState(spec.State, input.Event);

        return Accept(spec, FlowState.Draft, ProcessingStatus.Pending,
            [SideEffect.Log("draft created")]);
    }

    private static RuleOutput EvalDraftUpdated(RuleInput input)
    {
        var spec = input.Spec;
        if (spec.State == FlowState.Draft)
        {
            return Accept(spec, FlowState.Draft, ProcessingStatus.Pending,
                [SideEffect.Log("draft updated, AC precheck 재수행 대상")]);
        }
        if (spec.State == FlowState.ArchitectureReview)
        {
            // draft_updated는 architect rejection 후 Planner 보완 완료 시 발생 — active assignment 없어야 함
            var reject = RejectIfActiveAssignment(input);
            if (reject != null) return reject;

            return Accept(spec, FlowState.ArchitectureReview, ProcessingStatus.Pending,
                [
                    SideEffect.Log("draft updated after architect rejection"),
                    SideEffect.CreateAssignment(Models.AgentRole.Architect,
                        Models.AssignmentType.ArchitectureReview, spec.Id, "Architect 재검토")
                ]);
        }
        return RejectState(spec.State, input.Event);
    }

    private static RuleOutput EvalAcPrecheckPassed(RuleInput input)
    {
        if (input.Spec.State != FlowState.Draft)
            return RejectState(input.Spec.State, input.Event);

        var reject = RejectIfActiveAssignment(input);
        if (reject != null) return reject;

        var counters = ResetAllCounters();
        return AcceptPhaseTransition(input.Spec, FlowState.Queued, counters,
            [SideEffect.Log("AC precheck passed")]);
    }

    private static RuleOutput EvalAcPrecheckRejected(RuleInput input)
    {
        if (input.Spec.State != FlowState.Draft)
            return RejectState(input.Spec.State, input.Event);

        return Accept(input.Spec, FlowState.Draft, ProcessingStatus.Pending,
            [
                SideEffect.Log("AC precheck rejected"),
                SideEffect.CreateAssignment(Models.AgentRole.Planner,
                    Models.AssignmentType.Planning, input.Spec.Id, "Planner 보완 요청")
            ]);
    }

    // ── 대기 → 구현/구현 검토 ──

    private static RuleOutput EvalAssignmentStarted(RuleInput input)
    {
        var spec = input.Spec;

        if (spec.State == FlowState.Queued)
        {
            var reject = RejectIfActiveAssignment(input);
            if (reject != null) return reject;

            if (spec.RiskLevel is RiskLevel.Medium or RiskLevel.High or RiskLevel.Critical)
            {
                var counters = ResetAllCounters();
                return AcceptPhaseTransition(spec, FlowState.ArchitectureReview, counters,
                    [
                        SideEffect.Log("architect review assignment started"),
                        SideEffect.CreateAssignment(Models.AgentRole.Architect,
                            Models.AssignmentType.ArchitectureReview, spec.Id)
                    ]);
            }
            else
            {
                var counters = ResetAllCounters();
                return AcceptPhaseTransition(spec, FlowState.Implementation, counters,
                    [
                        SideEffect.Log("implementation assignment started"),
                        SideEffect.CreateAssignment(Models.AgentRole.Developer,
                            Models.AssignmentType.Implementation, spec.Id)
                    ]);
            }
        }

        // 구현/테스트 검증/구현 검토에서의 assignment_started는 processingStatus만 변경
        if (spec.State is FlowState.Implementation or FlowState.TestValidation
            or FlowState.ArchitectureReview)
        {
            if (spec.ProcessingStatus != ProcessingStatus.Pending)
                return RejectState(spec.State, input.Event);

            return Accept(spec, spec.State, ProcessingStatus.InProgress,
                [SideEffect.Log("assignment started")]);
        }

        return RejectState(spec.State, input.Event);
    }

    // ── 구현 검토 ──

    private static RuleOutput EvalArchitectReviewPassed(RuleInput input)
    {
        if (input.Spec.State != FlowState.ArchitectureReview)
            return RejectState(input.Spec.State, input.Event);

        // architect review 완료 → 기존 assignment는 completed 상태여야 함
        // running assignment가 남아있으면 거부
        var reject = RejectIfActiveAssignment(input);
        if (reject != null) return reject;

        var counters = ResetAllCounters();
        return AcceptPhaseTransition(input.Spec, FlowState.Implementation, counters,
            [
                SideEffect.Log("architect review passed"),
                SideEffect.CreateAssignment(Models.AgentRole.Developer,
                    Models.AssignmentType.Implementation, input.Spec.Id, "구현 assignment 생성")
            ]);
    }

    private static RuleOutput EvalArchitectReviewRejected(RuleInput input)
    {
        if (input.Spec.State != FlowState.ArchitectureReview)
            return RejectState(input.Spec.State, input.Event);

        var counters = input.Spec.RetryCounters.Clone();
        counters.ArchitectReviewLoopCount++;

        if (counters.ArchitectReviewLoopCount > MaxRetryCount)
        {
            var effects = new List<SideEffect> { SideEffect.Log("architect review rejected, retry limit exceeded") };
            effects.AddRange(BuildFailActiveAssignmentEffects(input, "retry limit exceeded"));
            effects.Add(SideEffect.CreateReviewRequest(
                "Architect 반려 3회 초과, 사용자 판단 필요", input.Spec.Id));
            return AcceptPhaseTransition(input.Spec, FlowState.Failed, counters, effects);
        }

        return Accept(input.Spec, FlowState.ArchitectureReview, ProcessingStatus.Pending, counters,
            [
                SideEffect.Log("architect review rejected"),
                SideEffect.CreateAssignment(Models.AgentRole.Planner,
                    Models.AssignmentType.Planning, input.Spec.Id, "Planner 보완 assignment 생성")
            ]);
    }

    // ── 구현 ──

    private static RuleOutput EvalImplementationSubmitted(RuleInput input)
    {
        if (input.Spec.State != FlowState.Implementation)
            return RejectState(input.Spec.State, input.Event);

        // implementation 결과 제출 → running assignment는 없어야 정상
        var reject = RejectIfActiveAssignment(input);
        if (reject != null) return reject;

        var counters = ResetAllCounters();
        return AcceptPhaseTransition(input.Spec, FlowState.TestValidation, counters,
            [
                SideEffect.Log("implementation submitted"),
                SideEffect.CreateAssignment(Models.AgentRole.TestValidator,
                    Models.AssignmentType.TestValidation, input.Spec.Id, "Test Validator 입력 생성")
            ]);
    }

    // ── 테스트 검증 ──

    private static RuleOutput EvalTestValidationPassed(RuleInput input)
    {
        if (input.Spec.State != FlowState.TestValidation)
            return RejectState(input.Spec.State, input.Event);

        var reject = RejectIfActiveAssignment(input);
        if (reject != null) return reject;

        var counters = ResetAllCounters();
        return AcceptPhaseTransition(input.Spec, FlowState.Review, counters,
            [
                SideEffect.Log("test validation passed"),
                SideEffect.CreateAssignment(Models.AgentRole.SpecValidator,
                    Models.AssignmentType.SpecValidation, input.Spec.Id, "Spec Validator 입력 생성")
            ]);
    }

    private static RuleOutput EvalTestValidationRejected(RuleInput input)
    {
        if (input.Spec.State != FlowState.TestValidation)
            return RejectState(input.Spec.State, input.Event);

        var reject = RejectIfActiveAssignment(input);
        if (reject != null) return reject;

        // backward transition → counters 유지
        return AcceptPhaseTransition(input.Spec, FlowState.Implementation, null,
            [
                SideEffect.Log("test validation rejected, rework required"),
                SideEffect.CreateAssignment(Models.AgentRole.Developer,
                    Models.AssignmentType.Implementation, input.Spec.Id, "재작업 요청")
            ]);
    }

    // ── 검토 ──

    private static RuleOutput EvalSpecValidationPassed(RuleInput input)
    {
        if (input.Spec.State != FlowState.Review || input.Spec.ProcessingStatus != ProcessingStatus.InReview)
            return RejectState(input.Spec.State, input.Event);

        var reject = RejectIfActiveAssignment(input);
        if (reject != null) return reject;

        var counters = ResetAllCounters();
        return AcceptPhaseTransition(input.Spec, FlowState.Active, counters,
            [
                SideEffect.Log("spec validation passed", "spec_activated")
            ]);
    }

    private static RuleOutput EvalSpecValidationReworkRequested(RuleInput input)
    {
        if (input.Spec.State != FlowState.Review || input.Spec.ProcessingStatus != ProcessingStatus.InReview)
            return RejectState(input.Spec.State, input.Event);

        var counters = input.Spec.RetryCounters.Clone();
        counters.ReworkLoopCount++;

        if (counters.ReworkLoopCount > MaxRetryCount)
        {
            return AcceptPhaseTransition(input.Spec, FlowState.Failed, counters,
                [
                    SideEffect.Log("rework loop limit exceeded"),
                    SideEffect.CreateReviewRequest(
                        "재작업 3회 초과, 사용자 판단 필요", input.Spec.Id)
                ]);
        }

        // backward transition → counters 유지
        return AcceptPhaseTransition(input.Spec, FlowState.Implementation, counters,
            [
                SideEffect.Log("rework requested"),
                SideEffect.CreateAssignment(Models.AgentRole.Developer,
                    Models.AssignmentType.Implementation, input.Spec.Id, "재작업 assignment 재큐잉")
            ]);
    }

    private static RuleOutput EvalSpecValidationUserReviewRequested(RuleInput input)
    {
        if (input.Spec.State != FlowState.Review || input.Spec.ProcessingStatus != ProcessingStatus.InReview)
            return RejectState(input.Spec.State, input.Event);

        var counters = input.Spec.RetryCounters.Clone();
        counters.UserReviewLoopCount++;

        if (counters.UserReviewLoopCount > MaxRetryCount)
        {
            return AcceptPhaseTransition(input.Spec, FlowState.Failed, counters,
                [
                    SideEffect.Log("user review loop limit exceeded"),
                    SideEffect.CreateReviewRequest(
                        "사용자 검토 3회 초과, 판단 필요", input.Spec.Id)
                ]);
        }

        var effects = new List<SideEffect> { SideEffect.Log("user review requested") };

        // 기존 Open RR을 Superseded로 전환
        effects.AddRange(GetOpenReviewRequests(input)
            .Select(r => SideEffect.SupersedeReviewRequest(r.Id, "superseded by new user review request")));

        effects.Add(SideEffect.CreateReviewRequest("사용자 판단 필요", input.Spec.Id));

        return Accept(input.Spec, FlowState.Review, ProcessingStatus.UserReview, counters, effects);
    }

    private static RuleOutput EvalSpecValidationFailed(RuleInput input)
    {
        if (input.Spec.State != FlowState.Review)
            return RejectState(input.Spec.State, input.Event);

        var effects = new List<SideEffect>
        {
            SideEffect.Log("spec validation failed"),
            SideEffect.CreateReviewRequest(
                "terminal failure, 후속 선택지 생성", input.Spec.Id)
        };
        effects.AddRange(BuildCloseOpenReviewRequestEffects(input, "spec validation failed"));

        return AcceptPhaseTransition(input.Spec, FlowState.Failed, null, effects);
    }

    private static RuleOutput EvalUserReviewSubmitted(RuleInput input)
    {
        if (input.Spec.State != FlowState.Review || input.Spec.ProcessingStatus != ProcessingStatus.UserReview)
            return RejectState(input.Spec.State, input.Event);

        return Accept(input.Spec, FlowState.Review, ProcessingStatus.InReview,
            [SideEffect.Log("user review submitted")]);
    }

    private static RuleOutput EvalReviewRequestTimedOut(RuleInput input)
    {
        if (input.Spec.State != FlowState.Review || input.Spec.ProcessingStatus != ProcessingStatus.UserReview)
            return RejectState(input.Spec.State, input.Event);

        var effects = new List<SideEffect> { SideEffect.Log("review request timed out") };
        effects.AddRange(BuildCloseOpenReviewRequestEffects(input, "deadline 초과"));
        effects.Add(SideEffect.CreateReviewRequest(
            "timeout에 의한 실패, 후속 선택지 생성", input.Spec.Id));

        return AcceptPhaseTransition(input.Spec, FlowState.Failed, null, effects);
    }

    // ── 활성/완료 ──

    private static RuleOutput EvalSpecCompleted(RuleInput input)
    {
        if (input.Spec.State != FlowState.Active || input.Spec.ProcessingStatus != ProcessingStatus.Done)
            return RejectState(input.Spec.State, input.Event);

        // 열린 review request 확인
        var openReviews = GetOpenReviewRequests(input);
        if (openReviews.Count > 0)
            return RuleOutput.Reject(RejectionReason.MissingPrecondition,
                [SideEffect.Log($"cannot complete: {openReviews.Count} open review request(s) exist (ids: {string.Join(", ", openReviews.Select(r => r.Id))})")]);

        // active assignment 확인
        var reject = RejectIfActiveAssignment(input);
        if (reject != null) return reject;

        return AcceptPhaseTransition(input.Spec, FlowState.Completed, null,
            [SideEffect.Log("spec completed")]);
    }

    private static RuleOutput EvalRollbackRequested(RuleInput input)
    {
        if (input.Spec.State != FlowState.Active)
            return RejectState(input.Spec.State, input.Event);

        // backward transition → counters 유지
        return AcceptPhaseTransition(input.Spec, FlowState.Review, null,
            [
                SideEffect.Log("rollback requested"),
                SideEffect.CreateReviewRequest(
                    "운영 중 역방향 수정, 재검토 필요", input.Spec.Id)
            ]);
    }

    // ── 취소 ──

    private static RuleOutput EvalCancelRequested(RuleInput input)
    {
        var state = input.Spec.State;

        // 금지: 활성, 실패, 완료, 보관
        if (state is FlowState.Active or FlowState.Failed or FlowState.Completed or FlowState.Archived)
            return RuleOutput.Reject(RejectionReason.ForbiddenTransition,
                [SideEffect.Log($"cancel_requested forbidden in state {state}")]);

        var effects = new List<SideEffect> { SideEffect.Log("cancel requested") };

        // 활성 assignment 취소 (대상 ID 포함)
        if (state is FlowState.ArchitectureReview or FlowState.Implementation or FlowState.TestValidation)
        {
            effects.AddRange(BuildCancelActiveAssignmentEffects(input));
        }

        // 열린 review request 닫기 (대상 ID 포함)
        if (state == FlowState.Review)
        {
            effects.AddRange(BuildCloseOpenReviewRequestEffects(input, "cancel requested"));
        }

        return AcceptPhaseTransition(input.Spec, FlowState.Failed, null, effects);
    }

    /// <summary>
    /// agent 실행 실패 (backend crash, timeout, provisioning failure 등).
    /// CancelRequested와 달리 사용자 의도가 아닌 시스템 실행 오류를 표현한다.
    /// 허용 상태: ArchitectureReview, Implementation, TestValidation, Review.
    /// </summary>
    private static RuleOutput EvalExecutionFailed(RuleInput input)
    {
        var state = input.Spec.State;

        if (state is not (FlowState.ArchitectureReview or FlowState.Implementation
            or FlowState.TestValidation or FlowState.Review))
            return RuleOutput.Reject(RejectionReason.ForbiddenTransition,
                [SideEffect.Log($"execution_failed not applicable in state {state}")]);

        var effects = new List<SideEffect> { SideEffect.Log("execution failed — agent crash or infra error") };
        effects.AddRange(BuildFailActiveAssignmentEffects(input, "execution failed"));

        return AcceptPhaseTransition(input.Spec, FlowState.Failed, null, effects);
    }

    // ── dependency ──

    private static RuleOutput EvalDependencyBlocked(RuleInput input)
    {
        var state = input.Spec.State;
        if (state is FlowState.Completed or FlowState.Failed or FlowState.Archived)
            return RejectState(state, input.Event);

        var effects = new List<SideEffect> { SideEffect.Log("dependency blocked") };

        // in-flight assignment 취소 (flow-state-rule.md §7.2)
        effects.AddRange(BuildCancelActiveAssignmentEffects(input));

        return Accept(input.Spec, state, ProcessingStatus.OnHold, effects);
    }

    private static RuleOutput EvalDependencyFailed(RuleInput input)
    {
        var state = input.Spec.State;
        if (state is FlowState.Completed or FlowState.Failed or FlowState.Archived)
            return RejectState(state, input.Event);

        var effects = new List<SideEffect>
        {
            SideEffect.Log("dependency failed"),
            SideEffect.CreateReviewRequest(
                "upstream dependency 실패", input.Spec.Id)
        };

        // in-flight assignment 취소 (flow-state-rule.md §7.2)
        effects.AddRange(BuildCancelActiveAssignmentEffects(input));

        return Accept(input.Spec, state, ProcessingStatus.OnHold, effects);
    }

    private static RuleOutput EvalDependencyResolved(RuleInput input)
    {
        if (input.Spec.ProcessingStatus != ProcessingStatus.OnHold)
            return RejectState(input.Spec.State, input.Event);

        var restoredStatus = input.Spec.State == FlowState.Review
            ? ProcessingStatus.InReview
            : ProcessingStatus.Pending;

        return Accept(input.Spec, input.Spec.State, restoredStatus,
            [SideEffect.Log("dependency resolved")]);
    }

    // ── timeout/resume ──

    private static RuleOutput EvalAssignmentTimedOut(RuleInput input)
    {
        if (input.Spec.ProcessingStatus != ProcessingStatus.InProgress)
            return RejectState(input.Spec.State, input.Event);

        var effects = new List<SideEffect> { SideEffect.Log("assignment timed out") };
        effects.AddRange(BuildFailActiveAssignmentEffects(input, "heartbeat timeout"));

        return Accept(input.Spec, input.Spec.State, ProcessingStatus.Error, effects);
    }

    private static RuleOutput EvalAssignmentResumed(RuleInput input)
    {
        if (input.Spec.ProcessingStatus != ProcessingStatus.Error)
            return RejectState(input.Spec.State, input.Event);

        return Accept(input.Spec, input.Spec.State, ProcessingStatus.Pending,
            [SideEffect.Log("assignment resumed")]);
    }

    // ── archive ──

    private static RuleOutput EvalSpecArchived(RuleInput input)
    {
        if (input.Spec.State != FlowState.Failed)
            return RejectState(input.Spec.State, input.Event);

        var effects = new List<SideEffect> { SideEffect.Log("spec archived") };
        effects.AddRange(BuildCloseOpenReviewRequestEffects(input, "spec archived"));

        return AcceptPhaseTransition(input.Spec, FlowState.Archived, null, effects);
    }

    // ── helpers ──

    private static readonly Dictionary<(FlowState from, FlowState to), ProcessingStatus> PhaseTransitionInitialStatus = new()
    {
        { (FlowState.Draft, FlowState.Queued), ProcessingStatus.Pending },
        { (FlowState.Queued, FlowState.ArchitectureReview), ProcessingStatus.Pending },
        { (FlowState.Queued, FlowState.Implementation), ProcessingStatus.Pending },
        { (FlowState.ArchitectureReview, FlowState.Implementation), ProcessingStatus.Pending },
        { (FlowState.Implementation, FlowState.TestValidation), ProcessingStatus.Pending },
        { (FlowState.TestValidation, FlowState.Review), ProcessingStatus.InReview },
        { (FlowState.TestValidation, FlowState.Implementation), ProcessingStatus.Pending },
        { (FlowState.Review, FlowState.Implementation), ProcessingStatus.Pending },
        { (FlowState.Review, FlowState.Active), ProcessingStatus.Done },
        { (FlowState.Review, FlowState.Failed), ProcessingStatus.Error },
        { (FlowState.Active, FlowState.Completed), ProcessingStatus.Done },
        { (FlowState.Active, FlowState.Review), ProcessingStatus.InReview },
        { (FlowState.Draft, FlowState.Failed), ProcessingStatus.Error },
        { (FlowState.Queued, FlowState.Failed), ProcessingStatus.Error },
        { (FlowState.ArchitectureReview, FlowState.Failed), ProcessingStatus.Error },
        { (FlowState.Implementation, FlowState.Failed), ProcessingStatus.Error },
        { (FlowState.TestValidation, FlowState.Failed), ProcessingStatus.Error },
        { (FlowState.Failed, FlowState.Archived), ProcessingStatus.Done },
    };

    private static bool IsForwardTransition(FlowState from, FlowState to)
    {
        if (from == FlowState.TestValidation && to == FlowState.Implementation) return false;
        if (from == FlowState.Review && to == FlowState.Implementation) return false;
        if (from == FlowState.Active && to == FlowState.Review) return false;
        return true;
    }

    private static RetryCounters ResetAllCounters()
    {
        return new RetryCounters();
    }

    private static RuleOutput AcceptPhaseTransition(
        SpecSnapshot spec, FlowState newState,
        RetryCounters? counters, IReadOnlyList<SideEffect> sideEffects)
    {
        var key = (spec.State, newState);
        var newProcessingStatus = PhaseTransitionInitialStatus.TryGetValue(key, out var ps)
            ? ps
            : ProcessingStatus.Pending;

        var finalCounters = counters;
        if (finalCounters == null)
        {
            finalCounters = IsForwardTransition(spec.State, newState)
                ? ResetAllCounters()
                : spec.RetryCounters.Clone();
        }

        return new RuleOutput
        {
            Accepted = true,
            Mutation = new StateMutation
            {
                NewState = newState,
                NewProcessingStatus = newProcessingStatus,
                NewRetryCounters = finalCounters,
                NewVersion = spec.Version + 1
            },
            SideEffects = sideEffects
        };
    }

    private static RuleOutput Accept(
        SpecSnapshot spec, FlowState state, ProcessingStatus processingStatus,
        IReadOnlyList<SideEffect> sideEffects)
    {
        return Accept(spec, state, processingStatus, null, sideEffects);
    }

    private static RuleOutput Accept(
        SpecSnapshot spec, FlowState state, ProcessingStatus processingStatus,
        RetryCounters? counters, IReadOnlyList<SideEffect> sideEffects)
    {
        return new RuleOutput
        {
            Accepted = true,
            Mutation = new StateMutation
            {
                NewState = state,
                NewProcessingStatus = processingStatus,
                NewRetryCounters = counters ?? spec.RetryCounters.Clone(),
                NewVersion = spec.Version + 1
            },
            SideEffects = sideEffects
        };
    }

    private static RuleOutput RejectState(FlowState currentState, FlowEvent ev)
    {
        return RuleOutput.Reject(RejectionReason.InvalidStateForEvent,
            [SideEffect.Log($"event {ev} not allowed in state {currentState}")]);
    }
}
