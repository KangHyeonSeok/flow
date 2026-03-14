# Flow State Rule

이 문서는 Flow의 상태 전이 규칙을 구현 가능한 수준으로 고정하기 위한 문서다. 목적은 설명이 아니라 테스트 가능한 규칙표를 만드는 데 있다.

# 1. 목표

- 상태 전이 규칙을 deterministic하게 정의한다.
- 상태와 처리 상태를 분리한다.
- 누가 무엇을 바꿀 수 있는지 고정한다.
- runner와 rule evaluator가 같은 규칙을 사용하게 만든다.
- 테스트 코드의 입력 표로 바로 사용할 수 있게 만든다.

이 문서는 FSM과 workflow를 혼합해서 쓰되, 의미 충돌이 없도록 아래 원칙을 따른다.

- 비즈니스 상태는 "지금 어떤 단계에 있는가"를 나타낸다.
- 처리 상태는 "그 단계의 실행과 검증이 어디까지 왔는가"를 나타낸다.
- 실행 이벤트가 실제 단계 진입을 의미할 때만 비즈니스 상태를 바꾼다.

# 2. 공통 원칙

- 스펙 상태는 Spec Manager만 변경할 수 있다.
- 처리 상태는 Spec Validator가 검증 관점에서 변경할 수 있다.
- 다른 Agent는 상태 변경 요청에 필요한 결과와 근거만 남긴다.
- 사용자와 webservice는 review request 응답만 기록하고 상태를 직접 바꾸지 않는다.
- 상태 전이는 항상 활동 로그와 함께 기록한다.

# 3. 용어

## 3.1 상태

상태는 비즈니스 흐름상의 현재 단계를 나타낸다.

허용 값:

- 초안
- 대기
- 구현 검토
- 구현
- 테스트 검증
- 검토
- 활성
- 실패
- 완료
- 보관

## 3.2 처리 상태

처리 상태는 현재 단계에서 담당자의 작업, 검증, 사용자 검토가 어디까지 왔는지 나타낸다.

허용 값:

- 대기
- 처리중
- 검토
- 사용자검토
- 완료
- 실패
- 보류

## 3.3 이벤트

상태 전이는 이벤트 기반으로 계산한다. 초기 구현에서는 아래 이벤트만 고정하면 충분하다.

- draft_created
- draft_updated
- ac_precheck_passed
- ac_precheck_rejected
- architect_review_passed
- architect_review_rejected
- assignment_started
- implementation_submitted
- test_validation_passed
- test_validation_rejected
- spec_validation_passed
- spec_validation_rework_requested
- spec_validation_user_review_requested
- user_review_submitted
- spec_validation_failed
- spec_completed
- cancel_requested
- dependency_blocked
- dependency_failed
- dependency_resolved
- assignment_timed_out
- assignment_resumed
- review_request_timed_out
- rollback_requested
- spec_archived

# 4. 책임 경계

## 4.1 Planner

- `초안` 상태의 spec을 만든다.
- acceptance criteria와 scope를 제안한다.
- 상태를 직접 변경하지 않는다.

## 4.2 Architect

- acceptance criteria의 기술적 구현 가능성과 아키텍처 영향을 검토한다.
- `구현 검토` 단계에서 구조 검토를 수행한다.
- 상태를 직접 변경하지 않는다.

## 4.3 Developer

- 구현과 테스트 실행 결과를 제출한다.
- 상태를 직접 변경하지 않는다.

## 4.4 Test Validator

- 테스트의 적합성과 합격 여부를 판정한다.
- 상태를 직접 변경하지 않는다.

## 4.5 Spec Validator

- 처리 상태를 검증 관점에서 제어한다.
- Planner 초안의 acceptance criteria와 필수 필드의 형식적 완결성을 검토한다.
- review request 생성 여부를 판정한다.
- 재작업, 사용자검토, 실패를 판정한다.

## 4.6 Spec Manager

- 상태 변경의 유일한 권한자다.
- 처리 상태와 이벤트를 보고 기계적으로 상태를 이동시킨다.
- dependency cascade, timeout recovery 같은 운영 규칙을 적용한다.

## 4.7 이벤트 발생 주체

이벤트는 아래처럼 발생 주체를 고정한다.

