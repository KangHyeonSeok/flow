# Flow Phase 3 Runner 구현 결정 사항

이 문서는 로드맵의 3단계인 `runner skeleton + 더미 agent` 구현 전에 고정해야 할 결정 사항을 정리한 문서다. 목적은 실제 agent 품질이 아니라 orchestration 정확성을 먼저 고정하는 것이다.

이 문서는 아래 문서들을 보완한다.

- `flow-구현-로드맵.md`: 왜 3단계를 먼저 해야 하는지와 전체 순서
- `flow-runner-agent-contract.md`: runner와 agent 사이의 최소 런타임 계약
- `flow-phase2-requirements.md`: 저장 계층, 모델, JSON 직렬화, DependencyEvaluator 설계

즉 이 문서는 "3단계에서 무엇을 만들 것인가"보다 "3단계를 구현하기 전에 무엇을 먼저 결정해야 하는가"에 초점을 둔다.

# 1. 3단계 범위 고정

3단계의 목표는 더미 agent만으로도 runner가 예측 가능한 순서로 spec을 처리하고, 그 근거를 로그와 테스트로 설명할 수 있게 만드는 것이다.

이번 단계에 포함할 것:

- runner loop (`RunOnce` + daemon wrapper)
- 상태 기반 dispatch (`FlowState` × `ProcessingStatus` → `AgentRole`)
- `IAgentAdapter` 인터페이스와 더미 구현체
- rule evaluator 호출과 agent 호출 분기
- side effect 실행기 (`SideEffectExecutor`)
- 구조화된 activity logging (`IActivityStore` 활용)
- 더미 Planner, Architect, Developer, TestValidator, SpecValidator
- fixture spec 세트를 기반으로 한 deterministic simulation
- golden scenario / golden log 테스트

이번 단계에 포함하지 않을 것:

- 실제 LLM prompt 최적화
- 장시간 실행 worker 분리
- webservice 전용 상태 변경 로직
- Slack/외부 채널 입력 처리
- 복잡한 멀티 runner 분산 락 시스템

핵심 원칙:

- runner는 state mutator가 아니라 orchestration coordinator다.
- 상태 판단은 `RuleEvaluator`가 수행한다.
- agent는 event producer이지 상태 전이 엔진이 아니다.
- 사람이 읽는 설명보다 구조화된 결과와 재현 가능한 테스트를 우선한다.

# 2. 선행 조건 (Phase 1/2에서 완료)

3단계의 선행 조건은 모두 Phase 1과 Phase 2에서 구현 완료되었다.

| 선행 조건 | 구현 위치 | 테스트 수 |
| --- | --- | --- |
| 상태 전이 규칙표 | `RuleEvaluator` (24 FlowEvent, EventActorPermissions) | 100 tests |
| ProcessingStatus와 dependency cascade 규칙 | `DependencyEvaluator` (Evaluate + DetectCycles) | 10 tests |
| Spec/Assignment/ReviewRequest/Activity 저장 계약 | `IFlowStore` (ISP 4개 인터페이스 합성) | 13 tests |
| CAS 기반 저장과 충돌 감지 | `FileFlowStore.SaveAsync(spec, expectedVersion)` | 2 tests |
| Fixture 초기화 도구 | `FixtureInitializer` (7 fixture specs) | 8 tests |
| JSON 직렬화 (camelCase, enum, pruning) | `FlowJsonOptions`, `SpecPruner` | 16 tests |
| ID 생성 | `FlowId.New("prefix")` → `"prefix-{8hex}"` | 3 tests |

총 153개 테스트가 통과하는 상태에서 Phase 3를 시작한다.

# 3. 구현 전에 고정할 결정 사항

## 3.1 실행 단위와 loop topology

**결정:**

