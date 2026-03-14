# CliPlanner 설계 문서

## 1. Context

Phase 1-5에서 flow-core의 상태 기계, 스토리지, FlowRunner, Review Loop, Backend 추상화가 구현되었다.
현재 실제 LLM 기반 agent는 `CliSpecValidator`만 존재하고, Planner는 아직 `DummyPlanner`다.

Planner를 실제로 도입하려면 두 가지 경로를 분리해서 설계해야 한다.

- 경로 A: 기존 runner 파이프라인 안에서 spec을 보완하거나 실패 spec을 재등록한다.
- 경로 B: 사용자 자연어 요청을 받아 새 draft spec 여러 개를 생성한다.

중요한 전제는 현재 계약만으로는 Planner가 spec 본문을 수정할 수 없다는 점이다.
지금의 `AgentOutput`과 `OutputParser`는 `proposedEvent`, `summary`, `proposedReviewRequest`, `evidenceRefs`만 다루며,
title, goal, acceptance criteria 같은 spec 본문 변경 payload가 없다.

따라서 이 문서는 단순히 `CliSpecValidator` 패턴을 복제하는 수준이 아니라,
Planner가 실제로 의미 있는 결과를 만들기 위한 계약 확장을 포함한 설계로 정리한다.

## 2. 현재 코드 기준 사실

문서 설계를 고정하기 전에 현재 runtime의 사실을 먼저 정리한다.

### 2.1 이미 구현된 부분

- `DummyPlanner`는 `DraftUpdated` 또는 `DraftCreated`만 반환한다.
- `FlowRunner.HandleFailedSpecReregistrationAsync()`는 실패 spec 재등록 시 Planner를 직접 호출한다.
- `RuleEvaluator`는 아래 경우에 Planner assignment를 side effect로 생성한다.
  - `AcPrecheckRejected`
  - `ArchitectReviewRejected`
- `StoreFactory`는 아직 `DummyPlanner`를 등록한다.

### 2.2 아직 맞지 않는 부분

- `DispatchTable`에는 Planner dispatch 규칙이 없다.
  - 즉, `AcPrecheckRejected` 또는 `ArchitectReviewRejected`가 만든 Planner assignment는 현재 runner cycle에서 소비되지 않는다.
- `PromptBuilder`의 Planner 분기는 아직 placeholder 수준이다.
- `OutputParser`는 spec 수정 payload를 파싱하지 못한다.
- 실패 spec 재등록 경로에서도 Planner 결과로 새 spec 내용을 받지 않는다.
  - 현재는 실패 spec의 title/problem/goal/AC를 그대로 복사해서 새 draft를 만든다.
- `PromptBuilder.BuildPrompt()`는 `AgentInput` 기반 단일 spec 프롬프트만 지원한다.
  - 따라서 사용자 요청 분해 경로는 별도 입력 모델 또는 별도 builder가 필요하다.

이 상태에서는 문서 초안에 있던 아래 설명이 그대로는 성립하지 않는다.

- "FlowRunner가 DispatchTable 또는 부수효과에 의해 Planner를 호출한다"
- "Planner가 기존 spec을 수정한다"
- "PlannerService가 `PromptBuilder`를 그대로 사용한다"

## 3. 설계 결정

이 문서는 아래 결정을 최종안으로 채택한다.

### 3.1 경로 A와 경로 B를 분리한다

- 경로 A는 `IAgentAdapter` 기반 `CliPlanner`로 구현한다.
- 경로 B는 runner 밖의 별도 orchestration 서비스인 `PlannerService`로 구현한다.
- 두 경로는 같은 backend 매핑(`planner`)을 쓰되, 프롬프트와 파서는 분리한다.

### 3.2 Planner는 read-only agent로 유지한다

- `AllowFileEdits = false`
- `AllowedTools = Read, Glob, Grep`

`Bash`는 v1에서 허용하지 않는다.
이유는 "읽기 전용 Bash"를 런타임에서 강제할 수 없고, Planner는 코드 수정이나 명령 실행 없이도 충분히 동작해야 하기 때문이다.

### 3.3 사용자 요청 분해 결과는 모두 `Draft/Pending`으로 저장한다

- `Queued`로 바로 올리지 않는다.
- 사용자 확인을 추가로 강제하지도 않는다.
- 저장 직후 기존 파이프라인의 AC precheck를 그대로 타게 한다.

이 선택이 가장 단순하고 안전하다.
새로 생성된 spec도 기존 draft와 동일한 품질 게이트를 통과해야 하기 때문이다.

### 3.4 umbrella spec은 v1에서 지원하지 않는다