- Planner: `draft_created`, `draft_updated`
- Spec Validator: `ac_precheck_passed`, `ac_precheck_rejected`, `spec_validation_passed`, `spec_validation_rework_requested`, `spec_validation_user_review_requested`, `spec_validation_failed`
- Architect: `architect_review_passed`, `architect_review_rejected`
- runner 또는 scheduler: `assignment_started`, `assignment_timed_out`, `assignment_resumed`, `review_request_timed_out`, `dependency_blocked`, `dependency_failed`, `dependency_resolved`
- Developer: `implementation_submitted`
- Test Validator: `test_validation_passed`, `test_validation_rejected`
- 사용자 또는 운영 입력: `user_review_submitted`, `rollback_requested`, `cancel_requested`, `spec_completed`, `spec_archived`

`spec_activated`는 rule evaluator의 입력 이벤트가 아니라 activity log의 `action` 필드에 기록하는 운영 마커다. `spec_validation_passed`가 `검토 -> 활성` 전이를 유발할 때 side effect로 `LogActivity(action: spec_activated)`를 추가한다.

# 5. 상태 전이 규칙표

## 5.1 상태 전이 표

| 현재 상태 | 입력 이벤트 | 선행 조건 | 다음 상태 | 부수 효과 |
| --- | --- | --- | --- | --- |
| 초안 | draft_updated | Planner가 초안을 수정 | 초안 | 활동 로그 기록, AC precheck 재수행 대상 |
| 초안 | ac_precheck_passed | AC 테스트 가능 | 대기 | 활동 로그 기록 |
| 초안 | ac_precheck_rejected | AC 모호 또는 검증 불가 | 초안 | Planner 보완 요청 생성 |
| 대기 | assignment_started | risk level이 `low` 또는 Architect 생략 허용 | 구현 | 구현 assignment 시작, 활동 로그 기록 |
| 대기 | assignment_started | risk level이 `medium` 이상이고 Architect review 필요 | 구현 검토 | Architect review assignment 시작 |
| 구현 검토 | architect_review_passed | 구조 검토 통과 | 구현 | 구현 assignment 생성, 활동 로그 기록 |
| 구현 검토 | architect_review_rejected | 구조 검토 미통과, architectReviewLoopCount ≤ 3 | 구현 검토 | Planner 보완 assignment 생성, architectReviewLoopCount 증가 |
| 구현 검토 | architect_review_rejected | architectReviewLoopCount > 3 | 실패 | 실패 로그, review request 생성 |
| 구현 검토 | draft_updated | Planner가 보완 완료 | 구현 검토 | Architect 재검토 assignment 생성 |
| 구현 | implementation_submitted | Developer 결과 제출 | 테스트 검증 | Test Validator 입력 생성 |
| 테스트 검증 | test_validation_passed | 테스트 적합성/합격 판정 완료 | 검토 | Spec Validator 입력 생성 |
| 테스트 검증 | test_validation_rejected | 테스트 부족 또는 실패 | 구현 | 재작업 요청 기록 |
| 검토 | spec_validation_passed | Spec Validator 완료 판정 | 활성 | 상태 전이 로그 기록 |
| 검토 | spec_validation_rework_requested | 재작업 필요 | 구현 | 재작업 assignment 재큐잉 |
| 검토 | spec_validation_user_review_requested | 사용자 판단 필요 | 검토 | review request 생성 |
| 검토 | spec_validation_failed | 3회 초과 비수렴 또는 terminal failure | 실패 | 실패 로그, 후속 선택지 생성 |
| 검토 | review_request_timed_out | 사용자검토 중 deadline 초과 | 실패 | review request를 `closed`로 변경, 실패 로그 기록, 후속 선택지 생성 |
| 활성 | rollback_requested | 사용자 요청 또는 운영상 치명적 이슈 | 검토 | review request 생성, 필요한 경우 재계획 |
| 활성 | spec_completed | 사용자 명시 승인, processingStatus=완료, 열린 review request 없음 | 완료 | 완료 로그 기록 |
| 초안 | cancel_requested | 사용자 취소 | 실패 | 활동 로그 기록 |
| 대기 | cancel_requested | 사용자 취소 | 실패 | 활동 로그 기록 |
| 구현 검토 | cancel_requested | 사용자 취소 | 실패 | 활성 assignment CancelAssignment, 활동 로그 기록 |
| 구현 | cancel_requested | 사용자 취소 | 실패 | 활성 assignment CancelAssignment, worktree 해제, 활동 로그 기록 |
| 테스트 검증 | cancel_requested | 사용자 취소 | 실패 | 활성 assignment CancelAssignment, 활동 로그 기록 |
| 검토 | cancel_requested | 사용자 취소 | 실패 | 열린 review request CloseReviewRequest, 활동 로그 기록 |
| 실패 | spec_archived | Planner 재등록 완료 또는 사용자 폐기 결정 | 보관 | 관련 파일을 archived 디렉토리로 이동, 활동 로그 기록 |
| 모든 상태 | dependency_blocked | upstream blocker 미해결 | 현재 상태 유지 | 처리 상태 `보류`, 활동 로그 기록 |
| 모든 상태 | dependency_failed | upstream blocker 최종 실패 | 현재 상태 유지 | 처리 상태 `보류`, review request 생성 |
| 모든 상태 | dependency_resolved | upstream blocker 해소 | 현재 상태 유지 | 처리 상태 `대기` 또는 `검토` 재계산 |