- 초기 구현은 `단일 프로세스 poll loop`를 기본으로 한다.
- 한 번의 `RunOnce`는 "실행 가능한 spec 집합을 읽고, 정해진 우선순위대로 하나씩 처리"하는 모델로 둔다.
- daemon 모드는 `RunOnce`를 주기적으로 반복하는 thin wrapper로 둔다.
- review 관련 별도 재평가가 필요하면 메인 loop와 분리된 `review sweep`를 둘 수 있지만, 저장 계약과 상태 전이는 같은 `RuleEvaluator`를 사용해야 한다.

**고정 값:**

| 항목 | 값 | 이유 |
| --- | --- | --- |
| `PollIntervalSeconds` | 30 | 더미 agent는 즉시 반환하므로 실제로는 대기 없음. daemon에서만 사용 |
| `MaxSpecsPerCycle` | 10 | 한 `RunOnce`에서 처리할 최대 spec 수. 무한 loop 방지 |
| 빈 결과 처리 | 정상 상태 | `RunOnce`가 처리할 spec이 없으면 정상 반환 (에러 아님) |

## 3.2 dispatch 입력과 우선순위

**결정:**

dispatch는 아래 입력으로 계산한다:

- `spec.State` (`FlowState`)
- `spec.ProcessingStatus` (`ProcessingStatus`)
- active assignment 존재 여부 (`AssignmentStatus.Running` 또는 `Queued`)
- open review request 존재 여부 (`ReviewRequestStatus.Open`)
- retry/cooldown metadata (`RetryCounters`, `Assignment.StartedAt + TimeoutSeconds`)
- `spec.Version`

dispatch 우선순위:

1. **timeout/interrupt 처리** — stale assignment 회수 (`AssignmentTimedOut`), review request deadline 초과 (`ReviewRequestTimedOut`)
2. **dependency cascade** — `DependencyEvaluator.Evaluate()` 결과 적용
3. **rule-only 처리** — `RuleEvaluator`만으로 전이 가능한 경우 (agent 호출 없음)
4. **agent 호출** — dispatch table에 따라 적절한 agent 호출

**dispatch table (구체적 매핑):**

| FlowState | ProcessingStatus | 조건 | Agent | AssignmentType | ProposedEvent |
| --- | --- | --- | --- | --- | --- |
| `Draft` | `Pending` | — | `SpecValidator` | `AcPrecheck` | `AcPrecheckPassed` / `AcPrecheckRejected` |
| `Queued` | `Pending` | `RiskLevel ≥ Medium` | — (rule-only) | — | `AssignmentStarted` → `ArchitectureReview` |
| `Queued` | `Pending` | `RiskLevel = Low` | — (rule-only) | — | `AssignmentStarted` → `Implementation` |
| `ArchitectureReview` | `Pending` | — | `Architect` | `ArchitectureReview` | `ArchitectReviewPassed` / `ArchitectReviewRejected` |
| `ArchitectureReview` | `InProgress` | active assignment | — (대기) | — | — |
| `Implementation` | `Pending` | — | `Developer` | `Implementation` | `ImplementationSubmitted` |
| `Implementation` | `InProgress` | active assignment | — (대기) | — | — |
| `TestValidation` | `Pending` | — | `TestValidator` | `TestValidation` | `TestValidationPassed` / `TestValidationRejected` |
| `TestValidation` | `InProgress` | active assignment | — (대기) | — | — |
| `Review` | `InReview` | — | `SpecValidator` | `SpecValidation` | `SpecValidationPassed` / `SpecValidation*` |
| `Review` | `UserReview` | open review request | — (대기) | — | 사용자 응답 대기 |
| `Review` | `InProgress` | active assignment | — (대기) | — | — |
| `Active` | `Done` | — | — (대기) | — | 사용자 `SpecCompleted` 대기 |

**dispatch에서 제외할 spec:**

- `ProcessingStatus.OnHold` — dependency blocked 상태
- `ProcessingStatus.Error` — terminal failure 상태
- `FlowState.Failed` — 실패 상태
- `FlowState.Completed` — 완료 상태

**backlog 정렬 순서 (낮은 번호가 높은 우선순위):**

