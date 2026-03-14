# Flow Runner Agent Contract

이 문서는 runner와 agent 사이의 최소 계약을 정의한다. 목적은 실제 LLM agent를 붙이기 전에 runner orchestration과 더미 agent 테스트를 안정적으로 시작하는 데 있다.

# 1. 목표

- runner가 어떤 기준으로 rule과 agent를 호출하는지 고정한다.
- agent 입력과 출력의 최소 스키마를 고정한다.
- 더미 agent와 실제 agent가 같은 계약을 따르게 만든다.
- 실패, timeout, no-op를 runner가 일관되게 처리하게 만든다.

추가 목표:

- 멀티 runner 환경에서도 같은 spec이 중복 처리되지 않게 한다.
- agent가 잘못된 이벤트를 제안해도 runner가 런타임에서 차단할 수 있게 한다.
- 오래 걸리는 agent 작업과 사용자 무응답 상황에서도 파이프라인이 영구 정지하지 않게 한다.

# 2. runner 역할

runner는 아래 역할만 담당한다.

- 실행 대상 spec 선택
- spec-level lock 획득과 해제
- 상태/처리 상태 기반 dispatch
- rule evaluator 호출
- agent adapter 호출
- 결과를 activity log로 기록
- proposed event를 적용 후보로 넘김
- lock, timeout, retry 관리
- version 충돌 검증

runner는 아래를 직접 판단하지 않는다.

- 테스트 적합성 판정
- review request 내용 작성
- 상태 전이 규칙 해석
- 사용자 응답 의미 해석
- dependency 상태 판단

이 판단은 각각 Test Validator, Spec Validator, rule evaluator, review contract 계층이 담당한다.

# 3. runner loop

## 3.1 한 루프의 기본 순서

1. 실행 가능한 spec 후보를 조회한다.
2. spec-level lock 또는 version 기반 CAS 획득을 시도한다.
3. 최신 spec과 assignment, review request를 다시 읽는다.
4. stale assignment와 deadline 초과 review request가 있는지 먼저 검사한다.
5. 현재 상태와 처리 상태를 기준으로 dispatch target을 계산한다.
6. rule 또는 agent를 호출한다.
7. 결과를 activity log에 기록한다.
8. proposed event를 rule evaluator에 전달해 상태/처리 상태 변경안을 계산한다.
9. spec version 충돌이 없는지 확인한 뒤 변경안을 저장한다.
10. lock을 해제한다.
11. 다음 spec으로 이동한다.

## 3.2 dispatch 원칙

- 순수 규칙으로 처리 가능한 것은 agent를 호출하지 않는다.
- 상태 변경은 항상 rule evaluator를 통해 계산한다.
- agent는 event producer이지 state mutator가 아니다.
- dependency 판단은 runner가 직접 하지 않고 rule evaluator만 수행한다.
- 같은 spec에 대해 동시에 두 runner가 dispatch하지 못하도록 spec-level lock 또는 CAS를 강제한다.
- 활성 assignment가 있는 spec은 인터럽트 규칙이 없는 한 강제 상태 변경 대상에서 제외한다.

# 4. dispatch 매핑

| 상태 | 처리 상태 | 우선 호출 대상 | 설명 |
| --- | --- | --- | --- |
| 초안 | 대기 | Spec Validator (AC precheck) | AC 테스트 가능성 프리패스 |
| 대기 | 대기 | Architect 또는 Developer assignment rule | risk level에 따라 구현 검토 또는 구현 시작 |
| 대기 | 처리중 | 없음 | 이 조합은 허용하지 않는 방향으로 간다. 실제 구현 착수는 `구현 + 처리중`이어야 한다. |
| 구현 검토 | 대기 | Architect | 구조 검토 |
| 구현 | 대기 | Developer | 구현 시작 |
| 구현 | 처리중 | 없음 | 작업 중 |
| 테스트 검증 | 대기 | Test Validator | 테스트 검증 |
| 검토 | 검토 | Spec Validator | spec 적합성 검증 |
| 검토 | 사용자검토 | 없음 | 사용자 응답 대기 |
| 검토 | 사용자검토 | review timeout rule | deadline 초과 시 실패 또는 기본 정책 적용 |
| 활성 | 완료 | Spec Manager rule | 완료 처리 가능 여부 계산 |

설명:

- `없음`은 새 agent 호출 없이 timeout 검사 또는 외부 입력 대기를 의미한다.
- 실제 구현에서는 상태와 처리 상태 외에 assignment 존재 여부도 dispatch에 포함해야 한다.
- 실제 구현에서는 상태와 처리 상태 외에 `active assignment`, `review deadline`, `spec version`도 dispatch 입력에 포함해야 한다.

# 5. agent adapter 인터페이스

## 5.1 입력

모든 agent는 공통 envelope를 입력으로 받는다.

```json
{
  "agentRole": "developer",
  "spec": {},
  "activeAssignment": {},
  "recentActivity": [],
  "recentSummary": {},
  "openReviewRequests": [],
  "context": {
    "projectId": "proj-001",
    "runId": "run-001",
    "loopId": "loop-003",
    "attempt": 1,
    "currentVersion": 1
  }
}
```

필수 보장:

- `spec`은 최신 저장 상태다.
- `recentActivity`는 최근 필요한 범위만 포함한다. 전체 event stream을 넣지 않는다.
- `recentSummary`는 runner가 압축한 최근 상태 요약이다.
- `activeAssignment`는 없을 수 있다.
- `openReviewRequests`는 없을 수 있다.

## 5.2 출력

모든 agent는 아래 구조를 반환한다.

```json
{
  "result": "success",
  "baseVersion": 1,
  "proposedEvent": {
    "type": "implementation_submitted",
    "summary": "implementation done",
    "payload": {}
  },
  "artifacts": [],
  "evidence": [],
  "message": "developer completed assignment"
}
```

필수 규칙:

- agent는 직접 상태를 바꾸지 않는다.
- agent는 event proposal만 반환한다.
- 초기 구현에서는 한 번에 하나의 `proposedEvent`만 허용한다.
- runner는 반환값을 그대로 신뢰하지 않고 schema와 허용 event를 검증한다.
- runner는 `baseVersion`이 현재 spec version과 일치하는지 검증한다.

단일 event만 허용하는 이유는 아래와 같다.

- agent가 한 번에 여러 phase를 건너뛰는 것을 막는다.
- rule evaluator의 event 적용 순서를 단순하게 유지한다.
- 초기 테스트를 table-driven으로 만들기 쉽다.

# 6. agent별 허용 이벤트

## 6.1 Planner

- `draft_created`
- `draft_updated`

## 6.2 Architect

- `architect_review_passed`
- `architect_review_rejected`

## 6.3 Developer

- `implementation_submitted`

## 6.4 Test Validator

- `test_validation_passed`
- `test_validation_rejected`

## 6.5 Spec Validator

- `ac_precheck_passed`
- `ac_precheck_rejected`
- `spec_validation_passed`
- `spec_validation_rework_requested`
- `spec_validation_user_review_requested`
- `spec_validation_failed`

## 6.6 Spec Manager

Spec Manager는 agent라기보다 rule owner에 가깝다. 초기 구현에서는 실제 adapter 호출 없이 rule evaluator 내부에서 처리하는 편이 낫다.

## 6.7 런타임 권한 검증

runner는 아래 두 축을 모두 검증해야 한다.

1. role permission
2. state permission

예:

- Developer는 `implementation_submitted`를 제안할 수 있다.
- 하지만 현재 state가 `구현`이 아니면 이 이벤트는 거부해야 한다.
- Spec Validator는 `spec_validation_passed`를 제안할 수 있다.
- 하지만 현재 state가 `검토`가 아니면 이 이벤트는 거부해야 한다.

# 7. 결과 처리 규칙

## 7.1 `success`

- `baseVersion`이 현재 spec version과 일치하는지 먼저 확인한다.
- `proposedEvent`가 role permission과 state permission을 모두 만족하는지 검증한다.
- 검증 통과 시 `proposedEvent`를 rule evaluator에 전달한다.
- 활동 로그를 남긴다.
- artifacts/evidence를 저장한다.

## 7.2 `retryable-failure`

- 활동 로그를 남긴다.
- assignment retry count를 증가시킨다.
- 규칙이 허용하면 같은 단계 재시도를 예약한다.

retry는 무한 반복하면 안 된다.

- assignment는 `retryCount`와 `maxRetry`를 가져야 한다.
- `retryCount >= maxRetry`면 `terminal-failure`처럼 처리한다.
- backoff를 두는 편이 좋다.

