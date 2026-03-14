# Flow 2단계 구현 요구사항

이 문서는 2단계(Flow Core) 구현을 시작하기 전에 확정해야 하는 결정사항을 정리한 문서다. 1단계에서 상태 전이 규칙(RuleEvaluator)이 완성되었으므로, 2단계는 그 규칙을 실행할 저장/도구 계층을 만드는 단계다.

# 1. 2단계 범위

로드맵(flow-구현-로드맵.md §2.2) 기준 구현 범위:

- spec JSON load/save
- 빈 필드 제외 로직
- activity log append
- review request load/save
- assignment/lock metadata load/save
- fixture spec 초기화 도구
- 상태 규칙 테스트용 helper

완료 기준:

- runner와 webservice가 공통으로 호출할 저장 API가 준비된다.
- fixture spec 세트를 명령 하나로 초기화할 수 있다.

# 2. 기존 시스템과의 관계

## 2.1 문제

기존 `flow-cli`는 이미 `SpecStore`(load/save), `SpecStateStore`(volatile state), `RunnerLogService`(activity log), `RunnerService`(orchestration)를 갖고 있다. 새 `flow-core`가 같은 기능을 다시 만들면 중복이 발생한다.

## 2.2 결정: 선택지 A 변형, 추상화 우선 점진 이전

권장 전략은 기존 flow-cli 로직을 무조건 재사용하거나 무조건 폐기하는 양극단이 아니라, 추상화 인터페이스를 먼저 만들고 점진적으로 이전하는 방식이다.

결정:

- `flow-core`는 새 스키마와 새 저장 계약의 기준 구현이 된다.
- 기존 `flow-cli`는 즉시 제거하지 않고, 점진적으로 `flow-core` 인터페이스를 참조하도록 바꾼다.
- 기존 구현의 JSON 옵션, 파일 처리 패턴, 에러 처리 방식은 참고하되, 새 스키마에 맞춰 재구성한다.
- runtime compatibility layer는 두지 않는다. 새 `flow-core`는 새 포맷만 읽고 쓴다.
- 기존 데이터가 필요하면 나중에 one-off migration tool로 옮긴다. 런타임에서 old/new 포맷을 동시에 지원하지는 않는다.

이유:

- 코드 중복을 줄이면서도 새 모델의 정합성을 지킬 수 있다.
- `flow-cli`, runner, webservice가 같은 core abstraction을 공유할 수 있다.
- 기존 spec 포맷과 새 spec 포맷은 상태 모델이 달라서 런타임 호환 계층을 두는 비용이 너무 크다.

전환 순서:

1. `flow-core`에 저장 인터페이스와 공통 JSON 계약을 만든다.
2. 새 spec 저장소 구현을 `flow-core`에 둔다.
3. runner와 새 webservice가 먼저 `flow-core`를 사용한다.
4. 기존 `flow-cli` 명령을 순차적으로 `flow-core` 기반으로 재작성한다.

내 의견:

- 이 단계에서는 "기존 시스템 유지"보다 "새 코어의 기준 인터페이스 수립"이 더 중요하다.
- 기존 구현은 참고 자료이자 전환 대상이지, 새 스키마의 권위가 되어서는 안 된다.

# 3. 저장 디렉토리 구조

## 3.1 문제

기존 flow-cli는 `~/.flow/{project}/specs/` 아래에 `F-NNN.json` 파일을 저장한다. 새 문서(flow-schema.md)에서는 `spec.json`, `activity/*.jsonl`, 별도 assignment/review request를 언급하지만, 구체적인 디렉토리 구조가 확정되어 있지 않다.

## 3.2 결정: Shared Home 중앙 저장 + spec 단위 디렉토리 방식

Authoritative Source는 반드시 Shared Home에 둔다. Local Workspace에는 소스 코드와 `flow.exe`만 있고, 스펙 상태나 락 정보는 두지 않는다.

Shared Home 루트 경로: `~/.flow/` (환경 변수 `FLOW_HOME`으로 override 가능)

권장 구조:

```
~/.flow/projects/
  <project-id>/
    specs/
      <spec-id>/
        spec.json
        activity/
          2026-03-14.jsonl
        assignments/
          asg-a1b2c3d4.json
        review-requests/
          rr-e5f6a7b8.json
```

결정:

- spec, activity, assignments, review requests는 모두 Shared Home 중앙 저장소에 둔다.
- Local Workspace는 상태 저장소가 아니라 작업 공간이다.
- spec-assignment-review request 관계는 ID 기반 참조를 사용한다.
- activity는 spec별 디렉토리 아래 날짜별 JSONL 파일로 저장한다.

이유:

- 다수의 agent와 webservice가 같은 중앙 상태를 바라볼 수 있다.
- spec 단위 디렉토리는 백업, 삭제, 복구, 락 처리, fixture 초기화가 단순하다.
- activity를 spec별로 분리하면 최근 activity 조회가 쉽고, 전체 날짜별 단일 파일의 폭증을 피할 수 있다.
- assignment/review request를 별도 파일로 두면 spec.json이 지나치게 커지지 않는다.