설명:

- `대기 -> assignment_started -> 구현`은 실제 구현 착수를 의미한다. 구현이 시작됐는데 상태가 여전히 `대기`인 모순을 피하기 위해 상태를 함께 이동시킨다.
- `검토 -> spec_validation_user_review_requested`도 상태는 `검토`에 머무르고 처리 상태만 `사용자검토`로 이동한다.
- `활성 -> rollback_requested -> 검토`는 운영 중 역방향 수정 루프를 허용하기 위한 안전장치다.
- `활성 -> 완료`는 서비스 특성에 따라 생략 가능하지만, 현재 문서에서는 명시적 완료 단계를 유지한다.
- `실패 -> 보관`은 Planner 재등록이 완료되었거나 사용자가 폐기를 결정한 후에만 허용한다. 보관 상태의 spec은 runner cycle에서 제외되지만 파일은 archived 디렉토리에 보존된다.

## 5.2 상태 금지 규칙

아래 전이는 금지한다.

- `초안 -> 구현`
- `초안 -> 테스트 검증`
- `대기 -> 완료`
- `구현 -> 완료`
- `테스트 검증 -> 완료`
- `실패 -> 완료`
- `활성 -> cancel_requested` (활성은 rollback_requested 사용)
- `실패 -> cancel_requested`
- `완료 -> cancel_requested`
- `보관 -> cancel_requested`
- `보관 -> *` (보관은 영구 종단 상태, 역방향 전이 금지)
- `초안 -> 보관` (실패를 거치지 않고 직접 보관 금지)
- `대기 -> 보관`
- `구현 검토 -> 보관`
- `구현 -> 보관`
- `테스트 검증 -> 보관`
- `검토 -> 보관`
- `활성 -> 보관`
- `완료 -> 보관`
- `사용자 응답만으로 상태 직접 변경`
- `Developer 결과만으로 상태 직접 변경`

# 6. 처리 상태 규칙표

## 6.1 처리 상태 전이 표

| 현재 처리 상태 | 입력 이벤트 | 선행 조건 | 다음 처리 상태 | 결정 주체 |
| --- | --- | --- | --- | --- |
| 대기 | assignment_started | assignment 생성 및 lock 확보 | 처리중 | runner |
| 처리중 | architect_review_passed | Architect 구조 검토 통과 (구현 검토 상태) | 대기 | runner |
| 처리중 | architect_review_rejected | Architect 구조 검토 미통과 (구현 검토 상태) | 대기 | runner |
| 처리중 | implementation_submitted | 담당자 작업 제출 (구현 상태) | 대기 | runner |
| 처리중 | assignment_timed_out | heartbeat 초과 | 실패 | Spec Manager |
| 처리중 | test_validation_passed | Test Validator 합격 판정 (테스트 검증 상태) | 검토 | runner |
| 처리중 | test_validation_rejected | Test Validator 재작업 판정 (테스트 검증 상태) | 대기 | runner |
| 검토 | spec_validation_passed | 검증 적합 | 완료 | Spec Validator |
| 검토 | spec_validation_rework_requested | 재작업 필요 | 대기 | Spec Validator |
| 검토 | spec_validation_user_review_requested | 사용자 판단 필요 | 사용자검토 | Spec Validator |
| 검토 | spec_validation_failed | terminal failure 판정 | 실패 | Spec Validator |
| 사용자검토 | user_review_submitted | 유효한 사용자 응답 수신 | 검토 | runner |
| 사용자검토 | review_request_timed_out | deadline 초과 | 실패 | runner |
| 모든 처리 상태 | dependency_blocked | upstream blocker 미해결 | 보류 | Spec Manager |
| 모든 처리 상태 | dependency_failed | upstream blocker 최종 실패 | 보류 | Spec Manager |
| 보류 | dependency_resolved | blocker 해소, state≠검토 | 대기 | Spec Manager |
| 보류 | dependency_resolved | blocker 해소, state=검토 | 검토 | Spec Manager |
| 실패 | assignment_resumed | 명시적 재시도 승인 | 대기 | Spec Manager |
| 모든 처리 상태 | cancel_requested | 사용자 취소 | 실패 | Spec Manager |

