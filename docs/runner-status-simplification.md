# Runner Status Simplification

## 목적

Runner의 구현, 테스트 검토, 최종 판정이 한 덩어리로 섞여 있으면 상태 전이가 복잡해지고 운영자가 현재 대기 이유를 읽기 어려워진다.
이 문서는 스펙 처리 흐름을 세 역할로 나누고, `needs-review`를 정말로 사용자 행동이 필요한 경우에만 쓰도록 단순화한 기준을 정리한다.

핵심 방향은 다음과 같다.

- 스펙의 외부 상태는 적게 유지한다.
- 내부 handoff는 status가 아니라 `activity` 로그로 표현한다.
- `needs-review`는 더 이상 일반 handoff 상태가 아니다.
- `needs-review`는 Tester 단계까지 끝난 뒤 사용자 질문 또는 사용자 테스트가 필요한 경우에만 사용한다.
- 세 역할은 하나의 runner cycle 안에서 순차적으로 실행한다.

## 역할 분담

### 1. Developer (`gpt-5.4`)

Developer는 구현 책임을 가진다.

- `queued` 상태의 스펙을 가져와 `working`으로 바꾼다.
- condition에 대응하는 테스트를 먼저 작성하거나 보강한다.
- 스펙 요구사항대로 구현한다.
- 테스트를 실행한다.
- 실패하면 코드를 수정하고 테스트를 다시 실행한다.
- 가능한 범위에서 반복적으로 해결을 시도한다.
- 테스트 코드, 테스트 시나리오, 실행 로그, 증거 경로, 한계 사항을 스펙에 기록한다.
- 작업이 끝나면 status를 바꾸지 않고 Test Validator로 내부 handoff 한다.

Developer는 최종 상태를 확정하지 않는다.

### 2. Test Validator (`gpt-5-mini`)

Test Validator는 테스트 품질을 검토한다.

- 테스트가 spec/condition을 실제로 검증하는가
- 테스트가 trivial하지 않은가
- 테스트가 deterministic한가
- 테스트 결과 해석이 맞는가

판정 규칙:

- 테스트가 부적절하면 코멘트와 함께 스펙을 `queued`로 되돌린다.
- 테스트가 적절하면 status를 유지한 채 Tester로 내부 handoff 한다.

Test Validator는 구현을 직접 완료했다고 간주하지 않는다.

### 3. Tester (`gpt-5-mini`)

Tester는 증거를 보고 condition과 최종 상태를 확정한다.

- 테스트 증거와 실행 로그를 확인한다.
- 각 condition이 통과했는지 판단한다.
- condition status를 갱신한다.
- 스펙 최종 status를 결정한다.

판정 규칙:

- 모든 condition이 통과하면 feature는 `verified`, task는 `done`
- 질문이 있거나 사용자 테스트가 필요하면 `needs-review`
- 질문이 없고 모든 수동 테스트가 통과하면 feature는 `verified`, task는 `done`
- 질문이 없고 어느 하나의 테스트라도 실패했다면 개선 방향 코멘트 후 `queued`

Tester만 condition status와 최종 spec status를 확정한다.

## 상태 모델

### spec status

스펙 status는 외부에서 보이는 작업 흐름만 표현한다.

- `draft`: 아직 작업 준비 전
- `queued`: 다음 자동 처리 사이클이 다시 시도해야 하는 상태
- `working`: Developer, Test Validator, Tester 파이프라인 내부에서 처리 중인 상태
- `needs-review`: Tester까지 끝났고, 이제 사용자 질문 응답 또는 사용자 테스트가 필요한 상태
- `verified`: feature 최종 검증 완료
- `done`: task 최종 완료
- `deprecated`: 기존 의미 유지

중요한 점은 `working`의 범위를 넓게 잡는 것이다.
Developer가 구현을 끝냈더라도 Test Validator와 Tester가 같은 자동 파이프라인 안에서 이어서 처리 중이라면 spec은 계속 `working`에 머문다.

즉 이 문서 기준의 권장 흐름은 아래와 같다.

- spec: `draft -> queued -> working -> queued|needs-review|verified|done`