추가 규칙:

- `spec.json`은 현재 상태와 주요 참조를 담는다.
- assignment와 review request의 식별자는 spec.json에 reference로도 남길 수 있지만, authoritative body는 각 파일이다.
- 락 메타데이터도 Local Workspace가 아니라 Shared Home 기준으로 관리한다.

# 4. 전체 Spec 모델

## 4.1 문제

1단계의 `SpecSnapshot`은 rule evaluator 입력용 최소 모델이다. 2단계에서는 `flow-schema.md`의 전체 spec 필드를 지원하는 모델이 필요하다.

## 4.2 결정

flow-schema.md 기준 전체 spec 필드:

```
id, projectId, title, type, problem, goal,
acceptanceCriteria[], state, processingStatus,
riskLevel, dependencies { dependsOn[], blocks[] },
assignments[], reviewRequestIds[], testIds[], tests[],
retryCounters, createdAt, updatedAt, version
```

결정:

- `SpecSnapshot`은 rule evaluator 전용 최소 모델로 유지한다.
- 2단계에서는 별도 `Spec` aggregate 모델을 만든다.
- `Spec -> SpecSnapshot` 변환은 `flow-core` 내부에서 수행한다. 저장 계층 또는 facade가 rule evaluator 호출 직전에 변환하는 것이 맞다.
- `acceptanceCriteria`와 `tests`는 2단계부터 `flow-core` 모델에 포함한다. 다만 rule evaluator는 이들 전체를 읽지 않고 snapshot으로 축약된 정보만 본다.

`assignments` 필드 명명 규칙:

- spec.json의 필드명은 `assignments` (flow-schema.md §2.1, §2.2 기준)이며, 값은 assignment ID 문자열 배열이다.
- `assignmentIds`로 이름을 바꾸지 않는다. flow-schema.md가 authoritative source이고, 이미 `reviewRequestIds`와 `testIds`가 `Ids` 접미사를 쓰는 것과 달리 `assignments`는 접미사 없이 쓴다. 이 비대칭은 flow-schema.md의 원래 설계를 따른다.
- C# 모델의 프로퍼티명도 `Assignments`로 통일한다. JSON 직렬화 시 camelCase 변환에 의해 `assignments`가 된다.

이유:

- rule evaluator와 저장 모델의 책임이 다르다.
- 전체 spec 모델이 없으면 webservice와 runner가 각자 별도 모델을 만들게 된다.
- AC와 test는 3단계에서만 쓰이는 정보가 아니라, 2단계 저장 계약 자체의 일부다.

우선순위:

- P0: `Spec`, `Dependency`, `ActivityEvent`
- P1: `Assignment` 상세 필드, `ReviewRequest` 상세 필드
- P1: `AcceptanceCriterion`, `TestDefinition` 최소 모델
- P2: AC/test의 고급 validation 로직

## 4.3 AcceptanceCriteria 모델

flow-schema.md 기준:

```json
{
  "id": "ac-001",
  "text": "비활성 상태 버튼은 회색 배경과 비활성 커서를 표시한다.",
  "testable": true,
  "notes": "UI snapshot 또는 style assertion으로 검증 가능",
  "relatedTestIds": ["test-001"]
}
```

## 4.4 Test 모델

flow-schema.md 기준:

```json
{
  "id": "test-001",
  "type": "unit",
  "title": "비활성 버튼 스타일 검증",
  "acIds": ["ac-001"],
  "status": "not-run"
}
```

결정: test는 2단계에서는 spec.json 안에 inline 저장한다.

이유:

- AC와 test의 연결을 spec 한 파일 안에서 바로 볼 수 있다.
- 테스트 정의는 spec의 검증 계약 일부라서 spec 본문에 함께 있는 것이 자연스럽다.
- activity처럼 무한히 커지는 데이터가 아니므로 inline 저장 부담이 상대적으로 작다.

# 5. Activity Log 저장 계약

## 5.1 문제

기존 flow-cli는 activity를 spec.json의 `activity` 배열에 inline 저장한다. 새 문서(flow-schema.md §8)는 `activity/<date>.jsonl` 파일에 한 줄씩 append하는 방식을 정의한다.

## 5.2 결정: JSONL append-only, spec별 날짜 파일

결정:

- activity의 primary storage는 JSONL append-only다.
- 파일은 spec별 디렉토리 아래 날짜별 JSONL로 둔다.
- spec.json에는 activity를 inline 저장하지 않는다.
- runner가 agent에 recent activity를 전달할 때는 JSONL에서 최근 window만 읽고, 필요하면 별도 summary를 계산해 함께 준다.

이유:

- activity는 audit/replay/debug 용도라 append-only 구조가 맞다.
- spec별 분리는 조회와 보관 전략이 단순하다.
- 전체 event stream을 agent 입력으로 넣는 것은 context explosion을 만든다.