현재 모델에는 "구현 대상이 아닌 상위 spec"을 표시하는 필드가 없고,
`DispatchTable`은 모든 `Draft/Pending` spec을 처리 대상으로 본다.

따라서 v1 Planner는 구현 가능한 leaf spec만 생성해야 한다.
umbrella spec이 필요하면 이후에 별도 `SpecType` 또는 `ExecutionMode` 확장을 먼저 추가해야 한다.

### 3.5 중복 검출은 v1에서 prompt-level로만 처리한다

- `PlannerService`는 현재 spec 목록 요약을 프롬프트에 제공한다.
- Planner는 기존 spec ID를 `dependsOn`에 재사용하도록 유도한다.
- RAG나 유사도 검색은 v1 범위에서 제외한다.

정교한 중복 검출은 나중에 붙일 수 있지만, 지금은 저장소의 현재 spec 목록만 제공하는 것이 가장 비용 대비 효과가 좋다.

### 3.6 Planner timeout은 1800초로 통일한다

경로 A는 보통 더 짧겠지만, 경로 B는 코드와 기존 spec 목록을 길게 읽을 수 있다.
현재 backend 설정은 역할 단위 timeout만 지원하므로 `planner` 전체를 1800초로 두는 편이 낫다.

## 4. 핵심 계약 변경

Planner를 실제로 쓰려면 `AgentOutput`에 spec 본문 제안 payload를 추가해야 한다.

### 4.1 새 payload 모델

```csharp
public sealed class ProposedSpecDraft
{
    public string? Title { get; init; }
    public SpecType? Type { get; init; }
    public string? Problem { get; init; }
    public string? Goal { get; init; }
    public IReadOnlyList<AcceptanceCriterionDraft>? AcceptanceCriteria { get; init; }
    public RiskLevel? RiskLevel { get; init; }
    public IReadOnlyList<string>? DependsOn { get; init; }
}

public sealed class AcceptanceCriterionDraft
{
    public required string Text { get; init; }
    public bool Testable { get; init; } = true;
    public string? Notes { get; init; }
}
```

### 4.2 `AgentOutput` 확장

```csharp
public sealed class AgentOutput
{
    public required AgentResult Result { get; init; }
    public required int BaseVersion { get; init; }
    public FlowEvent? ProposedEvent { get; init; }
    public string? Summary { get; init; }
    public string? Message { get; init; }
    public ProposedReviewRequest? ProposedReviewRequest { get; init; }
    public IReadOnlyList<EvidenceRef>? EvidenceRefs { get; init; }

    // Planner 전용
    public ProposedSpecDraft? ProposedSpec { get; init; }
}
```

### 4.3 parser 규칙

- Planner가 `DraftUpdated`를 제안하면 `ProposedSpec`은 필수다.
- Planner가 `DraftCreated`를 제안하면 `ProposedSpec`은 필수다.
- 다른 agent는 `ProposedSpec`을 사용하지 않는다.
- `BaseVersion`은 기존처럼 LLM 응답을 무시하고 `input.CurrentVersion`으로 강제한다.

이 확장은 가장 작고 명확하다.
경로 A와 실패 spec 재등록 모두 같은 payload를 재사용할 수 있기 때문이다.

## 5. 경로 A: runner 내부 Planner

### 5.1 역할

경로 A는 다음 세 경우를 담당한다.

| 시점 | 실제 트리거 | Planner 결과 |
| --- | --- | --- |
| AC precheck 반려 | `AcPrecheckRejected`의 side effect | 현재 draft 보완 후 `DraftUpdated` |
| Architect 반려 | `ArchitectReviewRejected`의 side effect | 현재 spec 보완 후 `DraftUpdated` |
| 실패 spec 재등록 | `HandleFailedSpecReregistrationAsync()` | 새 draft 내용 제안 후 `DraftCreated` |

### 5.2 dispatch 수정이 먼저 필요하다

현재 가장 큰 결함은 Planner assignment가 생성되어도 runner가 이를 dispatch하지 않는다는 점이다.

따라서 `DispatchTable`은 상태만 보지 말고 열린 assignment를 먼저 봐야 한다.

권장 규칙:

1. `Running` 또는 `Queued` 상태의 `Planning` assignment가 있으면 Planner를 최우선 dispatch 대상으로 본다.
2. `Running` planner assignment가 이미 있고 agent 실행 중이면 `Wait`한다.
3. `Planning` assignment가 없을 때만 기존 state 기반 dispatch 규칙을 적용한다.

이렇게 해야 `Draft/Pending`에서 SpecValidator가 Planner보다 먼저 다시 실행되는 문제를 막을 수 있다.