1. timeout/interrupt 대상 (stale assignment, deadline 초과)
2. `ProcessingStatus.Pending` && `FlowState` 진행도 높은 순 (Review > TestValidation > Implementation > ArchitectureReview > Queued > Draft)
3. 같은 FlowState 내에서는 `UpdatedAt` 오래된 순 (FIFO)

## 3.3 runner 내부 stage와 FlowState 관계

**결정:**

Phase 1에서 FlowState를 9개 비즈니스 상태로 세분화했으므로, runner 전용 내부 stage는 불필요하다.

| 기존 stage 개념 | Phase 1/2 실제 매핑 |
| --- | --- |
| `implementation` | `FlowState.Implementation` + `ProcessingStatus.InProgress` |
| `test-validation` | `FlowState.TestValidation` + `ProcessingStatus.InProgress` |
| `review` | `FlowState.Review` + `ProcessingStatus.InReview` |

runner는 `FlowState` × `ProcessingStatus` 조합만으로 dispatch를 결정한다. 별도 stage enum을 만들지 않는다.

## 3.4 agent adapter 인터페이스

**결정:**

```csharp
namespace FlowCore.Agents;

/// <summary>Agent adapter 공통 인터페이스</summary>
public interface IAgentAdapter
{
    AgentRole Role { get; }
    Task<AgentOutput> ExecuteAsync(AgentInput input, CancellationToken ct = default);
}

/// <summary>Agent 입력 envelope</summary>
public sealed class AgentInput
{
    public required Spec Spec { get; init; }
    public required Assignment Assignment { get; init; }
    public IReadOnlyList<ActivityEvent> RecentActivity { get; init; } = [];
    public IReadOnlyList<ReviewRequest> OpenReviewRequests { get; init; } = [];

    // context
    public required string ProjectId { get; init; }
    public required string RunId { get; init; }
    public required int CurrentVersion { get; init; }
}

/// <summary>Agent 출력</summary>
public sealed class AgentOutput
{
    public required AgentResult Result { get; init; }
    public required int BaseVersion { get; init; }
    public FlowEvent? ProposedEvent { get; init; }
    public string? Summary { get; init; }
    public string? Message { get; init; }
}
```

**규칙:**

- 모든 agent는 `IAgentAdapter`를 구현한다.
- `AgentOutput.ProposedEvent`는 한 번에 하나만 허용한다 (multi-event 불허).
- `AgentOutput.BaseVersion`은 입력 시점의 `Spec.Version`과 일치해야 한다.
- runner는 반환값을 그대로 신뢰하지 않고, `RuleEvaluator`에 전달하여 검증한다.
- `AgentResult` enum은 이미 Phase 1에서 정의: `Success`, `RetryableFailure`, `TerminalFailure`, `NoOp`.
- `SpecManager`는 독립 agent가 아니라 runner 내부에서 `RuleEvaluator`를 직접 호출하는 형태로 처리한다.

**role별 허용 event (RuleEvaluator.EventActorPermissions에서 도출):**

| AgentRole | ActorKind | 허용 FlowEvent |
| --- | --- | --- |
| `Planner` | `Planner` | `DraftCreated`, `DraftUpdated` |
| `Architect` | `Architect` | `ArchitectReviewPassed`, `ArchitectReviewRejected` |
| `Developer` | `Developer` | `ImplementationSubmitted` |
| `TestValidator` | `TestValidator` | `TestValidationPassed`, `TestValidationRejected` |
| `SpecValidator` | `SpecValidator` | `AcPrecheckPassed`, `AcPrecheckRejected`, `SpecValidationPassed`, `SpecValidationReworkRequested`, `SpecValidationUserReviewRequested`, `SpecValidationFailed` |

## 3.5 동시성 제어 수준

**결정:**