추가 규칙:

- recent activity 조회는 기본적으로 최근 N개 window만 반환한다.
- 고빈도 event가 늘어나면 나중에 snapshot/summary 캐시를 추가할 수 있다.

## 5.3 Activity Event 모델

flow-schema.md 기준 최소 필드:

```
eventId, timestamp, specId, actor, action, sourceType,
baseVersion, state, processingStatus, message
```

권장 필드:

```
assignmentId, reviewRequestId, correlationId, payload
```

`action` 필드의 허용 값을 enum으로 고정할지, string으로 자유롭게 둘지 결정이 필요하다.

결정: `action`은 `FlowEvent`와 완전히 동일하지 않고, 로그 전용 값을 포함하는 별도 `ActivityAction` enum으로 관리한다.

`ActivityAction` enum 값:

- FlowEvent 1:1 매핑 (24개): `DraftCreated`, `DraftUpdated`, `AcPrecheckPassed`, `AcPrecheckRejected`, `ArchitectReviewPassed`, `ArchitectReviewRejected`, `AssignmentStarted`, `ImplementationSubmitted`, `TestValidationPassed`, `TestValidationRejected`, `SpecValidationPassed`, `SpecValidationRejected`, `UserReviewSubmitted`, `SpecCompleted`, `CancelRequested`, `DependencyBlocked`, `DependencyFailed`, `DependencyResolved`, `AssignmentTimedOut`, `AssignmentResumed`, `ReviewRequestTimedOut`, `RollbackRequested`, `RetryLimitExceeded`, `ReviewRequestCreated`
- 로그 전용 값: `SpecActivated`, `SpecFailed`, `AssignmentCancelled`, `AssignmentFailed`, `ReviewRequestClosed`, `CounterReset`, `ManualOverride`

예:

- 입력 이벤트: `SpecValidationPassed` → 로그 action: `SpecActivated`
- 입력 이벤트: `CancelRequested` → 로그 action: `SpecFailed`, `AssignmentCancelled`

이유:

- 로그는 전이 결과를 표현해야 할 때가 있다.
- 이벤트와 로그 액션을 완전히 동일시하면 운영 마커를 남기기 어렵다.

# 6. Assignment 저장과 생명주기

## 6.1 문제

1단계의 `Assignment` 모델은 ID, specId, role, type, status만 가진 최소 모델이다. flow-schema.md는 startedAt, lastHeartbeatAt, timeoutSeconds, finishedAt, resultSummary, cancelReason, worktree까지 정의한다. 2단계에서 이 필드들을 어디까지 구현할지 결정해야 한다.

## 6.2 결정

결정:

- assignment는 별도 파일로 관리한다.
- 위치는 spec별 디렉토리 아래 `assignments/`다.
- 2단계에서는 schema의 핵심 필드를 모델에 포함한다.
- heartbeat 실시간 갱신 로직 자체는 3단계 runner로 미루되, 저장 필드는 2단계부터 둔다.
- worktree 정보는 assignment 모델에 포함하되 optional 필드로 둔다.

2단계 필수 필드:

- `id`
- `specId`
- `agentRole`
- `type`
- `status`
- `startedAt`
- `lastHeartbeatAt`
- `timeoutSeconds`
- `finishedAt`
- `resultSummary`
- `cancelReason`
- `worktree`

이유:

- 나중에 필드를 추가하면 저장 포맷이 다시 흔들린다.
- heartbeat 실행 주체는 나중에 붙여도, 저장 필드 자체는 지금부터 있어야 한다.

# 7. Review Request 저장과 생명주기

## 7.1 문제

1단계에서 ReviewRequest 모델은 확장되었지만, review request의 options(선택지 객체)는 모델에 없다. flow-schema.md §6에서는 options 배열을 정의한다.

## 7.2 결정

```json
{
  "options": [
    { "id": "opt-a", "label": "안 A", "description": "진한 회색 배경 + not-allowed cursor" },
    { "id": "opt-b", "label": "안 B", "description": "밝은 회색 배경 + pointer 유지" }
  ]
}
```

결정:

- review request의 `options` 모델은 `flow-core`에 포함한다.
- review request는 별도 파일로 관리한다.
- 위치는 spec별 디렉토리 아래 `review-requests/`다.
- `editedPayload`는 `JsonElement?` 타입으로 저장한다. 해석은 상위 계층에서 수행한다 (구체 직렬화 전략은 §14.6 참조).

이유:

- review request는 webservice 전용 데이터가 아니라 runner와 validator가 함께 읽는 운영 객체다.
- partial-edit-approve의 payload 구조는 아직 자주 바뀔 수 있으므로 엄격한 typed object보다 raw payload가 안전하다.

# 8. JSON 직렬화 계약

## 8.1 문제

기존 flow-cli는 아래 JSON 옵션을 사용한다:

```csharp
WriteOptions = new()
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
```