### 5.3 FlowRunner 처리 방식

경로 A의 성공 경로는 아래처럼 고정한다.

1. `RuleEvaluator`가 Planner assignment를 생성한다.
2. `DispatchTable`이 open planning assignment를 보고 Planner를 dispatch한다.
3. `ProcessAgent()`는 새 assignment를 만들지 않고 기존 planning assignment를 재사용한다.
4. `CliPlanner`가 `DraftUpdated` 또는 `DraftCreated`와 `ProposedSpec`을 반환한다.
5. `FlowRunner`는 `EvaluateAndApply()` 전에 `ProposedSpec`을 현재 spec 또는 새 spec에 반영한다.
6. 이후 `RuleEvaluator`가 기존 규칙으로 상태 전이를 계산한다.

### 5.4 draft update 적용 규칙

`DraftUpdated`일 때 `FlowRunner`는 현재 spec에 아래 필드만 반영한다.

- `Title`
- `Type`
- `Problem`
- `Goal`
- `AcceptanceCriteria`
- `RiskLevel`
- `Dependencies.DependsOn`

반영 규칙:

- 값이 null이면 기존 값을 유지한다.
- `AcceptanceCriteria`가 있으면 AC ID는 runner가 새로 부여한다.
- `Assignments`, `ReviewRequestIds`, `TestIds`, `Version`, `CreatedAt`, `DerivedFrom`은 Planner가 건드리지 못한다.

### 5.5 실패 spec 재등록 규칙

실패 spec 재등록은 기존처럼 `FlowRunner.HandleFailedSpecReregistrationAsync()`에 남긴다.
다만 새 spec 내용은 더 이상 원본 spec 복사가 아니라 `ProposedSpec`에서 만든다.

규칙:

1. Planner는 `DraftCreated`와 `ProposedSpec`을 반환한다.
2. Runner는 새 spec을 생성한다.
3. `DerivedFrom = failedSpec.Id`
4. 새 spec 상태는 `Draft/Pending`, `Version = 1`
5. 생성 성공 후 원본 failed spec을 archive한다.

이 경로는 기존 failed review loop 구조와 가장 잘 맞는다.

## 6. 경로 B: 사용자 요청 분해 서비스

### 6.1 위치와 책임

경로 B는 runner loop가 아니라 별도 서비스로 구현한다.

```csharp
public sealed class PlannerService
{
    private readonly IFlowStore _store;
    private readonly BackendRegistry _registry;
    private readonly PlannerPromptBuilder _promptBuilder;
    private readonly PlannerOutputParser _outputParser;
    private readonly TimeProvider _time;

    public async Task<PlanResult> PlanAsync(
        string userRequest,
        CancellationToken ct = default);
}

public sealed class PlanResult
{
    public bool Success { get; init; }
    public IReadOnlyList<Spec> CreatedSpecs { get; init; } = [];
    public string? Summary { get; init; }
    public string? ErrorMessage { get; init; }
}
```

`projectId`는 메서드 인자로 받지 않는다.
현재 `IFlowStore` 구현은 이미 project-scoped이기 때문이다.

### 6.2 입력 프롬프트용 모델

경로 B는 `AgentInput`이 아니라 별도 request model이 필요하다.

```csharp
public sealed class PlannerPlanInput
{
    public required string UserRequest { get; init; }
    public required IReadOnlyList<SpecSummary> ExistingSpecs { get; init; }
}
```

따라서 경로 B는 기존 `PromptBuilder.BuildPrompt()`를 그대로 쓰지 않고,
전용 `PlannerPromptBuilder`를 둔다.

### 6.3 출력 모델

```csharp
public sealed class PlanParseResult
{
    public IReadOnlyList<SpecDraft> Specs { get; init; } = [];
    public string? Summary { get; init; }
}

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
```

주의할 점:

- `RiskLevel`은 `low | medium | high | critical`을 모두 허용해야 한다.
- `acceptanceCriteria` 항목은 실제 모델과 맞춰 `text`를 사용한다.
- JSON 예시 안에 주석은 넣지 않는다.

### 6.4 `PlanAsync()` 흐름

1. `_store.LoadAllAsync()`로 현재 spec 목록을 읽는다.
2. `PlannerPromptBuilder`가 사용자 요청과 현재 spec 요약을 프롬프트로 만든다.
3. `BackendRegistry.GetBackend(AgentRole.Planner)`로 backend를 얻는다.
4. `PlannerOutputParser`가 응답 JSON을 파싱한다.
5. 각 `SpecDraft`를 `Spec`으로 변환한다.
6. `InternalDependsOn`를 실제 생성된 spec ID로 치환한다.
7. 각 spec을 `Draft/Pending`, `Version = 1`로 저장한다.
8. `PlanResult`를 반환한다.

