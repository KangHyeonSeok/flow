# Flow 1단계 구현 요구사항

이 문서는 1단계(State Rule) 구현을 시작하기 전에 확정해야 하는 결정사항을 정리한 문서다. 각 항목에 권장안을 포함하며, 확정 후 관련 문서(flow-state-rule.md, flow-schema.md)에 반영한다.

# 1. 요구사항 계층 구조

1단계 요구사항은 아래 3단계 계층으로 정리하는 편이 가장 안전하다.

## 1.1 비즈니스 목표

최상위 목표는 아래 한 줄로 요약할 수 있다.

- 스펙 중심의 자율 주행 개발 환경을 구축한다.

여기서 핵심은 다음과 같다.

- spec이 authoritative source다.
- agent는 spec을 중심으로 협업한다.
- 상태 전이와 검토 요청, evidence, activity log가 모두 spec 단위로 추적된다.
- 사람이 개입하더라도 파이프라인이 끊기지 않는다.

즉 1단계 구현의 목적은 기능을 많이 만드는 것이 아니라, 이후 runner, agent, webservice가 모두 공유할 상태 기계와 저장 계약을 먼저 고정하는 데 있다.

## 1.2 상태 전이 매트릭스

두 번째 계층은 state transition matrix다.

- 어떤 이벤트가 들어오면
- 어떤 선행 조건에서
- 어떤 state / processingStatus로 바뀌고
- 어떤 side effect를 내보내는가

이 계층은 `flow-state-rule.md`의 table-driven 규칙표와 1:1로 대응해야 한다. 테스트 코드도 같은 행 구조를 그대로 사용해야 한다.

예:

```text
(현재: 구현, 처리중)
+ (이벤트: implementation_submitted)
= (다음: 테스트 검증, 대기)
+ side effects: LogActivity, CreateAssignment(Test Validator)
```

## 1.3 에이전트 계약

세 번째 계층은 agent contract다.

- 각 agent는 무엇을 입력으로 받는가
- 어떤 이벤트를 제안할 수 있는가
- 어떤 evidence / artifact / message를 출력해야 하는가
- 어떤 이벤트는 절대 제안하면 안 되는가

이 계층은 `flow-runner-agent-contract.md`와 `flow-schema.md`를 기준으로 맞춘다.

내 의견:

- 이 세 계층을 섞지 않는 것이 중요하다.
- 비즈니스 목표는 방향을 정하고, 상태 전이 매트릭스는 규칙을 정하고, agent contract는 실행 인터페이스를 정한다.
- 세 문서를 동시에 고치더라도 언제나 이 순서로 생각하는 편이 좋다.

# 2. Rule Evaluator 입출력 계약

## 2.1 목적

Rule evaluator는 상태 전이의 핵심 함수다. I/O 없이 순수 계산만 수행해야 하므로 입출력 타입을 먼저 고정한다.

## 2.2 입력

```
RuleInput
  spec snapshot
    id
    state
    processingStatus
    riskLevel
    dependencies
    retryCounters
    version
  active assignments          // 현재 running 상태인 assignment 목록
  open review requests        // 현재 open 상태인 review request 목록
  incoming event
    type                      // 이벤트 종류 (event enum 중 하나)
    actor                     // 발생 주체
    baseVersion               // 이벤트 발생 시점의 spec version
    payload                   // 이벤트별 추가 데이터
```

설명:

- spec snapshot은 전체 spec이 아니라 rule 판단에 필요한 필드만 포함한다.
- `retryCounters`는 반복 규칙 판단에 필요하다 (3절 참조).
- active assignments는 timeout 판단과 중복 실행 방지에 필요하다.
- open review requests는 review timeout 판단과 사용자검토 상태 확인에 필요하다.
- `baseVersion`은 optimistic concurrency 충돌 감지에 필요하다.

## 2.3 출력

```
RuleOutput
  accepted                    // 이벤트 수용 여부 (bool)
  rejectionReason             // 거부 시 사유
  mutation                    // 수용 시 상태 변경안 (nullable)
    nextState
    nextProcessingStatus
    resetRetryCounter         // 어떤 카운터를 리셋할지 (nullable)
    incrementRetryCounter     // 어떤 카운터를 증가시킬지 (nullable)
  sideEffects                 // 부수 효과 목록
```