새 flow-core도 같은 옵션을 사용할지, 더 엄격한 옵션을 사용할지 결정해야 한다.

## 8.2 결정

결정:

- JSON 필드 네이밍은 camelCase 정책을 공통 옵션으로 적용한다.
- enum 저장 값은 영어를 사용한다.
- UI에서만 한국어 label을 렌더링한다.
- null 필드는 제거한다.
- 빈 배열과 all-zero `retryCounters` 제거는 serializer 옵션이 아니라 저장 계층의 pruning pass에서 수행한다.
- `UnsafeRelaxedJsonEscaping`은 유지한다.
- 날짜 형식은 ISO8601 UTC를 강제한다.

## 8.3 Enum 직렬화 관련 추가 사항

flow-schema.md의 예시에서 state는 `"구현"`, processingStatus는 `"처리중"`으로 한국어를 사용한다. 하지만 C# enum은 영어(`Implementation`, `InProgress`)로 정의되어 있다.

결정: JSON 저장 값은 영어로 통일한다. enum 직렬화 케이스는 **camelCase**를 사용한다 (`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`).

예:

- `state: "implementation"`
- `processingStatus: "inProgress"`
- `riskLevel: "high"`
- `assignmentStatus: "running"`

이유:

- C# enum과 저장 계약이 안정적으로 매핑된다.
- custom converter 복잡도를 줄일 수 있다.
- UI label만 한국어로 두면 문서 표현과 저장 표현을 분리할 수 있다.

# 9. 빈 필드 제외 규칙

## 9.1 문제

flow-schema.md §2.4에서 비워도 되는 필드를 정의한다:

- `dependencies.dependsOn` (빈 배열)
- `dependencies.blocks` (빈 배열)
- `assignments` (빈 배열)
- `reviewRequestIds` (빈 배열)
- `testIds` (빈 배열)
- `retryCounters` (모든 값이 0)

## 9.2 결정

결정:

- 빈 배열은 저장 시 제거한다.
- `retryCounters`가 모두 0이면 필드 자체를 제거한다.
- pruning은 저장 계층에서 수행한다.
- 읽기 시 빠진 필드는 기본값으로 채운다.

이유:

- serializer 옵션만으로는 빈 배열과 0값 묶음 제거를 깔끔하게 제어하기 어렵다.
- 저장 계층에서 pruning 하면 문서 계약과 실제 JSON 결과를 맞추기 쉽다.

# 10. 동시성 제어

## 10.1 문제

flow-schema.md와 flow-runner-agent-contract.md에서 spec-level lock과 version 기반 CAS를 정의한다. 2단계에서 이를 어디까지 구현할지 결정해야 한다.

## 10.2 결정

결정:

- 2단계에서는 optimistic concurrency를 필수로 지원한다.
- 저장 계층이 `expectedVersion`을 받아 자동으로 CAS를 검증한다.
- conflict 같은 예상 가능한 저장 실패는 결과 객체로 반환한다.
- 예기치 않은 I/O 실패만 예외를 throw한다.
- spec-level lock 파일은 3단계 runner에서 추가할 수 있지만, 2단계 핵심은 CAS다.

핵심 데이터 흐름:

1. Read: caller가 spec과 현재 `version`을 읽는다.
2. Evaluate: RuleEvaluator가 mutation과 side effect 목록을 계산한다.
3. Write: `SaveSpec(spec, expectedVersion)`이 현재 파일의 version과 비교한다.
4. 충돌 시 `SaveResult.Conflict`를 반환한다.

이유:

- 2단계의 핵심은 저장 정합성이지 멀티 runner scheduling 완성이 아니다.
- CAS만으로도 stale write 대부분을 막을 수 있다.

## 10.3 Assignment/ReviewRequest 동시성 제어

문제: Spec에는 `version` 기반 CAS가 있지만, Assignment와 ReviewRequest에는 별도 동시성 제어가 없다.

결정: **Spec-level CAS에 의존한다.**

- Assignment와 ReviewRequest의 상태 변경은 모두 RuleEvaluator의 side effect를 통해 발생한다.
- Side effect 실행 시점에 이미 spec의 CAS를 통과한 상태이므로, assignment/reviewRequest에 별도 version을 두지 않는다.
- Runner가 직접 assignment를 갱신하는 케이스(heartbeat 등)는 3단계 범위다. 3단계에서 멀티 runner를 지원할 때 assignment에 version 필드를 추가한다.

이유:

- 현재 설계에서 assignment/reviewRequest 변경 경로는 모두 spec 전이의 side effect다.
- 2단계에서 불필요한 version 관리를 추가하면 저장/로드 복잡도만 증가한다.
- 3단계에서 heartbeat 등 직접 갱신이 필요해지면 그때 CAS를 확장한다.

# 11. ID 생성 전략

## 11.1 문제

spec, assignment, review request, activity event 모두 고유 ID가 필요하다. 생성 전략을 고정해야 한다.