## 6.2 처리 상태 반복 규칙

- `검토 -> 사용자검토 -> 검토` 루프는 최대 3회까지 허용한다 (`retryCounters.userReviewLoopCount`).
- `검토 -> 대기(재작업) -> 처리중 -> 검토` 루프도 최대 3회까지 허용한다 (`retryCounters.reworkLoopCount`).
- `구현 검토 -> architect_review_rejected -> 구현 검토` 루프도 최대 3회까지 허용한다 (`retryCounters.architectReviewLoopCount`).
- 세 루프의 횟수는 독립적으로 카운트한다.
- 어느 카운터든 3을 초과하면 해당 이벤트를 거부하고 `spec_validation_failed`로 대체하여 `실패`로 전환한다.
- `spec_validation_passed` 수용 시 `userReviewLoopCount`와 `reworkLoopCount`를 0으로 리셋한다.
- `architect_review_passed` 수용 시 `architectReviewLoopCount`를 0으로 리셋한다.
- forward phase 전환 시 모든 카운터를 0으로 리셋한다.
- backward phase 전환 시 카운터를 유지한다.
- `실패` 전환 시 review request 또는 활동 로그에 실패 사유와 후속 선택지를 남긴다.
- `보류`는 외부 의존성, 정책 대기, 환경 문제처럼 현재 spec 자체 노력만으로 진행할 수 없는 경우에 사용한다.

카운터는 `spec.json`의 `retryCounters` 필드에 저장한다. 모든 값이 0이면 저장 시 생략 가능하다.

## 6.3 phase 전환 시 처리 상태 초기값

phase 전환이 발생하면 처리 상태는 아래 표에 따라 재설정된다.

| 전환 | 다음 processingStatus | 이유 |
| --- | --- | --- |
| `초안 -> 대기` | `대기` | assignment 대기 상태 |
| `대기 -> 구현 검토` | `대기` | Architect assignment 대기 |
| `대기 -> 구현` | `대기` | Developer assignment 대기 |
| `구현 검토 -> 구현` | `대기` | 새 phase 시작 |
| `구현 -> 테스트 검증` | `대기` | Test Validator 대기 |
| `테스트 검증 -> 검토` | `검토` | Spec Validator 입력 준비 |
| `테스트 검증 -> 구현` (재작업) | `대기` | 재구현 대기 |
| `검토 -> 구현` (재작업) | `대기` | 재구현 대기 |
| `검토 -> 활성` | `완료` | 검증 완료 후 운영 진입 |
| `검토 -> 실패` | `실패` | terminal failure |
| `활성 -> 완료` | `완료` | 최종 완료 |
| `활성 -> 검토` (rollback) | `검토` | Spec Validator 재검토 시작 |
| `* -> 실패` (cancel_requested) | `실패` | 사용자 취소 |
| `실패 -> 보관` | `완료` | 아카이브 완료 (runner cycle 대상에서 제거) |

forward 전환: `초안->대기`, `대기->구현 검토`, `대기->구현`, `구현 검토->구현`, `구현->테스트 검증`, `테스트 검증->검토`, `검토->활성`, `활성->완료`

종단 전환: `실패->보관`

backward 전환: `테스트 검증->구현`, `검토->구현`, `활성->검토`

## 6.4 규칙 적용 우선순위

하나의 이벤트가 상태 전이표(5.1)와 처리 상태 전이표(6.1) 양쪽에 해당할 수 있다. 이때 적용 규칙은 아래와 같다.

- phase 전환이 없는 이벤트: 처리 상태 전이표(6.1)를 그대로 적용한다.
- phase 전환이 있는 이벤트: phase 전환 초기값 표(6.3)가 processingStatus를 결정하고, 처리 상태 전이표(6.1)는 무시한다.