새로 생성된 spec은 runner가 다음 cycle에서 AC precheck를 수행한다.

## 7. 프롬프트 설계

### 7.1 경로 A 프롬프트

경로 A는 기존 spec 보완 또는 failed 재등록에 사용한다.

```text
# 역할: Planner

당신은 소프트웨어 스펙 플래너입니다.
주어진 spec의 문제를 분석하고 수정 가능한 draft를 제안하세요.

# Spec 정보
{spec JSON}

# 최근 활동 이력
{activity log}

# Review Requests
{있으면 포함}

# 지시사항

- 현재 state와 최근 반려 사유를 먼저 해석하세요.
- acceptance criteria를 측정 가능하고 테스트 가능한 문장으로 다시 쓰세요.
- AI가 한 번의 구현 사이클에서 처리 가능한 크기로 범위를 줄이세요.
- 기존 spec을 대체할 수정안만 제안하세요. 상태 전이는 직접 수행하지 마세요.
- 실패 spec 재등록인 경우에는 원본 복사가 아니라 새 draft 본문을 제안하세요.

# 응답 형식

```json
{
  "proposedEvent": "draftUpdated",
  "summary": "반려 사유를 반영해 AC를 구체화했습니다.",
  "proposedSpec": {
    "title": "구체화된 스펙 제목",
    "type": "task",
    "problem": "해결할 문제",
    "goal": "달성 목표",
    "acceptanceCriteria": [
      { "text": "...", "notes": "..." }
    ],
    "riskLevel": "low",
    "dependsOn": ["spec-101"]
  }
}
```
```

### 7.2 경로 B 프롬프트

경로 B는 자연어 요청을 새 spec 여러 개로 분해한다.

```text
# 역할: Planner — 요청 분해

당신은 소프트웨어 스펙 플래너입니다.
사용자의 요청을 AI가 자율적으로 구현할 수 있는 draft spec 단위로 분해하세요.

# 현재 프로젝트 스펙 목록
{ID, Title, State, Dependencies 요약}

# 사용자 요청
{userRequest}

# 분해 원칙

1. 한 spec은 한 번의 구현 사이클에서 끝낼 수 있어야 한다.
2. feature는 acceptance criteria 3-5개 수준을 유지한다.
3. 6개 이상이면 분해를 우선한다.
4. umbrella spec은 만들지 않는다.
5. 이미 존재하는 spec을 재사용할 수 있으면 `dependsOn`에 기존 ID를 넣는다.
6. 각 acceptance criterion은 테스트 가능한 문장으로 작성한다.

# 응답 형식

```json
{
  "specs": [
    {
      "title": "스펙 제목",
      "type": "feature",
      "problem": "해결할 문제",
      "goal": "달성 목표",
      "acceptanceCriteria": [
        { "text": "Given ... When ... Then ..." }
      ],
      "riskLevel": "medium",
      "dependsOn": ["spec-001"],
      "internalDependsOn": [1]
    }
  ],
  "summary": "분해 결과 요약"
}
```
```

## 8. 구현 파일 구조

```text
tools/flow-core/
  Agents/Cli/
    CliPlanner.cs
    PromptBuilder.cs            # 경로 A Planner 프롬프트 추가
    OutputParser.cs             # proposedSpec 파싱 추가
  Planning/
    PlannerPromptBuilder.cs     # 경로 B 전용
    PlannerOutputParser.cs
    PlannerService.cs

tools/flow-console/
  Services/
    StoreFactory.cs             # DummyPlanner -> CliPlanner, BackendRegistry 구성

tools/flow-core.tests/
  CliPlannerTests.cs
  PlannerOutputParserTests.cs
  PlannerServiceTests.cs
  DispatchTableTests.cs         # Planner assignment 우선순위 테스트 추가
  FlowRunnerTests.cs            # Planner 보완 루프 테스트 추가
```

`PlannerService`는 `Runner/` 폴더보다 `Planning/` 폴더가 더 적절하다.
이 서비스는 runner cycle의 일부가 아니라 planner orchestration이기 때문이다.

## 9. 구현 순서

### Phase A: runner 내부 Planner를 먼저 살린다