문서나 대화에서 `queue`라고 부르더라도 실제 저장 값은 항상 `queued`를 사용한다.

`working -> needs-review`는 더 이상 일반적인 구현 종료 handoff가 아니다.
오직 Tester가 사용자 행동 필요를 확정했을 때만 발생한다.

### condition status

condition status는 검증 상태를 나타낸다.

- `draft`: 아직 검증 완료되지 않음. 자동 재시도 대상도 이 상태에 머무를 수 있다.
- `needs-review`: 해당 condition에 대해 사용자 확인, 질문 응답, 수동 테스트가 필요함
- `verified`: Tester가 통과를 확인함

즉 condition의 권장 흐름은 아래와 같다.

- condition: `draft -> verified|needs-review`

condition 실패는 기본적으로 `draft`로 되돌린다.
`needs-review`는 사용자 확인, 질문 응답, 수동 테스트가 필요한 경우에만 쓴다.
한 번 `verified`였던 condition도 spec이 다시 `queued`로 가면 `draft`로 되돌린다.

## 순차 파이프라인

### 1. 작업 시작

- planner 또는 큐 로직이 spec을 `queued`로 둔다.
- Runner가 spec을 집으면 `working`으로 바꾼다.
- 이 시점부터 내부 파이프라인이 끝날 때까지 외부 status는 기본적으로 `working`을 유지한다.

### 2. Developer 단계

- condition 기준 테스트를 먼저 추가하거나 보강한다.
- 구현 후 테스트를 실행한다.
- 실패하면 수정 후 재실행을 반복한다.
- 테스트 결과와 증거를 스펙에 동기화한다.
- `activity`에 작업 내용을 append 한다.

이 단계만 끝났다고 `needs-review`로 바꾸지 않는다.

### 3. Test Validator 단계

- 테스트가 요구사항을 제대로 검증하는지 점검한다.
- 부적절하면 `activity`에 이유를 남기고 spec을 `queued`로 되돌린다.
- 적절하면 `activity`에 승인 의견을 남기고 Tester로 넘긴다.

### 4. Tester 단계

- 실행 로그, 테스트 결과, evidence를 확인한다.
- condition별 통과 여부를 확정한다.
- 최종 spec status를 결정한다.

결정표:

1. 모든 condition 통과
   - feature: `verified`
   - task: `done`
2. 사용자 질문 응답 또는 사용자 테스트 필요
   - spec: `needs-review`
   - 관련 condition: `needs-review`
3. 사용자 행동은 필요 없지만 테스트 실패, 실행 불가, 증거 부족
   - spec: `queued`
  - 실패한 condition: `draft`
   - 관련 코멘트와 다음 시도 방향 기록

## `needs-review`의 의미 축소

이 문서의 가장 중요한 규칙은 `needs-review`를 좁게 쓰는 것이다.

`needs-review`는 아래 상황에서만 사용한다.

- 사용자가 질문에 답해야 함
- 사용자가 직접 테스트해야 함
- 사용자의 운영 판단 또는 확인이 필요함

아래 상황은 `needs-review`가 아니라 `queued`가 더 자연스럽다.

- 테스트가 깨졌고 자동으로 다시 고칠 수 있음
- 테스트 증거가 부족해 다음 자동 시도가 필요함
- 구현이 덜 되어 있어 재작업이 필요함
- 실행 중 프로세스가 죽어서 자동 복구 또는 재시도가 필요함
- 사용자가 모든 질문에 이미 답했음
- 질문은 없고 수동 테스트 중 하나라도 실패했음

즉 `needs-review`는 "자동 파이프라인이 끝났고 이제 사용자 차례"를 의미한다.

## `activity` 스키마

상태 전이를 단순하게 유지하려면, 내부 handoff와 판단 근거는 status가 아니라 append-only `activity`에 남겨야 한다.

`activity`는 spec 최상위 배열로 둔다.

```json
"activity": []
```

각 항목은 시간순 append-only를 원칙으로 한다.
기존 항목 수정은 지양하고, 정정이 필요하면 새 항목을 추가한다.

### 필드 정의