## 11.2 결정

결정:

- spec ID는 기존 `F-NNN`을 유지하지 않고 새 포맷으로 간다.
- 모든 ID는 `prefix + UUID` 전략을 사용한다.
- UUID는 8자리 hex (UUID v4의 앞 8자리)를 기본으로 사용한다. 충돌 확률이 문제가 되면 12자리로 확장한다.
- 생성 함수: `FlowId.New("spec")` → `"spec-3f6f2d1c"`

예:

- `spec-3f6f2d1c`
- `asg-7a1bc902`
- `rr-8de91f11`
- `evt-4c1d0baf`

이유:

- 중앙 카운터 파일 없이도 유일성을 보장하기 쉽다.
- Shared Home 멀티 에이전트 환경에서 순번 기반 ID는 충돌 관리가 번거롭다.

# 12. Dependency 그래프

## 12.1 문제

flow-schema.md에서 spec.dependencies는 `{ dependsOn: [], blocks: [] }`로 정의된다. 1단계 RuleEvaluator는 `dependency_blocked/failed/resolved` 이벤트를 처리하지만, 의존성 그래프 자체를 조회하거나 cascade를 계산하는 로직은 없다.

## 12.2 결정

결정:

- dependency cascade 계산 로직은 `flow-core`에 포함한다.
- `DependencyEvaluator`를 별도 순수 함수로 둔다.
- cycle detection은 2단계부터 기본 검증으로 포함한다.

## 12.3 DependencyEvaluator 입출력 계약

```csharp
public static class DependencyEvaluator
{
    /// <summary>
    /// 변경된 spec의 상태를 기반으로, downstream spec들에 발생할 이벤트를 계산한다.
    /// </summary>
    public static IReadOnlyList<DependencyEffect> Evaluate(DependencyInput input);

    /// <summary>전체 spec 그래프에서 cycle을 검출한다.</summary>
    public static IReadOnlyList<DependencyCycle> DetectCycles(
        IReadOnlyList<(string SpecId, IReadOnlyList<string> DependsOn)> graph);
}

public sealed class DependencyInput
{
    /// <summary>상태가 변경된 spec</summary>
    public required SpecSnapshot ChangedSpec { get; init; }

    /// <summary>변경 전 상태</summary>
    public required FlowState PreviousState { get; init; }

    /// <summary>ChangedSpec을 dependsOn으로 참조하는 downstream spec 목록</summary>
    public required IReadOnlyList<SpecSnapshot> DownstreamSpecs { get; init; }
}

public sealed class DependencyEffect
{
    /// <summary>이벤트를 받을 downstream spec ID</summary>
    public required string TargetSpecId { get; init; }

    /// <summary>발생할 이벤트 (DependencyBlocked, DependencyFailed, DependencyResolved)</summary>
    public required FlowEvent Event { get; init; }
}

public sealed class DependencyCycle
{
    /// <summary>cycle에 포함된 spec ID 목록 (순서대로)</summary>
    public required IReadOnlyList<string> SpecIds { get; init; }
}
```

동작 규칙:

DependencyEvaluator는 비즈니스 상태(`FlowState`)와 처리 상태(`ProcessingStatus`)의 조합으로 판단한다.

- ChangedSpec의 `FlowState`가 `Failed`로 전이 → downstream에 `DependencyFailed` 발행
- ChangedSpec의 `ProcessingStatus`가 `OnHold` 또는 `Error`로 전이 → downstream에 `DependencyBlocked` 발행
- ChangedSpec의 `ProcessingStatus`가 `OnHold`/`Error`에서 정상(`Pending`/`InProgress`/`InReview`/`Done`)으로 복귀 → downstream 중 `ProcessingStatus`가 `OnHold`인 spec에 `DependencyResolved` 발행
- Cycle detection: spec 생성/수정 시 `DetectCycles()`로 사전 검증. cycle이 발견되면 저장을 거부한다.

참고: `OnHold`와 `Error`는 `ProcessingStatus` enum 값이다. `Failed`는 `FlowState` enum 값이다. 비즈니스 상태와 처리 상태를 혼동하지 않도록 주의한다.

이유:

- dependency 판단을 runner에 두면 규칙이 분산된다.
- 순수 함수로 두면 테스트가 쉽고 runner/webservice가 함께 쓸 수 있다.

# 13. Fixture 초기화 도구

## 13.1 문제

로드맵에서 fixture spec 초기화 도구를 2단계 범위에 포함한다. 3단계(runner skeleton)에서 사용할 고정 fixture를 미리 만들어야 한다.

## 13.2 결정

필요 fixture (로드맵 §4.1 기준):

- 정상 완료되는 단순 spec (low risk)
- Architect review가 필요한 spec (medium+ risk)
- review request가 한 번 필요한 spec
- dependency failure로 `보류` 전파가 필요한 spec 세트 (upstream + downstream)
- stale assignment 회수가 필요한 spec
- 3회 초과 실패하는 spec