설명:

- `accepted=false`이면 mutation과 sideEffects는 모두 무시한다.
- mutation이 null이면 상태는 변경하지 않고 sideEffects만 실행한다.
- mutation의 nextState가 현재와 같으면 처리 상태만 변경된 것이다.
- `accepted=false`이면서 `rejectionReason=ConflictError`이면 caller는 최신 spec을 다시 읽고 재평가해야 한다.

## 2.4 부수 효과 종류

초기 구현에서 필요한 side effect 종류는 아래로 고정한다.

| side effect | 설명 |
| --- | --- |
| `LogActivity` | activity event 기록 (모든 전이에 필수) |
| `CreateAssignment` | 새 assignment 생성 |
| `CancelAssignment` | 기존 assignment를 cancelled로 종료 |
| `FailAssignment` | 기존 assignment를 failed로 종료 |
| `CreateReviewRequest` | review request 생성 |
| `CloseReviewRequest` | review request를 closed로 변경 |

각 side effect는 필요한 파라미터를 자체적으로 가진다.

```
CreateAssignment
  specId
  agentRole
  type

CancelAssignment
  assignmentId
  reason

FailAssignment
  assignmentId
  reason

CreateReviewRequest
  specId
  reason
  questions

CloseReviewRequest
  reviewRequestId
  reason
```

## 2.5 에지 케이스 side effect 규칙

초기 구현에서 특히 빠뜨리면 안 되는 side effect는 아래다.

- retry 초과:
  - `LogActivity`
  - 활성 assignment가 있으면 `FailAssignment`
  - 필요 시 `CreateReviewRequest`
- dependency cascade:
  - `LogActivity`
  - 진행 중 assignment가 있으면 `CancelAssignment`
  - 필요 시 `CreateReviewRequest`
- review request deadline 초과:
  - `LogActivity`
  - `CloseReviewRequest`
  - 정책에 따라 `CreateReviewRequest` 또는 실패 전환
- 강제 상태 변경 또는 삭제 요청:
  - `LogActivity`
  - 활성 assignment가 있으면 `CancelAssignment` 또는 `FailAssignment`

내 의견:

- 1단계에서 side effect를 대충 두면 runner 구현 때 로직이 문서 밖으로 새어나간다.
- 상태 전이만 정의하지 말고, 각 예외 경로에서 시스템이 무엇을 "해야 하는가"까지 같이 적는 편이 좋다.

# 3. 반복 카운트 저장

## 3.1 문제

처리 상태 반복 규칙에서 세 가지 루프에 각각 최대 3회 제한이 있다.

- 사용자검토 루프: `검토 -> 사용자검토 -> 검토`
- 재작업 루프: `검토 -> 대기(재작업) -> 처리중 -> 검토`
- Architect 반려 루프: `구현 검토 -> architect_review_rejected -> 구현 검토`

이 횟수를 어디에 저장할지 결정해야 한다.

## 3.2 선택지

- (A) `spec.json`에 `retryCounters` 필드 추가
- (B) activity log에서 매번 이벤트를 순회해서 카운트

## 3.3 결정: (A) spec.json에 저장

이유:

- 매번 activity log를 순회하면 spec 수가 늘어날수록 비용이 증가한다.
- rule evaluator는 I/O 없이 동작해야 하므로 입력에 카운터가 포함되어야 한다.
- activity log는 audit/replay 용도이지 실시간 판단 입력이 아니다.

## 3.4 스키마 추가

`spec.json`에 아래 필드를 추가한다.

```json
{
  "retryCounters": {
    "userReviewLoopCount": 0,
    "reworkLoopCount": 0,
    "architectReviewLoopCount": 0
  }
}
```

규칙:

- `spec_validation_user_review_requested` 이벤트 수용 시 `userReviewLoopCount`를 1 증가한다.
- `spec_validation_rework_requested` 이벤트 수용 시 `reworkLoopCount`를 1 증가한다.
- `architect_review_rejected` 이벤트 수용 시 `architectReviewLoopCount`를 1 증가한다.
- 어느 카운터든 3을 초과하면 해당 이벤트를 거부하고 `spec_validation_failed`로 대체한다.
- `spec_validation_passed` 수용 시 `userReviewLoopCount`와 `reworkLoopCount`를 0으로 리셋한다.
- `architect_review_passed` 수용 시 `architectReviewLoopCount`를 0으로 리셋한다.
- forward phase 전환 시 모든 카운터를 0으로 리셋한다.
- backward phase 전환 시 카운터를 유지한다.