- `at`: ISO 8601 시각. 필수.
- `role`: `planner` | `developer` | `test-validator` | `tester` | `system` | `user`. 필수.
- `actor`: 실행 주체 식별자. 예: `runner-31234`, `copilot-cli`, `user`. 권장.
- `model`: 사용한 모델명. 예: `gpt-5.4`, `gpt-5-mini`. 권장.
- `summary`: 한두 문장 요약. 필수.
- `comment`: 상세 판단이나 메모. 선택.
- `artifacts`: 관련 파일 경로 목록. 선택.
- `issues`: 구조화된 이슈 코드 목록. 선택.
- `conditionUpdates`: condition 판정 결과 목록. 선택.
- `statusChange`: spec status 변경이 있었다면 `{ "from": "...", "to": "..." }`. 선택.
- `kind`: activity의 구조화된 유형. 예: `create`, `mutate`, `supersede`, `deprecate`, `restore`, `implementation`, `validation`, `verification`, `recovery`. 선택.
- `relatedIds`: 관련 스펙 ID 목록. 선택.
- `outcome`: 이번 단계의 결론. 필수.

`changeLog`는 사용하지 않는다.
새 포맷에서는 스펙 변경 이력과 handoff 이력을 모두 `activity`에 기록한다.

### `outcome` 권장 값

- `handoff`: 다음 역할로 정상 전달
- `requeue`: 자동 재시도를 위해 `queued`로 복귀
- `needs-review`: 사용자 행동 필요로 `needs-review` 전환
- `verified`: feature 최종 통과
- `done`: task 최종 완료
- `blocked`: 판단은 했지만 현재 단계에서 종료되지 못함

허용 vocabulary는 권장 수준이 아니라 구현 시 닫힌 집합으로 취급한다.
허용되지 않은 값은 validator에서 에러 또는 최소 warning으로 다룬다.

### `issues` 권장 값

- `spec-misaligned-test`
- `trivial-test`
- `non-deterministic-test`
- `incorrect-result-interpretation`
- `missing-evidence`
- `execution-crash`
- `test-failed`
- `user-input-required`
- `user-test-required`

구현 시 `issues`, `outcome`, `conditionUpdates.reason`은 아래 고정 집합만 허용하는 것을 기본값으로 한다.

- `issues`: `spec-misaligned-test`, `trivial-test`, `non-deterministic-test`, `incorrect-result-interpretation`, `missing-evidence`, `execution-crash`, `test-failed`, `user-input-required`, `user-test-required`
- `outcome`: `handoff`, `requeue`, `needs-review`, `verified`, `done`, `blocked`
- `conditionUpdates.reason`: `automated-tests-passed`, `manual-tests-passed`, `test-failed`, `user-input-required`, `user-test-required`, `missing-evidence`, `execution-crash`, `reset-for-requeue`

### `conditionUpdates` 스키마

`conditionUpdates`는 Tester가 주로 사용한다.

```json
{
  "conditionId": "F-123-C1",
  "status": "verified",
  "reason": "automated-tests-passed",
  "comment": "핵심 경로 테스트와 로그가 모두 일치함"
}
```

필드 규칙:

- `conditionId`: condition ID. 필수.
- `status`: `verified` | `needs-review` | `draft`. 필수.
- `reason`: 구조화된 사유 코드. 권장.
- `comment`: 사람이 읽는 설명. 선택.

### 예시 JSON

