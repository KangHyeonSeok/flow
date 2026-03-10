# Runner Spec/Condition Status Flow

## 목적

Flow Runner에서 spec 상태와 condition 상태가 언제, 왜 바뀌는지 한 문서에서 읽을 수 있게 정리한다.

이 문서는 다음 세 가지를 고정한다.

- spec 상태와 condition 상태의 의미
- 질문 답변, 수동 검증, 자동 재시도 시의 canonical 전이
- VS Code extension이 runner 의미를 깨지 않고 상태를 표시하거나 저장해야 하는 규칙

## 핵심 원칙

1. spec의 최종 판정은 runner review 단계가 담당한다.
2. condition은 검증 단위를 표현하고, spec은 작업 파이프라인 위치를 표현한다.
3. open question이 남아 있으면 spec은 자동 확정 대상이 아니다.
4. 질문에 답했다고 해서 extension이 spec을 바로 queued로 되돌리면 안 된다.
5. 자동 재시도로 되돌릴 때만 condition 전체를 draft로 초기화한다.

## 상태 의미

### spec status

| status | 의미 |
|---|---|
| `draft` | 아직 작업 대상으로 올리지 않은 초안 |
| `queued` | 자동 구현 또는 자동 재시도 대기 |
| `working` | runner가 구현/검증 파이프라인을 진행 중 |
| `needs-review` | 사용자 질문 응답 또는 사용자 수동 검증이 필요한 상태 |
| `verified` | feature 최종 검증 완료 |
| `done` | task 최종 완료 |
| `deprecated` | 워크플로우에서 제외 |

### condition status

| status | 의미 |
|---|---|
| `draft` | 아직 충족을 확정하지 못함 |
| `needs-review` | 사용자 질문, 수동 검증, 실패 원인 확인 등 review 맥락이 남아 있음 |
| `verified` | 증거와 테스트 또는 수동 검증으로 충족 확정 |

## Canonical 전이

### 구현 시작

- spec: `queued -> working`
- condition: 유지

### 자동 검증 실패 또는 자동 재시도

- spec: `working|needs-review -> queued`
- condition: 전체 `draft`로 초기화
- 이유: 다음 구현 시도 전 검증 상태를 다시 수집해야 하기 때문

### 사용자 질문 또는 사용자 수동 검증 필요

- spec: `working -> needs-review`
- condition:
  - 이미 `verified`인 condition은 유지
  - 질문 또는 수동 검증이 필요한 condition은 `needs-review`
  - 그 외 미확정 condition은 `draft` 또는 review evaluator 기준 `needs-review`로 표시될 수 있으나, 사용자 개입이 필요한 condition만 우선적으로 강조한다

### 질문 답변 저장

- open question이 남아 있으면:
  - spec은 `needs-review` 유지
  - `questionStatus=waiting-user-input`
  - `reviewDisposition=open-question`
- 모든 질문에 답했으면:
  - spec은 즉시 `queued`로 바꾸지 않는다
  - spec은 `needs-review`로 유지한 채 runner가 다음 review pass에서 `verified|done|queued`를 최종 재판정한다
  - `questionStatus`만 제거한다

### 자동 확정

- feature:
  - 모든 condition `verified`
  - open question 없음
  - manual verification requirement 없음
  - 결과: `needs-review -> verified`
- task:
  - 위와 동일한 조건 또는 condition 없음
  - 결과: `needs-review -> done`

## UI 계산 규칙

extension UI는 다음 규칙을 따라야 한다.

1. `autoVerifyEligible`은 아래를 모두 만족할 때만 true다.
   - 모든 condition이 `verified`
   - open question이 0개
   - 수동 검증 항목이 0개
2. open question이 남아 있으면 "모든 조건 충족, Runner 자동 검증 대상" 문구를 보여주면 안 된다.
3. reviewDisposition은 화면에 이유 설명용으로 노출할 수 있지만, extension이 자체 어휘(`needs-user-decision`, `retry-queued`)를 새로 만들면 안 된다.
4. 질문 답변 저장은 runner와 같은 의미를 유지해야 하며, spec과 condition 상태를 독자적으로 final state로 밀어붙이면 안 된다.

## Anti-pattern

다음은 피해야 한다.

- 질문이 모두 답변되었다는 이유만으로 extension이 spec을 즉시 `queued`로 변경
- open question이 남아 있는데 UI가 auto verify eligible로 계산
- reviewDisposition에 runner가 쓰지 않는 임시 값을 계속 누적
- requeue가 아닌데 condition 전체를 `draft`로 초기화

## 구현 체크리스트

- 질문 답변 저장 후 open question이 남아 있으면 spec은 `needs-review` 유지
- 질문 답변 저장 후 open question이 0개여도 spec은 runner 재판정 전까지 `needs-review` 유지
- auto verify 배지는 open question과 manual verification을 모두 고려
- reviewDisposition은 runner vocabulary로 정규화하거나 제거