비워도 되는 필드에 `retryCounters`를 추가한다. 값이 모두 0이면 저장 시 생략 가능하다.

# 4. 낙관적 잠금과 충돌 처리

## 4.1 문제

`baseVersion`이 입력과 agent output에 포함되어도, 충돌 시 무엇을 반환할지 고정하지 않으면 구현마다 처리 방식이 달라진다.

## 4.2 결정

`RuleInput.incomingEvent.baseVersion`이 현재 spec snapshot의 `version`과 다르면 rule evaluator는 이벤트를 거부한다.

권장 반환:

```text
accepted = false
rejectionReason = ConflictError
mutation = null
sideEffects = [LogActivity]
```

규칙:

- conflict는 상태 전이 실패이지 시스템 오류가 아니다.
- caller는 최신 spec을 다시 읽고 이벤트를 재평가해야 한다.
- agent output payload의 `baseVersion` 검증도 같은 원칙을 따른다.

내 의견:

- conflict는 예외 throw보다 명시적 결과로 반환하는 편이 table-driven test에 유리하다.
- 1단계에서는 ACID 트랜잭션보다 optimistic concurrency를 먼저 고정하는 편이 현실적이다.

# 5. 보류 복원 로직

## 5.1 문제

`dependency_resolved`로 `보류`에서 복귀할 때, 진입 전 처리 상태로 돌아가야 하는지, 항상 `대기`로 리셋해야 하는지 결정해야 한다.

## 5.2 선택지

- (A) 항상 `대기`로 리셋
- (B) 진입 전 처리 상태를 저장하고 복원

## 5.3 결정: (A) 항상 대기로 리셋 (state 기반 예외 포함)

이유:

- `보류` 기간 동안 외부 상황이 바뀔 수 있으므로 이전 상태를 그대로 복원하는 것은 위험하다.
- `대기`로 돌아가면 담당자가 처음부터 다시 작업을 시작하므로 안전하다.
- 저장할 필드가 줄어들어 구현이 단순해진다.
- 처리 상태 전이표에 이미 `보류 -> dependency_resolved -> 대기`로 고정되어 있으므로 문서와 일치한다.

예외:

- `state=검토`이고 `보류` 진입 전 처리 상태가 `검토`였다면, 복원 시에도 `검토`로 돌아가는 것이 자연스럽다. `대기`로 돌아가면 Spec Validator가 아닌 담당자가 다시 작업을 시작하는 것처럼 보이기 때문이다.
- 이 예외를 적용하려면 rule evaluator가 현재 `state`를 보고 `대기` 또는 `검토`를 결정하면 된다. 별도 저장 필드는 필요하지 않다.

최종 규칙:

- `state`가 `검토`이면 `보류 -> 검토`로 복원한다.
- 그 외 모든 `state`에서는 `보류 -> 대기`로 복원한다.

## 5.4 처리 상태 전이표 수정

기존:

```
| 보류 | dependency_resolved | blocker 해소 | 대기 | Spec Manager |
```

변경:

```
| 보류 | dependency_resolved | blocker 해소, state≠검토 | 대기 | Spec Manager |
| 보류 | dependency_resolved | blocker 해소, state=검토 | 검토 | Spec Manager |
```

# 6. spec_activated 이벤트 역할

## 6.1 문제

이벤트 목록에 `spec_activated`가 있고 발생 주체는 Spec Manager rule이지만, 상태 전이표에서 `검토 -> 활성` 전이는 `spec_validation_passed`로 정의되어 있다. 두 이벤트의 관계가 불명확하다.

## 6.2 선택지

- (A) `spec_activated`는 부수 효과 로그 이벤트. 실제 전이는 `spec_validation_passed`가 유발.
- (B) `spec_validation_passed`는 Spec Validator 판정이고, Spec Manager가 이를 받아 `spec_activated`를 발생시키면 그게 실제 전이 이벤트.

## 6.3 결정: (A) 부수 효과 로그 이벤트

이유:

- (B)는 하나의 전이에 이벤트가 두 개 필요해 불필요하게 복잡하다.
- rule evaluator는 단일 이벤트 입력에 대해 단일 mutation을 계산하는 것이 테스트하기 쉽다.
- `spec_activated`와 `spec_completed`는 activity log에 기록하는 운영 마커로 사용한다.

규칙:

- `spec_validation_passed` 이벤트가 `검토 -> 활성` 전이를 유발한다.
- 전이 수용 시 side effect로 `LogActivity(action: spec_activated)`를 추가한다.
- `spec_completed` 이벤트가 `활성 -> 완료` 전이를 유발한다.
- `spec_activated`는 rule evaluator의 입력 이벤트 enum에 포함하지 않는다. activity event의 `action` 필드 값으로만 사용한다.
- `spec_completed`는 사용자/운영 입력 이벤트이므로 입력 이벤트 enum에 포함한다.

코드 구분:

- `FlowEvent` enum: rule evaluator가 입력으로 받는 이벤트 (17개, `spec_activated` 제외)
- `ActivityAction` enum 또는 string: activity log의 `action` 필드에 기록하는 값 (`spec_activated` 포함)

# 7. Phase 전환 시 처리 상태 초기값

## 7.1 문제

state-rule 문서에 3개 전환의 처리 상태 초기값만 정의되어 있다. 나머지가 빠져 있어 rule evaluator가 출력할 `nextProcessingStatus`를 결정할 수 없다.

## 7.2 전체 표

| 전환 | 다음 processingStatus | 이유 |
| --- | --- | --- |
| `초안 -> 대기` | `대기` | assignment 대기 상태 |
| `대기 -> 구현 검토` | `대기` | Architect assignment 생성 직후이므로 곧 `처리중`으로 전환되지만 초기값은 `대기` |
| `대기 -> 구현` | `대기` | Developer assignment 생성 직후이므로 곧 `처리중`으로 전환되지만 초기값은 `대기` |
| `구현 검토 -> 구현` | `대기` | 기정의 |
| `구현 -> 테스트 검증` | `대기` | 기정의 |
| `테스트 검증 -> 검토` | `검토` | 기정의 |
| `테스트 검증 -> 구현` (재작업) | `대기` | 재구현 대기 |
| `검토 -> 구현` (재작업) | `대기` | 재구현 대기 |
| `검토 -> 활성` | `완료` | 검증 완료 후 운영 단계 진입, 추가 처리 없음 |
| `검토 -> 실패` | `실패` | terminal failure |
| `활성 -> 완료` | `완료` | 최종 완료 |
| `활성 -> 검토` (rollback) | `검토` | Spec Validator 재검토 시작 |

## 7.3 규칙

- phase 전환이 발생하면 rule evaluator는 반드시 `nextProcessingStatus`를 위 표에 따라 설정한다.
- `retryCounters`는 forward 전환 시 리셋하고, backward 전환 시 유지한다.
  - forward: `초안->대기`, `대기->구현 검토`, `대기->구현`, `구현 검토->구현`, `구현->테스트 검증`, `테스트 검증->검토`, `검토->활성`, `활성->완료`
  - backward: `테스트 검증->구현`, `검토->구현`, `활성->검토`
- `대기 -> 구현`과 `대기 -> 구현 검토` 전환은 `assignment_started` 이벤트에 의해 발생한다. 이때 상태는 바뀌지만 처리 상태는 `대기`로 설정한다. 직후 같은 루프 안에서 runner가 assignment를 시작하면서 `대기 -> 처리중` 전환이 별도로 발생한다.

## 7.4 `assignment_started`의 이중 역할 해소

`assignment_started` 이벤트는 현재 두 가지 전이를 동시에 유발하는 것처럼 보이는 문제가 있다.

- 상태 전이표: `대기 | assignment_started -> 구현` (state 변경)
- 처리 상태 전이표: `대기 | assignment_started -> 처리중` (processingStatus 변경)

이 두 표가 동시에 적용되면 `assignment_started` 하나로 state=`구현`, processingStatus=`처리중`이 되지만, phase 전환 초기값 표(7.2)에서는 `대기 -> 구현` 전환 시 processingStatus=`대기`로 설정한다고 정의했다. 이 모순을 해소해야 한다.

결정:

- rule evaluator는 하나의 이벤트에 대해 상태 전이표와 처리 상태 전이표를 동시에 평가하고, **phase 전환이 발생하면 phase 전환 초기값이 우선한다.**
- 즉 `assignment_started`가 `대기 -> 구현` phase 전환을 유발하면, processingStatus는 phase 전환 초기값 표에 따라 `대기`가 된다.
- runner는 직후 별도 루프에서 해당 spec을 다시 평가하고, 이때 `state=구현, processingStatus=대기`인 spec에 대해 실제 agent 호출과 함께 processingStatus를 `처리중`으로 전환한다.

이 방식의 장점:

- rule evaluator의 출력이 단일 mutation으로 단순해진다.
- phase 전환 초기값 표가 항상 우선하므로 규칙 충돌이 없다.
- 상태 전이표와 처리 상태 전이표 사이에 우선순위를 명시적으로 고정할 수 있다.

처리 상태 전이표 적용 규칙:

- phase 전환이 없는 이벤트: 처리 상태 전이표를 그대로 적용한다.
- phase 전환이 있는 이벤트: phase 전환 초기값 표(7.2)가 processingStatus를 결정하고, 처리 상태 전이표는 무시한다.

## 7.5 설계 메모

`대기 -> 구현` 전환 시 처리 상태를 바로 `처리중`으로 설정하지 않는 이유:

- 상태 전이와 처리 상태 전이를 분리하면 각 전이의 선행 조건을 독립적으로 검증할 수 있다.
- `assignment_started` 이벤트 하나가 state 변경과 processingStatus 변경을 동시에 유발하면 rule evaluator의 출력이 복잡해진다.
- 실제 구현에서는 runner가 한 루프 안에서 두 전이를 연속 적용하므로 사용자에게 보이는 차이는 없다.

# 8. 활성에서 완료 전환 조건

## 8.1 문제

`spec_completed` 이벤트의 선행 조건이 "운영상 완료 처리 가능"으로만 되어 있어 구현할 수 없다.

## 8.2 선택지

- (A) 사용자 명시 승인
- (B) 일정 기간 경과 후 자동 완료
- (C) 사용자 명시 승인 + 자동 완료 fallback

## 8.3 결정: (A) 사용자 명시 승인

이유:

- 자동 완료는 기간 설정이 프로젝트마다 다르고, 잘못 설정하면 아직 운영 중인 스펙이 닫힌다.
- 초기 구현에서는 단순한 규칙이 더 안전하다.
- 나중에 자동 완료가 필요하면 `spec_completed` 이벤트를 scheduler에서 발생시키는 것으로 확장 가능하다.

규칙:

- `spec_completed` 이벤트는 사용자 또는 운영 입력에 의해서만 발생한다.
- 선행 조건: `state=활성`, `processingStatus=완료`, 열린 review request 없음.
- 이벤트 발생 주체를 기존 "Spec Manager rule"에서 "사용자 또는 운영 입력"으로 변경한다.

# 9. 구현 검토 반려 후 복구 경로

## 9.1 문제

`구현 검토 | architect_review_rejected -> 구현 검토`이고 "Planner 보완 assignment 생성"인데, Planner가 보완을 완료한 뒤 어떤 이벤트로 Architect 재검토가 트리거되는지 정의되어 있지 않다.

`draft_updated`는 `초안` 상태에서만 전이가 정의되어 있으므로 `구현 검토` 상태에서는 사용할 수 없다.

## 9.2 결정

Planner 보완 assignment의 결과 이벤트로 기존 `draft_updated`를 재사용하되, `구현 검토` 상태에서도 유효한 전이를 추가한다.

상태 전이표 추가:

```
| 구현 검토 | draft_updated | Planner가 보완 완료 | 구현 검토 | Architect 재검토 assignment 생성 |
```

처리 상태 흐름:

1. `architect_review_rejected` → 처리 상태 `대기`, Planner 보완 assignment 생성
2. Planner assignment 시작 → 처리 상태 `처리중`
3. Planner 완료 → `draft_updated` 제출 → 처리 상태 `대기`, Architect 재검토 assignment 생성
4. Architect assignment 시작 → 처리 상태 `처리중`
5. `architect_review_passed` → 상태 `구현`으로 전환

이유:

- 새 이벤트를 만들기보다 기존 이벤트를 재사용하는 것이 이벤트 목록을 작게 유지한다.
- `draft_updated`는 "Planner가 스펙을 수정했다"는 의미이므로 `구현 검토` 상태에서도 자연스럽다.

## 9.3 반복 제한

Architect 반려 → Planner 보완 루프에도 반복 제한이 필요하다.

규칙:

- `구현 검토` 상태에서 `architect_review_rejected` 반복은 최대 3회까지 허용한다.
- 3회 초과 시 처리 상태를 `실패`로 전환하고 review request를 생성한다.
- 카운터는 `retryCounters.architectReviewLoopCount`에 저장한다 (3절 참조).

# 10. 중간 상태 취소/중단 이벤트

## 10.1 문제

현재 state-rule 문서에서 `rollback_requested`는 `활성` 상태에서만 정의되어 있다. `구현`, `테스트 검증`, `검토` 같은 중간 상태에서 사용자가 작업을 취소하거나 중단하려는 경우의 이벤트와 전이 규칙이 없다.

## 10.2 선택지

- (A) 중간 상태 취소를 1단계에서 지원하지 않고 나중에 추가
- (B) `cancel_requested` 이벤트를 새로 정의하여 중간 상태에서도 취소 가능하게 만듦

## 10.3 결정: (B) `cancel_requested` 이벤트 추가

이유:

- 실제 운영에서 구현 중 스펙이 무의미해지거나 방향이 바뀌는 경우가 빈번하다.
- 취소 경로가 없으면 사용자가 스펙을 방치하거나 비정상적인 방법으로 상태를 바꾸게 된다.
- rule evaluator에 이벤트 하나와 전이 규칙 몇 줄을 추가하는 비용은 작다.

이벤트 정의:

- 이벤트명: `cancel_requested`
- 발생 주체: 사용자 또는 운영 입력
- 적용 가능 상태: `초안`, `대기`, `구현 검토`, `구현`, `테스트 검증`, `검토`
- 적용 불가 상태: `활성` (이미 `rollback_requested` 사용), `실패`, `완료`

상태 전이표 추가:

```
| 초안 | cancel_requested | 사용자 취소 | 실패 | 활동 로그, 실패 사유 기록 |
| 대기 | cancel_requested | 사용자 취소 | 실패 | 활동 로그, 실패 사유 기록 |
| 구현 검토 | cancel_requested | 사용자 취소 | 실패 | 활성 assignment CancelAssignment, 활동 로그 |
| 구현 | cancel_requested | 사용자 취소 | 실패 | 활성 assignment CancelAssignment, worktree 해제, 활동 로그 |
| 테스트 검증 | cancel_requested | 사용자 취소 | 실패 | 활성 assignment CancelAssignment, 활동 로그 |
| 검토 | cancel_requested | 사용자 취소 | 실패 | 열린 review request CloseReviewRequest, 활동 로그 |
```

공통 규칙:

- `cancel_requested` 시 state는 `실패`로 전환한다.
- processingStatus는 `실패`로 설정한다.
- 활성 assignment가 있으면 `CancelAssignment` side effect를 발생시킨다.
- 열린 review request가 있으면 `CloseReviewRequest` side effect를 발생시킨다.
- activity log에 취소 사유를 기록한다.

이벤트 목록(state-rule 3.3)에 `cancel_requested`를 추가한다.
이벤트 발생 주체(state-rule 4.7)의 "사용자 또는 운영 입력"에 `cancel_requested`를 추가한다.

# 11. Golden Path와 예외 시나리오

이 문서의 결정사항이 올바른지 검증하려면 최소한 아래 시나리오가 테스트로 통과해야 한다.

## 11.1 Scenario A: 정상 경로

```text
초안(대기)
-> ac_precheck_passed
-> 대기(대기)
-> assignment_started (risk=low)
-> 구현(대기)
-> [runner: processingStatus -> 처리중]
-> implementation_submitted
-> 테스트 검증(대기)
-> [runner: processingStatus -> 처리중]
-> test_validation_passed
-> 검토(검토)
-> spec_validation_passed
-> 활성(완료)
-> spec_completed
-> 완료(완료)
```

완료 기준:

- 각 phase 전환이 기대 processingStatus를 가진다.
- side effect가 누락되지 않는다.
- retry counter가 forward 전환마다 적절히 리셋된다.