1. `AgentOutput` + `OutputParser`에 `ProposedSpec` 지원 추가
2. `PromptBuilder`에 Planner 프롬프트 추가
3. `CliPlanner` 구현
4. `DispatchTable`에 planning assignment 우선 규칙 추가
5. `FlowRunner.ProcessAgent()`가 existing planning assignment를 재사용하도록 수정
6. failed spec 재등록 경로가 `ProposedSpec`을 사용하도록 수정
7. `StoreFactory`에서 `DummyPlanner`를 `CliPlanner`로 교체

### Phase B: 사용자 요청 분해를 붙인다

8. `PlannerPromptBuilder`, `PlannerOutputParser`, `PlannerService` 구현
9. service 호출 진입점 추가
10. 생성 spec 저장 후 기존 runner cycle에 자연스럽게 편입

### Phase C: 외부 입력 채널 연결

11. flow-console 또는 별도 command에서 `PlannerService` 호출
12. 이후 Slack bot이 같은 `PlannerService`를 재사용

경로 B의 첫 UI는 `SpecListScreen`의 즉석 `P` 키보다 별도 command 또는 명시적 입력 흐름이 더 적절하다.
현재 `SpecListScreen`은 모니터링 화면이고, 생성 워크플로우를 섞으면 책임이 커진다.

## 10. 설정 예시

```json
{
  "agentBackends": {
    "planner": { "backend": "claude-cli" },
    "specValidator": { "backend": "claude-cli" }
  },
  "backends": {
    "claude-cli": {
      "command": "claude",
      "idleTimeoutSeconds": 300,
      "hardTimeoutSeconds": 1800,
      "maxRetries": 2,
      "allowedTools": ["Read", "Glob", "Grep"]
    }
  }
}
```

## 11. 최종 정리

이 문서에서 가장 중요한 수정 사항은 세 가지다.

1. Planner는 현재 계약만으로는 spec 본문을 수정할 수 없으므로 `ProposedSpec` payload 확장이 필요하다.
2. Planner assignment는 이미 생성되지만 dispatch되지 않으므로 `DispatchTable` 우선순위 수정이 선행돼야 한다.
3. 사용자 요청 분해는 runner 안이 아니라 별도 `PlannerService`로 구현하고, 결과 spec은 모두 `Draft/Pending`으로 저장해야 한다.

이 세 가지를 먼저 고치면 Planner는 더미 수준을 넘어 실제 workflow에 들어올 수 있다.

## 12. 보완 사항

아래는 구현 시 유의해야 할 추가 사항이다.

### 12.1 AcceptanceCriterionDraft.Testable 기본값

실제 `AcceptanceCriterion` 모델은 `bool Testable` 필드를 가진다.
`AcceptanceCriterionDraft`도 이를 포함하되, LLM이 생략하면 `true`를 기본값으로 한다.
Planner가 생성하는 AC는 원칙적으로 테스트 가능해야 하기 때문이다.

### 12.2 CAS 타이밍과 ProposedSpec 적용 순서

`DraftUpdated` + `ProposedSpec`일 때 FlowRunner의 처리 순서:

1. `ProposedSpec` 필드를 현재 spec에 반영 (in-memory)
2. `EvaluateAndApply()`로 상태 전이 계산
3. CAS 커밋 (Version 기반 충돌 검출)

CAS 실패 시 spec 변경과 상태 전이 모두 롤백된다.
즉 ProposedSpec 반영은 CAS 커밋 전에 일어나며, 커밋 실패 시 어떤 변경도 저장되지 않는다.

### 12.3 InternalDependsOn 유효성 검증

경로 B에서 `InternalDependsOn`은 같은 응답 내 spec 배열의 0-based 인덱스다.
파서는 아래 규칙을 적용해야 한다:

- 인덱스가 배열 범위를 벗어나면 해당 의존성을 무시하고 경고 로그를 남긴다.
- 자기 자신을 참조하는 인덱스도 무시한다.
- 순환 참조 검출은 v1에서 하지 않는다 (기존 runner의 dependency resolver가 처리).

### 12.4 ArchitectReviewRejected 후 Planner dispatch 예시

현재 코드의 실제 흐름을 명확히 한다:

```text
1. Architect가 ArchitectReviewRejected 제안
2. RuleEvaluator가 이벤트 수용 → Draft/Pending으로 전이
3. RuleEvaluator의 side effect로 Planning assignment 생성
4. 다음 runner cycle에서 DispatchTable이 open Planning assignment 감지
5. Planner dispatch → DraftUpdated + ProposedSpec 반환
6. FlowRunner가 spec 보완 후 EvaluateAndApply()
7. 이후 SpecValidator → ArchitectureReview → ... 정상 흐름 재진입
```

이 흐름이 동작하려면 §5.2의 DispatchTable 수정이 반드시 선행되어야 한다.