- `FileFlowStore`의 CAS (`SaveAsync(spec, expectedVersion)`)가 이미 구현되어 있다.
- Phase 3에서는 CAS를 1차 방어선으로 사용한다.
- Spec-level lock file은 멀티 runner 환경에서만 필요하며, Phase 3에서는 단일 프로세스이므로 구현하지 않는다.
- Runner 내부에서는 `RunOnce` 단위로 순차 처리하므로 in-process 동시성 문제가 없다.

**CAS 기반 처리 흐름:**

```
1. spec = store.LoadAsync(specId)        — 최신 spec 읽기
2. assignments = store.LoadBySpecAsync(specId)
3. reviewRequests = store.LoadBySpecAsync(specId)
4. dispatch → agent 호출 또는 rule-only 처리
5. ruleOutput = RuleEvaluator.Evaluate(input)  — baseVersion = spec.Version
6. SideEffectExecutor.Execute(ruleOutput.SideEffects)
7. store.SaveAsync(mutatedSpec, expectedVersion: spec.Version)
   → 실패 시 conflict 로그 + skip (다음 cycle에서 재시도)
8. activityStore.AppendAsync(activityEvent)
```

**Phase 3에서 미루는 것:**

- cross-machine lease coordination
- work stealing
- 고급 분산 failover

## 3.6 timeout, retry, cooldown

**고정 값:**

| 항목 | 값 | 근거 |
| --- | --- | --- |
| `DefaultTimeoutSeconds` (전체 agent) | 3600 (1시간) | 더미 agent는 즉시 반환. 실제 agent 교체 시 role별 조정 |
| `MaxRetryCount` | 3 | `RuleEvaluator`에 이미 `MaxRetryCount = 3` 하드코딩 |
| retry backoff | `retryNotBefore = now + (attemptCount * 60)` | 선형 backoff, 초 단위 |
| review deadline 기본값 | 86400 (24시간) | `ReviewRequest.DeadlineAt` 미설정 시 기본값 |
| stale assignment 판정 | `StartedAt + TimeoutSeconds < now` | `Assignment.StartedAt`, `Assignment.TimeoutSeconds` 활용 |

**timeout 처리 흐름:**

```
1. RunOnce 시작 시 모든 active assignment 스캔
2. stale 판정: assignment.StartedAt + assignment.TimeoutSeconds < DateTimeOffset.UtcNow
3. stale이면 RuleEvaluator.Evaluate(AssignmentTimedOut) 호출
4. RuleEvaluator가 반환하는 mutation과 side effects 적용
```

**retry 처리 흐름:**

```
1. agent가 AgentResult.RetryableFailure 반환
2. runner가 RetryCounters 확인
3. retryCount < MaxRetryCount 이면:
   - assignment status → Failed
   - spec ProcessingStatus → Pending (재큐잉)
   - retryCounter 증가
   - retryNotBefore = now + (retryCount * 60)
4. retryCount >= MaxRetryCount 이면:
   - TerminalFailure와 동일하게 처리
   - RuleEvaluator에 SpecValidationFailed 전달
```

## 3.7 구조화된 로그 계약

**이미 완료된 것:**

- `ActivityEvent` 모델: 10 required + 4 optional 필드
- `ActivityAction` enum: 31개 값 (24 FlowEvent 매핑 + 7 log-only)
- `IActivityStore`: `AppendAsync`, `LoadRecentAsync`
- `FileFlowStore`: JSONL append 구현

**Phase 3에서 추가할 것:**

runner 수준의 correlation과 context를 `ActivityEvent`에 기록한다.

| 필드 | 값 | 용도 |
| --- | --- | --- |
| `Actor` | `"runner"` 또는 agent role 이름 | 누가 이벤트를 발생시켰는지 |
| `SourceType` | `"runner"` | Phase 3에서는 항상 runner |
| `CorrelationId` | `runId` (RunOnce 단위 고유 ID) | 한 cycle의 이벤트를 묶는 키 |
| `AssignmentId` | assignment ID | agent 호출과 연관된 assignment |
| `BaseVersion` | spec version at evaluation time | 어떤 버전 기준으로 평가했는지 |