## 11.2 Scenario B: 중간 상태 취소

```text
구현(처리중)
-> cancel_requested
-> 실패(실패)
```

완료 기준:

- 활성 assignment가 `CancelAssignment`로 종료된다.
- worktree lock이 해제된다.
- activity log가 남는다.
- orphan assignment가 남지 않는다.

## 11.3 Scenario C: 사용자 무응답

```text
검토(검토)
-> spec_validation_user_review_requested
-> 검토(사용자검토)
-> review_request_timed_out
-> 실패(실패)
```

완료 기준:

- review request deadline이 실제로 적용된다.
- review request가 `closed`로 변경된다.
- downstream spec이 영구 보류에 빠지지 않는다.

## 11.4 Scenario D: dependency cascade

```text
upstream spec -> 실패
-> dependency_failed
-> downstream spec: processingStatus = 보류
-> in-flight assignment cancelled
```

완료 기준:

- 진행 중 작업이 중단된다.
- review request와 activity log가 생성된다.

## 11.5 Scenario E: version conflict

```text
agent reads spec version 3
user updates spec to version 4
agent submits baseVersion 3
-> ConflictError
-> mutation 없음
```

완료 기준:

- stale 결과가 자동 적용되지 않는다.

## 11.6 Scenario F: Architect 반려 루프

```text
대기(대기)
-> assignment_started (risk=medium)
-> 구현 검토(대기)
-> [runner: processingStatus -> 처리중]
-> architect_review_rejected (1회차)
-> 구현 검토(대기), Planner 보완 assignment 생성
-> [Planner 완료] -> draft_updated
-> 구현 검토(대기), Architect 재검토 assignment 생성
-> [runner: processingStatus -> 처리중]
-> architect_review_passed
-> 구현(대기)
```

완료 기준:

- `architectReviewLoopCount`가 정확히 증가한다.
- 3회 초과 시 `실패`로 전환된다.

## 11.7 Scenario G: 재작업 루프

```text
검토(검토)
-> spec_validation_rework_requested (1회차)
-> 구현(대기), reworkLoopCount=1
-> [구현 -> 테스트 검증 -> 검토]
-> spec_validation_rework_requested (2회차)
-> 구현(대기), reworkLoopCount=2
-> [구현 -> 테스트 검증 -> 검토]
-> spec_validation_rework_requested (3회차)
-> 구현(대기), reworkLoopCount=3
-> [구현 -> 테스트 검증 -> 검토]
-> spec_validation_rework_requested (4회차, 초과)
-> 거부, spec_validation_failed로 대체
-> 실패(실패)
```

완료 기준:

- 3회까지는 정상 재작업이 진행된다.
- 4회차 시도 시 `spec_validation_failed`로 대체된다.
- backward 전환(`검토->구현`)에서 카운터가 유지된다.

# 12. 기술 스택과 프로젝트 구조

## 12.1 문제

기존 프로젝트는 C# .NET 8 (`tools/flow-cli/`)이고, 로드맵에서 "새 구조로 다시 작성"을 전제한다. 1단계 rule engine을 어디에 만들지 결정해야 한다.

## 12.2 선택지

- (A) 기존 `tools/flow-cli/` 안에 새 모듈로 추가
- (B) 별도 프로젝트로 시작 (예: `tools/flow-core/`)
- (C) 같은 solution 안에 별도 class library 프로젝트

## 12.3 결정: (C) 같은 solution 안에 별도 class library 프로젝트

이유:

- rule engine은 I/O 없는 순수 로직이므로 CLI, runner, webservice에서 공통으로 참조해야 한다.
- 기존 `flow-cli`에 넣으면 CLI 의존성이 rule engine에 섞인다.
- 별도 solution으로 분리하면 초기에 빌드 구성이 복잡해진다.
- 같은 solution에 class library로 두면 빌드는 단순하고 참조는 깨끗하다.

권장 구조:

```
tools/
  flow-cli/
    flow-cli.csproj              // 기존 CLI (나중에 flow-core 참조)
  flow-core/
    flow-core.csproj             // 1단계 대상
    Models/
      SpecState.cs               // state enum
      ProcessingStatus.cs        // processingStatus enum
      FlowEvent.cs               // event enum + event input
      RuleInput.cs               // rule evaluator 입력
      RuleOutput.cs              // rule evaluator 출력
      SideEffect.cs              // side effect 타입들
      RetryCounters.cs           // 반복 카운터
    Rules/
      StateTransitionRule.cs     // 상태 전이 규칙표
      ProcessingStatusRule.cs    // 처리 상태 전이 규칙표
      DependencyCascadeRule.cs   // dependency cascade 규칙
      TimeoutRecoveryRule.cs     // timeout recovery 규칙
      RetryLimitRule.cs          // 반복 제한 규칙
      RuleEvaluator.cs           // 통합 evaluator
    Validation/
      EventPermission.cs         // role + state permission 검증
  flow-core.tests/
    flow-core.tests.csproj
    Rules/
      StateTransitionTests.cs
      ProcessingStatusTests.cs
      DependencyCascadeTests.cs
      TimeoutRecoveryTests.cs
      RetryLimitTests.cs
      ForbiddenTransitionTests.cs
      GoldenPathTests.cs
```

## 12.4 테스트 프레임워크

- xUnit + FluentAssertions
- table-driven test 데이터는 state-rule 문서의 표와 1:1 대응
- 각 테스트 케이스는 `(현재상태, 현재처리상태, 입력이벤트, 선행조건) -> (기대상태, 기대처리상태, 기대sideEffects)` 형태

# 13. 문서화 도구와 관리법

## 13.1 Markdown + Mermaid

상태 규칙 문서 상단에는 Mermaid 상태 전이도를 두는 편이 좋다.

권장 이유:

- 표와 그림을 함께 보면 누락된 역방향 전이를 빨리 찾을 수 있다.
- 리뷰어가 전체 흐름을 빠르게 이해할 수 있다.
- runner 테스트의 golden path와 시각적 비교가 쉬워진다.

## 13.2 Specification by Example

1단계 문서는 설명형 문서보다 예제 중심 문서가 더 맞다.

- 상태 전이 표 = 규칙 정의
- Golden Path 시나리오 = 동작 예제
- 테스트 케이스 = 실행 가능한 명세

즉 문서, 테스트, 코드가 같은 사례 집합을 공유해야 한다.

## 13.3 JSON Schema 분리

`flow-schema.md`의 내용은 나중에 별도 `.schema.json` 파일로 분리하는 편이 좋다.

초기 단계에서는 문서와 C# 모델을 먼저 맞추되, 2단계 이상으로 넘어갈 때 아래 파일을 추가하는 것을 권장한다.

```text
docs/schema/
  spec.schema.json
  assignment.schema.json
  review-request.schema.json
  activity-event.schema.json
  agent-input.schema.json
  agent-output.schema.json
```

내 의견:

- 지금 당장 JSON Schema 파일까지 만들 필요는 없다.
- 하지만 문서와 코드가 안정되면 schema 파일을 분리해 validation test에 붙이는 것이 장기적으로 유리하다.

# 14. 문서 반영 대상

이 문서의 결정사항이 확정되면 아래 문서에 반영한다.

| 결정 | 반영 대상 |
| --- | --- |
| 2. Rule evaluator I/O | flow-schema.md에 타입 추가 |
| 3. retryCounters | flow-schema.md spec.json에 필드 추가 |
| 4. conflict 처리 규칙 | flow-schema.md, flow-runner-agent-contract.md |
| 5. 보류 복원 로직 | flow-state-rule.md 처리 상태 전이표 수정 |
| 6. spec_activated 역할 | flow-state-rule.md 이벤트 설명 보강 |
| 7. phase 전환 초기 processingStatus | flow-state-rule.md에 전체 표 추가 |
| 7. assignment_started 이중 역할 | flow-state-rule.md 적용 우선순위 규칙 추가 |
| 8. 활성->완료 조건 | flow-state-rule.md 전이표 선행 조건 구체화 |
| 9. 구현 검토 반려 복구 경로 | flow-state-rule.md 전이표에 행 추가 |
| 10. cancel_requested 이벤트 | flow-state-rule.md 이벤트/전이표 추가 |
| 12. 프로젝트 구조 | 새 프로젝트 생성 |
| 1. 요구사항 계층 구조 | flow-구현-로드맵.md 상단 원칙 보강 |
| 11. Golden Path 시나리오 | 테스트 fixture 문서 또는 flow-core.tests |