결정:

- fixture는 C# 코드로 생성하되, 결과는 실제 JSON 파일로 써 넣는다.
- 테스트 setup과 CLI 초기화 양쪽에서 재사용 가능한 builder를 둔다.
- fixture ID는 `fixture-<name>` 형식을 사용한다.

예:

- `fixture-happy-path`
- `fixture-review-timeout`
- `fixture-dependency-upstream`

이유:

- 코드 생성은 유지보수가 쉽고, 파일 출력은 실제 저장 계약 테스트에 유리하다.

# 14. 저장 API 설계

## 14.1 문제

runner, webservice, CLI가 공통으로 사용할 저장 API의 인터페이스를 결정해야 한다.

## 14.2 SaveResult 정의

저장 결과는 단순 성공/실패를 넘어, 낙관적 잠금 충돌 상황을 호출자가 명확히 인지하고 재시도 여부를 결정할 수 있어야 한다.

```csharp
public enum SaveStatus
{
    Success,          // 저장 완료
    Conflict,         // 버전 불일치 (다른 프로세스가 이미 수정함)
    ValidationError,  // 스키마 또는 비즈니스 규칙 위반
    IOError           // 파일 시스템 권한, 용량 등
}

public sealed class SaveResult
{
    public required SaveStatus Status { get; init; }
    public int? CurrentVersion { get; init; }   // Conflict 시 현재 파일의 버전
    public string? Message { get; init; }       // 사람이 읽을 수 있는 오류 설명
    public bool IsSuccess => Status == SaveStatus.Success;

    public static SaveResult Ok() => new() { Status = SaveStatus.Success };
    public static SaveResult ConflictAt(int currentVersion) =>
        new() { Status = SaveStatus.Conflict, CurrentVersion = currentVersion };
}
```

설계 노트:

- `Conflict`는 spec CAS에서 발생한다. assignment/reviewRequest는 §10.3 결정에 따라 spec-level CAS에 의존하므로 별도 conflict가 없다.
- `ValidationError`는 pruning/직렬화 단계에서 필수 필드 누락 등을 감지할 때 사용한다.
- `IOError`는 §15.3 실패 복구 규칙에서 예외 throw 대신 결과 객체로 반환하는 선택지를 열어둔다. 다만 2단계에서는 예상 불가능한 I/O 실패는 예외를 throw하는 것을 기본으로 하고, `IOError`는 예비로 둔다.

## 14.3 인터페이스 분리 전략

결정: **도메인별 분리 인터페이스** + 선택적 통합 인터페이스

```csharp
/// <summary>Spec 저장소. 생성 시 projectId를 고정한다.</summary>
public interface ISpecStore
{
    Task<Spec?> LoadAsync(string specId, CancellationToken ct = default);
    Task<IReadOnlyList<Spec>> LoadAllAsync(CancellationToken ct = default);
    Task<SaveResult> SaveAsync(Spec spec, int expectedVersion, CancellationToken ct = default);
}

/// <summary>Assignment 저장소. 경로는 assignment.SpecId로 결정한다.</summary>
public interface IAssignmentStore
{
    Task<Assignment?> LoadAsync(string specId, string assignmentId, CancellationToken ct = default);
    Task<IReadOnlyList<Assignment>> LoadBySpecAsync(string specId, CancellationToken ct = default);
    Task<SaveResult> SaveAsync(Assignment assignment, CancellationToken ct = default);
}

/// <summary>ReviewRequest 저장소. 경로는 reviewRequest.SpecId로 결정한다.</summary>
public interface IReviewRequestStore
{
    Task<ReviewRequest?> LoadAsync(string specId, string reviewRequestId, CancellationToken ct = default);
    Task<IReadOnlyList<ReviewRequest>> LoadBySpecAsync(string specId, CancellationToken ct = default);
    Task<SaveResult> SaveAsync(ReviewRequest reviewRequest, CancellationToken ct = default);
}

/// <summary>Activity 저장소. append-only.</summary>
public interface IActivityStore
{
    Task AppendAsync(ActivityEvent activityEvent, CancellationToken ct = default);
    Task<IReadOnlyList<ActivityEvent>> LoadRecentAsync(string specId, int maxCount, CancellationToken ct = default);
}

/// <summary>통합 접근점. DI 편의용. runner는 이것 하나만 주입받으면 된다.</summary>
public interface IFlowStore : ISpecStore, IAssignmentStore, IReviewRequestStore, IActivityStore { }
```

이유:

- ISP(인터페이스 분리 원칙): 웹 대시보드는 `IActivityStore`만, fixture 도구는 `ISpecStore`만 참조하면 된다.
- runner는 `IFlowStore` 하나로 모든 저장소에 접근한다.
- 구현 클래스는 `FileFlowStore` 하나로 4개 인터페이스를 모두 구현한다.

## 14.4 ProjectId 범위

결정: 저장소 인스턴스 생성 시 **projectId를 생성자 파라미터로 고정**한다.