**golden test에서 검증할 이벤트 시퀀스:**

```
SpecSelected → DispatchDecided → AgentInvoked → AgentCompleted →
EventAccepted/EventRejected → StateTransitionCommitted
```

이 시퀀스는 `ActivityAction` enum의 log-only 값들로 매핑된다:

| 시퀀스 단계 | ActivityAction |
| --- | --- |
| SpecSelected | `SpecSelected` |
| DispatchDecided | `DispatchDecided` |
| AgentInvoked | `AgentInvoked` |
| AgentCompleted | `AgentCompleted` |
| EventAccepted | 해당 FlowEvent의 ActivityAction |
| EventRejected | `EventRejected` |
| StateTransitionCommitted | `StateTransitionCommitted` |
| ConflictDetected | `ConflictDetected` |
| RetryScheduled | `RetryScheduled` |

## 3.8 side effect 실행 경계

**이미 완료된 것:**

- `SideEffectKind` enum: `LogActivity`, `CreateAssignment`, `CancelAssignment`, `FailAssignment`, `CreateReviewRequest`, `CloseReviewRequest`
- `SideEffect` 클래스: factory methods, target IDs, payload
- 저장 순서: side effect 파일 → spec 저장(CAS) → activity append

**Phase 3에서 구현할 것: `SideEffectExecutor`**

```csharp
namespace FlowCore.Runner;

public sealed class SideEffectExecutor
{
    private readonly IFlowStore _store;

    public SideEffectExecutor(IFlowStore store) => _store = store;

    public async Task<SideEffectResult> ExecuteAsync(
        IReadOnlyList<SideEffect> effects,
        Spec spec,
        CancellationToken ct = default);
}
```

**실행 순서 (Phase 2 §15.3 기반):**

```
1. CreateAssignment / CreateReviewRequest → 파일 먼저 저장
   - Assignment: FlowId.New("asg"), IAssignmentStore.SaveAsync()
   - ReviewRequest: FlowId.New("rr"), IReviewRequestStore.SaveAsync()
   - 생성된 ID를 spec.Assignments / spec.ReviewRequestIds에 추가

2. CancelAssignment / FailAssignment / CloseReviewRequest → 대상 파일 업데이트
   - Assignment.Status → Cancelled/Failed, FinishedAt = now
   - ReviewRequest.Status → Closed

3. spec 저장 (CAS) → ISpecStore.SaveAsync(spec, expectedVersion)
   - CAS 실패 시: 1단계에서 생성한 side effect 파일 삭제 (rollback)

4. LogActivity → IActivityStore.AppendAsync()
   - activity append 실패: 로그만 남기고 진행 (best-effort)
```

## 3.9 fixture와 더미 agent deterministic 규칙

**이미 완료된 것:**

`FixtureInitializer`가 7개 fixture spec을 생성한다:

| Fixture ID | 초기 상태 | 목적 |
| --- | --- | --- |
| `fixture-happy-path` | `Draft/Pending, Low` | 정상 완료 시나리오 |
| `fixture-architect-review` | `Draft/Pending, Medium` | Architect review 경로 |
| `fixture-review-needed` | `Review/UserReview` + open RR | 사용자 검토 대기 |
| `fixture-dep-upstream` | `Implementation/InProgress` | dependency upstream |
| `fixture-dep-downstream` | `Queued/Pending` + dependsOn | dependency downstream |
| `fixture-stale-assignment` | `Implementation/InProgress` + stale asg | timeout 회수 |
| `fixture-retry-exceeded` | `ArchitectureReview/InProgress` + retryCount=3 | retry 상한 초과 |

**더미 agent deterministic 규칙:**

각 더미 agent는 fixture ID와 상태 조합에 따라 고정 응답을 반환한다.