예: `assignment_started`가 `대기 -> 구현` phase 전환을 유발하면, processingStatus는 phase 전환 초기값 표에 따라 `대기`가 된다. runner는 직후 별도 루프에서 해당 spec을 다시 평가하고, `state=구현, processingStatus=대기`인 spec에 대해 processingStatus를 `처리중`으로 전환한다.

# 7. dependency cascade 규칙

## 7.1 전파 대상

- blocker spec이 미해결 상태면 downstream spec에 `dependency_blocked`를 전파할 수 있다.
- blocker spec이 최종 `실패`가 되면 `depends on`으로 연결된 downstream spec에 `dependency_failed`를 전파한다.
- downstream spec의 처리 상태가 아래 중 하나면 cascade 대상이다.
  - 대기
  - 처리중
  - 검토
  - 사용자검토

## 7.2 전파 결과

- 비즈니스 상태는 유지한다.
- 처리 상태를 `보류`로 변경한다.
- 처리중인 assignment가 있으면 `cancelled`로 종료하고 관련 worktree lock을 해제한다.
- 활동 로그에 `dependency_blocked` 또는 `dependency_failed` event를 남긴다.
- Planner와 사용자에게 review request를 생성한다.

## 7.3 전파 제외

- downstream spec이 이미 `완료`이면 전파하지 않는다.
- downstream spec이 이미 `실패`이면 중복 전파하지 않는다.
- downstream spec이 이미 `보관`이면 전파하지 않는다.

# 8. timeout recovery 규칙

## 8.1 stale 판단 기준

- assignment가 `running` 상태다.
- 현재 시각이 `lastHeartbeatAt + timeoutSeconds`를 초과했다.
- 또는 `timeoutAt`을 초과했다.

## 8.2 stale 처리 결과

- assignment를 `failed`로 마감한다.
- 관련 spec lock, agent lock, worktree lock을 해제한다.
- 활동 로그에 `assignment_timed_out` event를 기록한다.
- 필요 시 Planner 또는 사용자 review request를 생성한다.

# 9. activity log 원칙

- 모든 상태 전이와 처리 상태 전이는 반드시 activity log를 남긴다.
- review request 생성, assignment 생성, assignment 취소, timeout recovery 같은 부수 효과도 별도 event로 남긴다.
- optional인 것은 review request 생성 여부이지, activity log 기록 여부가 아니다.

# 10. 테스트 우선순위

초기 구현에서 반드시 먼저 만들어야 하는 테스트는 아래다.

1. 정상 경로: `초안 -> 대기 -> 구현 -> 테스트 검증 -> 검토 -> 활성 -> 완료`
2. AC 프리패스 반려: `초안 -> 초안`
3. 테스트 부적합: `테스트 검증 -> 구현`
4. Architect 반려 후 Planner 보완 및 재검토: `구현 검토 -> draft_updated -> 구현 검토`
5. Architect 반려 3회 초과: `구현 검토 -> 실패`
6. review request 루프: `검토 -> 사용자검토 -> 검토 -> 활성 -> 완료`
7. review request 3회 초과: `검토 -> 실패`
8. 재작업 루프 3회 초과: `검토 -> 대기 -> 처리중 -> 검토` 반복 후 `실패`
9. dependency cascade: downstream `보류` 및 in-flight assignment `cancelled`
10. 보류 복원: `보류 -> dependency_resolved -> 대기` (state≠검토), `보류 -> 검토` (state=검토)
11. assignment timeout: `처리중 -> 실패`
12. review request deadline 초과: `사용자검토 -> 실패`
13. 활성 상태 rollback: `활성 -> 검토`
14. 중간 상태 취소: `cancel_requested -> 실패` (초안~검토)
15. version conflict: baseVersion 불일치 시 이벤트 거부
16. phase 전환 시 processingStatus 초기값 검증
17. 실패 후 아카이브: `실패 -> spec_archived -> 보관`
18. 보관 상태에서 모든 전이 거부: `보관 -> *` 금지
19. 실패 외 상태에서 직접 보관 전이 거부

# 11. 구현 메모

- 상태 전이 함수는 I/O 없이 계산만 수행하는 것이 좋다.
- side effect는 별도 목록으로 반환하고 runner가 실행하는 편이 좋다.
- table-driven test 데이터는 이 문서의 표와 동일한 구조를 유지하는 편이 좋다.