```csharp
var store = new FileFlowStore(projectId: "proj-001", flowHome: "~/.flow");
// 내부 기본 경로: ~/.flow/projects/proj-001/specs/...
```

- 모든 Load/Save 메서드는 생성 시 고정된 projectId 범위 안에서만 동작한다.
- 메서드마다 projectId를 넘기는 번거로움을 없앤다.
- 잘못된 프로젝트의 spec을 건드리는 실수를 원천 차단한다.

## 14.5 Save 메서드의 specId 의존성

결정: Assignment와 ReviewRequest의 Save 메서드는 **모델 내부의 `SpecId` 필드에 의존**한다.

- Assignment, ReviewRequest 모델은 반드시 `SpecId`를 필드로 포함해야 한다 (1단계에서 이미 `required`로 정의됨).
- 저장소 구현은 `assignment.SpecId`로 파일 경로를 계산한다.
- Load 메서드는 `specId`를 명시적 파라미터로 받는다 (파일 탐색 없이 경로를 바로 구성하기 위해).
- 이 비대칭(Load는 파라미터, Save는 모델 필드)은 의도적이다: Load 시에는 아직 모델 객체가 없고, Save 시에는 객체가 이미 완전한 컨텍스트를 갖고 있기 때문이다.

## 14.6 ReviewResponseType 및 editedPayload 직렬화

결정:

- `ReviewResponseType`은 `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`로 직렬화한다: `"approveOption"`, `"rejectWithComment"`, `"partialEditApprove"`.
- `editedPayload`는 `JsonElement?` 타입으로 저장한다. spec마다 수정 대상이 다르므로 유연한 비정형 저장이 필요하다.
- `editedPayload`를 도메인 모델로 변환할 때는 헬퍼 메서드를 제공한다:

```csharp
// ReviewResponse에 추가
public T? DeserializePayload<T>() where T : class =>
    EditedPayload is { } payload
        ? JsonSerializer.Deserialize<T>(payload, FlowJsonOptions.Default)
        : null;
```

이유:

- `editedPayload`의 스키마는 review request마다 다르므로 typed object를 강제하면 확장이 어렵다.
- 도메인 모델 변환은 상위 계층(runner, webservice)이 필요할 때 수행한다.

## 14.7 Fixture 도구

Fixture 초기화/리셋은 저장소 인터페이스에 포함하지 않고, 별도 도구 클래스로 분리한다:

```csharp
public class FixtureInitializer
{
    public FixtureInitializer(IFlowStore store) { ... }
    Task InitializeAsync(CancellationToken ct = default);
    Task ResetAsync(CancellationToken ct = default);
}
```

이유:

- fixture는 테스트/개발 전용이지 운영 저장소의 책임이 아니다.
- 저장소 인터페이스를 깨끗하게 유지한다.

## 14.8 기타 결정

- API는 async를 기본으로 한다.
- 2단계에서는 `SpecService.ApplyEvent()` 같은 facade를 두지 않는다. 저장 계층과 규칙 계층을 먼저 분리한다.
- facade까지 바로 만들면 side effect 실행 책임이 섞여 2단계 범위를 넘는다.

# 15. Side Effect 실행기

## 15.1 문제

1단계 RuleEvaluator는 side effect를 목록으로 반환하기만 한다. 2단계에서는 이 목록을 저장 계층이 직접 실행할지, runner가 실행할지 경계를 고정해야 한다.

## 15.2 결정: side effect 실행은 runner 계층

결정:

- `flow-core`는 side effect 목록을 계산만 한다.
- 실제 side effect 실행은 runner 계층이 담당한다.
- 2단계에서는 저장 API와 activity append API만 제공한다.
- 실패 처리는 "부분 적용 후 기록"을 기본으로 한다.

이유:

- runner만이 파일 시스템, Slack, 외부 API 같은 실행 환경 권한을 가진다.
- side effect 실패 때문에 상태 전이 자체를 롤백하면 더 큰 혼란이 생긴다.

## 15.3 저장 순서와 실패 복구 규칙

side effect 실행 시 여러 파일(spec.json, assignment, review request, activity)을 쓴다. 파일 시스템은 원자적 다중 파일 쓰기를 지원하지 않으므로, 순서와 실패 복구 규칙을 명확히 정한다.

### 저장 순서

RuleEvaluator가 mutation + side effects를 반환한 뒤, runner는 아래 순서로 저장한다:

1. **Side effect 파일 먼저 생성**: assignment 파일, review request 파일 등을 생성/갱신한다.
2. **Spec.json 저장 (CAS)**: mutation을 적용한 spec.json을 expectedVersion 기반으로 저장한다. 이 단계에서 CAS 실패 시 전체 전이를 거부하고, 1단계에서 생성한 파일을 정리(삭제)한다.
3. **Activity 기록**: spec.json 저장 성공 후에만 activity를 append한다. activity는 **확정된 전이만 기록**한다.