```csharp
// DummySpecValidator (AC precheck)
// Draft/Pending → AcPrecheckPassed (항상 성공)
return new AgentOutput
{
    Result = AgentResult.Success,
    BaseVersion = input.CurrentVersion,
    ProposedEvent = FlowEvent.AcPrecheckPassed
};

// DummyArchitect
// ArchitectureReview/Pending → ArchitectReviewPassed (항상 통과)
// fixture-retry-exceeded → ArchitectReviewRejected (거부)

// DummyDeveloper
// Implementation/Pending → ImplementationSubmitted (항상 제출)

// DummyTestValidator
// TestValidation/Pending → TestValidationPassed (항상 통과)

// DummySpecValidator (spec validation)
// Review/InReview → SpecValidationPassed (항상 통과)
// fixture-review-needed → SpecValidationUserReviewRequested (사용자 검토 요청)
```

**golden scenario: happy path 전체 흐름**

```
fixture-happy-path:
  Draft/Pending
  → [SpecValidator: AcPrecheckPassed] → Queued/Pending
  → [Runner: AssignmentStarted] → Implementation/InProgress  (Low risk → skip architect)
  → [Developer: ImplementationSubmitted] → TestValidation/Pending
  → [TestValidator: TestValidationPassed] → Review/InReview
  → [SpecValidator: SpecValidationPassed] → Active/Done
```

```
fixture-architect-review:
  Draft/Pending
  → [SpecValidator: AcPrecheckPassed] → Queued/Pending
  → [Runner: AssignmentStarted] → ArchitectureReview/Pending  (Medium risk → architect)
  → [Architect: ArchitectReviewPassed] → Implementation/Pending
  → [Developer: ImplementationSubmitted] → TestValidation/Pending
  → [TestValidator: TestValidationPassed] → Review/InReview
  → [SpecValidator: SpecValidationPassed] → Active/Done
```

```
fixture-stale-assignment:
  Implementation/InProgress + stale assignment
  → [Runner: AssignmentTimedOut] → Implementation/Pending  (assignment cancelled)
  → [Developer: ImplementationSubmitted] → TestValidation/Pending
  → ...
```

## 3.10 runner 설정 모델

```csharp
namespace FlowCore.Runner;

public sealed class RunnerConfig
{
    public int PollIntervalSeconds { get; init; } = 30;
    public int MaxSpecsPerCycle { get; init; } = 10;
    public int DefaultTimeoutSeconds { get; init; } = 3600;
    public int DefaultReviewDeadlineSeconds { get; init; } = 86400;
    public int RetryBackoffBaseSeconds { get; init; } = 60;
}
```

## 3.11 테스트 고정점

Phase 3 테스트는 unit test보다 integration 성격의 runner 시나리오 테스트를 우선한다.

**최소 검증 항목:**

| # | 검증 항목 | 테스트 유형 |
| --- | --- | --- |
| 1 | dispatch가 올바른 agent role을 선택하는지 | unit (dispatch table) |
| 2 | 허용되지 않은 event를 차단하는지 | unit (EventActorPermissions) |
| 3 | version mismatch 결과를 거부하는지 | unit (CAS) |
| 4 | stale assignment recovery가 다음 cycle 평가로 이어지는지 | integration |
| 5 | retryable failure가 cooldown 후 재큐잉되는지 | integration |
| 6 | retry limit 초과 시 terminal failure로 전환되는지 | integration |
| 7 | dependency blocked spec이 dispatch에서 제외되는지 | integration |
| 8 | golden scenario에서 기대 상태에 도달하는지 | golden test |
| 9 | golden log가 기대 ActivityAction 시퀀스를 유지하는지 | golden test |
| 10 | side effect 실행 순서가 올바른지 (파일 → CAS → activity) | integration |

**golden test 구조:**