```json
{
  "id": "F-123",
  "status": "queued",
  "conditions": [
    {
      "id": "F-123-C1",
      "nodeType": "condition",
      "description": "Given ... When ... Then ...",
      "status": "draft",
      "codeRefs": [],
      "evidence": [],
      "tests": []
    }
  ],
  "activity": [
    {
      "at": "2026-03-09T10:15:00Z",
      "role": "developer",
      "actor": "runner-31001",
      "model": "gpt-5.4",
      "summary": "condition 기준 테스트 2개를 추가하고 구현 후 3회 재실행했다.",
      "comment": "마지막 실행에서 회귀 테스트는 통과했지만 로그 수집이 불안정했다.",
      "artifacts": [
        "docs/evidence/F-123/runner-tests/20260309-101500/runner-tests.trx",
        "docs/evidence/F-123/test-plan.md"
      ],
      "outcome": "handoff"
    },
    {
      "at": "2026-03-09T10:22:00Z",
      "role": "test-validator",
      "actor": "copilot-cli",
      "model": "gpt-5-mini",
      "summary": "테스트가 핵심 실패 경로를 검증하지 못한다.",
      "comment": "성공 케이스 assertion만 있고 spec이 요구하는 예외 경로 검증이 빠져 있다.",
      "issues": [
        "spec-misaligned-test",
        "trivial-test"
      ],
      "statusChange": {
        "from": "working",
        "to": "queued"
      },
      "outcome": "requeue"
    }
  ]
}
```

사용자 행동이 필요한 경우의 예시는 아래와 같다.

```json
{
  "at": "2026-03-09T11:05:00Z",
  "role": "tester",
  "actor": "copilot-cli",
  "model": "gpt-5-mini",
  "summary": "자동 테스트는 통과했지만 실제 사용자 환경에서 수동 확인이 필요하다.",
  "comment": "OS 권한 팝업은 자동화로 검증할 수 없어 사용자 확인이 필요하다.",
  "issues": [
    "user-test-required"
  ],
  "conditionUpdates": [
    {
      "conditionId": "F-123-C2",
      "status": "needs-review",
      "reason": "user-test-required",
      "comment": "권한 팝업 처리 확인 필요"
    }
  ],
  "statusChange": {
    "from": "working",
    "to": "needs-review"
  },
  "outcome": "needs-review"
}
```

## `activity`와 기존 데이터의 역할 분리

`activity`는 판단 이력과 handoff 기록을 위한 로그다.
테스트와 evidence 자체를 대체하지 않는다.

역할 분리는 아래처럼 두는 편이 자연스럽다.

- `conditions[].tests`: 테스트 실행 결과의 정규화 데이터
- `conditions[].evidence`, `spec.evidence`: 로그, 스크린샷, 결과 파일 등 증거
- `conditions[].metadata`: 현재 condition 상태의 부가 정보
- `spec.metadata`: 현재 spec 상태의 부가 정보
- `activity`: 누가 어떤 근거로 어떤 결론을 냈는지의 시계열 로그

즉 `activity`는 댓글 또는 스레드처럼 읽히되, 기계가 해석할 수 있는 구조를 유지해야 한다.

## 포맷 전환 정책

기존 스펙 JSON의 `changeLog`는 제거 대상으로 본다.
기존 JSON은 백업만 하고, 사용자는 새 포맷으로 스펙을 다시 등록한다.
이번 전환에서는 기존 `changeLog`를 `activity`로 자동 migration하지 않는다.

## 운영 규칙

### 1. status보다 `activity`를 우선 신뢰한다

내부 파이프라인 handoff는 모두 `activity`에 남기고, status는 최소한으로만 바꾼다.

### 2. `needs-review`는 Tester만 설정한다

Developer와 Test Validator는 `needs-review`를 직접 만들지 않는다.

### 3. 자동 재시도 대상은 `queued`로 돌린다

사용자 행동이 필요하지 않다면 `queued`가 기본값이다.

requeue가 발생하면 condition도 현재 검증 상태를 유지하지 않고 기본적으로 `draft`로 되돌린다.

### 4. 최종 판정은 Tester만 한다

feature의 `verified`, task의 `done`, 사용자 대기 상태인 `needs-review`는 Tester만 확정한다.

### 5. 정정은 덮어쓰지 말고 append 한다

이전 판단이 틀렸다면 기존 `activity` 항목을 바꾸기보다 정정 항목을 새로 추가한다.

### 6. stale `working` 복구는 `queued` + `activity append`

비정상 종료 등으로 stale `working` 스펙이 생기면 `needs-review`로 보내지 않는다.
복구 시에는 `queued`로 되돌리고, 복구 사유를 `activity`에 append 한다.

### 7. worktree 정리 규칙