## 7.3 `terminal-failure`

- 활동 로그를 남긴다.
- assignment를 실패 종료한다.
- 필요 시 review request를 생성한다.

## 7.4 `no-op`

- 활동 로그만 남긴다.
- 상태 전이는 하지 않는다.

# 8. timeout과 heartbeat 계약

- `running` assignment를 가진 agent는 heartbeat를 갱신해야 한다.
- runner는 각 루프 시작 시 stale 여부를 검사한다.
- heartbeat 갱신 실패는 agent 실패가 아니라 assignment timeout으로 처리한다.
- timeout 이후 상태 변경은 agent가 아니라 rule evaluator가 수행한다.

초기 구현 메모:

- 실제 LLM 호출은 push heartbeat를 제공하기 어렵다.
- 따라서 초기 구현에서는 runner가 assignment 상태를 `queued -> running -> submitted`처럼 관리하고, timeout은 agent 호출 전체 시간 기준으로 계산하는 편이 현실적이다.
- 장기적으로 worker 분리 시 heartbeat를 별도 프로세스가 관리할 수 있다.

# 9. review request timeout 계약

- review request는 `deadlineAt`을 가질 수 있다.
- runner는 각 루프에서 deadline 초과 review request를 검사한다.
- deadline 초과 시 rule evaluator에 timeout event를 전달해 실패 처리 또는 기본 정책 적용을 수행한다.
- 사용자 무응답은 무기한 대기가 아니라 명시적 정책 대상이어야 한다.

# 10. 동시성 제어 계약

- 멀티 runner 환경에서는 같은 spec에 대한 동시 dispatch를 금지한다.
- 최소 요구사항은 spec-level lock이다.
- lock이 어렵다면 `version` 기반 CAS를 사용해야 한다.
- 가장 안전한 순서는 `lock 획득 -> 최신 spec 로드 -> dispatch -> 저장 -> lock 해제`다.
- 저장 직전 version이 바뀌었으면 runner는 결과 적용을 거부하고 conflict 로그를 남긴다.

강제 상태 변경이나 삭제 요청이 들어온 경우:

- 활성 assignment가 없으면 즉시 처리할 수 있다.
- 활성 assignment가 있으면 cancel signal을 기록하고 assignment를 `cancelled` 또는 `failed`로 종료한 뒤 lock을 해제해야 한다.
- 이 인터럽트 규칙 없이 비즈니스 상태만 바꾸면 orphan assignment가 생긴다.

# 11. 더미 agent 구현 원칙

- 더미 agent는 입력에 따라 deterministic한 출력을 반환해야 한다.
- fixture spec ID 또는 상태를 기준으로 고정 응답을 돌려주면 충분하다.
- 더미 agent에서 이미 schema validation을 통과하도록 만들어 실제 agent 교체 시 차이를 줄이는 편이 좋다.

# 12. 테스트 우선순위

1. dispatch가 올바른 agent를 고르는지 검증
2. agent가 허용되지 않은 event를 반환하면 차단되는지 검증
3. role permission은 맞지만 state permission이 틀린 event를 차단하는지 검증
4. version mismatch 결과를 차단하는지 검증
5. retryable-failure가 retry limit 안에서만 재시도로 이어지는지 검증
6. terminal-failure가 실패 종료로 이어지는지 검증
7. no-op가 상태를 바꾸지 않는지 검증
8. timeout이 stale assignment recovery로 이어지는지 검증
9. review request deadline 초과가 timeout policy로 이어지는지 검증
10. 멀티 runner에서 같은 spec이 중복 dispatch되지 않는지 검증

# 13. 구현 메모

- 초기 구현에서는 agent adapter를 인터페이스 하나로 추상화하는 편이 좋다.
- 실제 LLM adapter, 더미 adapter, 테스트 adapter가 같은 인터페이스를 구현하게 만들면 runner 테스트가 단순해진다.
- Spec Manager는 실제 agent 호출보다 rule evaluator의 일부로 두는 편이 더 자연스럽다.
- activity는 전체 event stream을 agent 입력에 직접 넣지 말고, 최근 event window + runner summary를 함께 전달하는 편이 좋다.
- dependency 계산 로직은 runner에 두지 말고 rule evaluator 또는 dedicated dependency evaluator 한 곳에만 두는 편이 안전하다.