```csharp
[Fact]
public async Task GoldenScenario_HappyPath()
{
    var store = new FileFlowStore("fixture-project", tempDir);
    var initializer = new FixtureInitializer(store);
    await initializer.InitializeAsync();

    var runner = new FlowRunner(store, dummyAgents, config);

    // RunOnce를 반복하여 fixture-happy-path가 Active/Done에 도달할 때까지 실행
    for (int i = 0; i < 10; i++)
        await runner.RunOnceAsync();

    var spec = await store.LoadAsync("fixture-happy-path");
    spec!.State.Should().Be(FlowState.Active);
    spec.ProcessingStatus.Should().Be(ProcessingStatus.Done);

    // activity log 시퀀스 검증
    var activities = await store.LoadRecentAsync("fixture-happy-path", 100);
    // 역순이므로 Reverse
    var actions = activities.Reverse().Select(a => a.Action).ToList();
    actions.Should().ContainInOrder(
        ActivityAction.AcPrecheckPassed,
        ActivityAction.AssignmentStarted,
        ActivityAction.ImplementationSubmitted,
        ActivityAction.TestValidationPassed,
        ActivityAction.SpecValidationPassed
    );
}
```

# 4. 3단계에서 미루는 결정 사항

아래는 중요하지만 3단계에서 확정하지 않아도 되는 항목이다.

- 실제 prompt 내용과 system prompt tuning
- 역할별 대형 context packing 전략
- webservice UI에서의 inbox/kanban 표현 방식
- Slack action payload 세부 포맷
- Docker 배포용 process supervision
- 장기 작업 heartbeat push 모델
- spec-level lock file (멀티 runner 전용)

이 항목들을 3단계에 끌어오면 orchestration 디버깅과 agent 품질 디버깅이 섞인다.

# 5. 구현 순서

1. `IAgentAdapter` 인터페이스와 `AgentInput`/`AgentOutput` 모델 정의
2. `RunnerConfig` 설정 모델 추가
3. dispatch table 구현 (`FlowState` × `ProcessingStatus` → `AgentRole?`)
4. `SideEffectExecutor` 구현 (Phase 2 저장 순서 기반)
5. `FlowRunner.RunOnceAsync()` 구현:
   - spec 후보 조회 → timeout/interrupt 검사 → dispatch → agent/rule 호출 → side effect 실행 → CAS 저장 → activity 기록
6. 더미 agent 5종 구현 (deterministic 규칙)
7. golden scenario 테스트: happy path, architect review, stale assignment
8. golden log 테스트: ActivityAction 시퀀스 검증
9. daemon wrapper (`RunDaemonAsync` = loop + poll interval + CancellationToken)

# 6. 완료 기준

3단계가 끝났다고 말하려면 아래가 충족되어야 한다.

- 더미 agent만으로 runner loop가 끊기지 않고 동작한다.
- 같은 fixture에 대해 호출 순서와 activity log 시퀀스가 재현 가능하다.
- timeout, retry, conflict, dependency blocked 경로가 테스트로 고정되어 있다.
- review 단계 진입과 재큐잉 경로가 `RuleEvaluator`를 우회하지 않는다.
- 5단계에서 실제 agent를 붙일 때 `IAgentAdapter`를 교체하는 것만으로 충분하다.
- Phase 1/2의 153개 기존 테스트가 모두 통과한다.

# 7. 산출물 요약

| 파일 | 설명 |
| --- | --- |
| `Agents/IAgentAdapter.cs` | agent adapter 인터페이스 + AgentInput/AgentOutput |
| `Agents/Dummy/Dummy*.cs` | 더미 agent 5종 |
| `Runner/FlowRunner.cs` | RunOnce + dispatch + orchestration |
| `Runner/SideEffectExecutor.cs` | side effect 실행기 |
| `Runner/RunnerConfig.cs` | 설정 모델 |
| `Runner/DispatchTable.cs` | (FlowState, ProcessingStatus) → AgentRole? 매핑 |
| `Tests/FlowRunnerTests.cs` | dispatch, timeout, retry, conflict 테스트 |
| `Tests/GoldenScenarioTests.cs` | fixture 기반 end-to-end 시나리오 |
| `Tests/SideEffectExecutorTests.cs` | 실행 순서, rollback 테스트 |