- `working` 진입 시 worktree를 만든다.
- 이미 worktree가 있으면 기존 것을 정리하고 새로 만든다.
- spec이 `needs-review`, `done`, `verified`가 되면 worktree를 정리한다.

## 구현 시사점

이 기준으로 코드가 바뀌면 다음 방향이 자연스럽다.

- `working` 상태를 구현 단계 전용이 아니라 전체 자동 파이프라인 전용 상태로 사용
- 기존 review loop 개념을 없애고, 하나의 cycle 안의 내부 순차 단계로 재해석
- `needs-review`를 일반 handoff가 아니라 사용자 대기 상태로 축소
- `spec-append-review`류 명령은 status를 직접 많이 바꾸기보다 `activity`와 구조화 데이터를 append
- condition status는 Tester 판정 시점에만 `verified` 또는 `needs-review`로 확정

## 구현 계약

이 문서 기준으로 구현 시 아래 항목은 결정된 것으로 본다.

1. Developer, Test Validator, Tester는 하나의 runner cycle 안에서 순차 호출한다.
2. `changeLog`는 제거하고 `activity`만 사용한다.
3. 기존 JSON은 백업만 하고, 새 포맷으로 다시 등록한다.
4. 사용자가 모든 질문에 답한 경우 spec은 `queued`로 되돌린다.
5. 질문이 없고 모든 수동 테스트가 통과한 경우 feature는 `verified`, task는 `done`으로 간다.
6. 질문이 없고 테스트 하나라도 실패한 경우 spec은 `queued`로 간다.
7. condition 실패는 `draft`다.
8. condition의 `needs-review`는 사용자 확인, 질문, 수동 테스트에만 쓴다.
9. condition이 통과하면 `verified`다.
10. `verified`였던 condition도 spec requeue 시 전체를 `draft`로 되돌린다.
11. stale `working`은 `queued` + `activity append`로 복구한다.
12. worktree는 `working`에서 만들고, 기존 것이 있으면 정리 후 재생성한다.
13. spec이 `needs-review`, `done`, `verified`가 되면 worktree를 정리한다.

## 비목표

이번 기준에서는 아래 항목은 다루지 않는다.

- 새 status 추가
- evidence 포맷 전면 개편
- planner 우선순위 정책 재설계
- dependency propagation 규칙 재설계

## 참고 파일

- `tools/flow-cli/Services/Runner/RunnerService.cs`
- `tools/flow-cli/Services/Runner/RunnerModels.cs`
- `tools/flow-cli/Commands/SpecCommand.cs`
- `tools/flow-cli/Services/TestSync/TestSyncService.cs`
- `tools/flow-cli/Services/SpecGraph/SpecModels.cs`

## Implementation Todo

- [x] 1. 스키마와 validator 정리: `activity` 모델 추가, `changeLog` 제거 방향 반영, vocabulary 검증 추가
- [x] 2. 상태 전이 정리: runner를 `queued -> working -> queued|needs-review|verified|done` 구조로 개편
- [x] 3. 명령 정리: review/condition/manual 입력 경로를 새 상태 모델과 `activity` append 방식으로 정리
- [x] 4. 테스트 정리: runner/spec command/spec validator 테스트를 새 규칙에 맞게 갱신

## Next Session Notes

- 3단계는 반영 완료: `spec-append-review`와 `spec-record-condition-review`가 `queued|needs-review|verified|done` 규칙에 맞춰 status를 결정하고 `activity`를 append하도록 정리되었다.
- 4단계까지 반영 완료: runner/spec command/spec validator 테스트를 새 상태 모델 기준으로 다시 정리했고, `activity` vocabulary와 `queued` requeue 규칙을 검증하는 케이스를 보강했다.
- `spec-append-review`/`spec-record-condition-review` 전용 테스트는 process-global 상태 충돌을 막기 위해 `CommandGlobalState` 컬렉션으로 직렬화했다.
- 다음 시작점은 문서 범위 밖의 후속 정리다: 필요하면 review 메타데이터(`reviewDisposition`, `reviewReason`)를 더 축소하거나, 남아 있는 legacy review loop 보조 메서드를 별도 단계로 제거할 수 있다.