### 핵심 원칙: activity는 커밋 후 기록만 남긴다

- activity log는 CAS를 통과하고 spec.json이 성공적으로 저장된 전이만 기록한다.
- CAS 실패나 I/O 실패로 spec.json 저장이 실패한 전이는 activity에 남기지 않는다.
- 이렇게 하면 activity의 모든 레코드는 실제 발생한 상태 전이를 의미하며, 시도만 하고 실패한 전이와 섞이지 않는다.

### 개별 파일 쓰기 전략

- 모든 JSON 파일 저장은 **write-to-temp-then-rename** 패턴을 사용한다 (같은 디렉토리에 `.tmp` 파일 쓰고 rename).
- activity JSONL append는 `FileShare.Read` + append 모드로 쓴다.

### 실패 복구 규칙

| 실패 지점 | 결과 | 복구 |
| --- | --- | --- |
| 1단계: side effect 파일 생성 실패 | 전이 중단 | 이미 생성된 side effect 파일 삭제. spec.json은 변경되지 않았으므로 상태 일관성 유지 |
| 2단계: spec.json CAS 실패 | 전이 거부 (conflict) | 1단계에서 생성한 side effect 파일 삭제. caller에 `SaveResult.Conflict` 반환 |
| 2단계: spec.json I/O 실패 | 전이 중단 | 1단계에서 생성한 side effect 파일 삭제. 예외 throw |
| 3단계: activity append 실패 | 전이는 이미 확정 | activity 실패를 로깅하고 재시도 큐에 넣는다. 상태 전이를 롤백하지 않는다 |

### 고아 파일 방지

- Spec.json의 `assignments[]`와 `reviewRequestIds[]`는 해당 파일이 성공적으로 생성된 후에만 갱신된다.
- 2단계 CAS 실패 시 1단계 파일을 삭제하므로, spec이 존재하지 않는 assignment/reviewRequest ID를 참조하는 상태가 발생하지 않는다.
- 만약 1단계에서 파일을 생성했는데 2단계 실패 후 삭제도 실패하면(드문 I/O 에러), 고아 파일이 남을 수 있다. 이는 주기적 정합성 검사(3단계 범위)로 정리한다.

# 16. 1단계 모델 보완 사항

## 16.1 문제

1단계 구현에서 flow-schema.md 대비 누락된 모델이 있다.

## 16.2 누락 항목

| 항목 | 현재 상태 | 필요 여부 |
| --- | --- | --- |
| ReviewRequest.options | 없음 | 2단계에서 필요 (사용자에게 선택지 제시) |
| Assignment 실행 상세 (startedAt, timeoutSeconds 등) | 없음 | 2단계 또는 3단계에서 필요 |
| AcceptanceCriteria 모델 | 없음 | 2단계에서 필요 (spec 전체 모델) |
| Test 모델 | 없음 | 2단계에서 필요 (spec 전체 모델) |
| Dependency 모델 | SpecSnapshot에 없음 | 2단계에서 필요 (cascade 계산) |
| ActivityEvent 모델 | 없음 | 2단계에서 필요 (activity log) |

결정:

- 2단계 필수(P0): `ActivityEvent`, `Dependency`, `Spec`, `SaveResult`, 저장소 인터페이스
- 2단계 중요(P1): `Assignment` 상세 필드, `ReviewRequest.options`, `AcceptanceCriterion`, `TestDefinition`
- 3단계 이관 가능(P2): heartbeat 실행 로직, worktree 연동 로직, AC/test 고급 validation, external side effect adapter

# 17. 문서 반영 대상

이 문서의 결정사항이 확정되면 아래에 반영한다.

| 결정 | 반영 대상 |
| --- | --- |
| 2. 기존 시스템 관계 | flow-구현-로드맵.md 마이그레이션 절 추가 |
| 3. 디렉토리 구조 | flow-schema.md 저장 구조 절 추가/갱신 |
| 4. 전체 Spec 모델 | flow-core Models/ |
| 5. Activity log 계약 | flow-schema.md, flow-core Models/ |
| 6. Assignment 생명주기 | flow-schema.md, flow-core Models/ |
| 7. Review request 생명주기 | flow-schema.md, flow-core Models/ |
| 8. JSON 직렬화 | flow-core 공통 옵션 |
| 9. 빈 필드 제외 | flow-core 저장 계층 |
| 10. 동시성 제어 | flow-core 저장 계층 |
| 11. ID 생성 | flow-core 유틸리티 |
| 12. Dependency 그래프 | flow-core Rules/ 또는 별도 모듈 |
| 13. Fixture | flow-core.tests 또는 별도 도구 |
| 14. 저장 API | flow-core 인터페이스 |
| 15. Side effect 실행기 | flow-core 또는 3단계 |
| 16. 모델 보완 | flow-core Models/ |
| 전체 | flow-schema.md 최종 동기화 (enum 영어화, 디렉토리 구조, ID 포맷 등) |
