# Flow Prompt

## 전체 철학

**Flow의 목적**: 중단 없는 작업 연속 실행.

* 상태 전이 시 다음 행동을 즉시 수행하며, 사용자 개입은 최소화한다.
* 백로그 작업은 완료 후 즉시 다음 백로그를 자동으로 가져와 실행한다.

---

## 실행 규칙 및 스크립트

모든 스크립트는 `<워크스페이스 경로>/.flow/scripts/`에서 실행한다. `Push-Location`을 사용하거나 절대 경로로 호출한다.

| 스크립트 | 용도 | 주요 매개변수 |
| --- | --- | --- |
| `./get-status.ps1` | 현재 상태 확인 | - |
| `./start-plan.ps1` | 플랜 시작 및 폴더 초기화 | `-Title` |
| `./human-input.ps1` | 사용자 입력 요청 (Confirm, Select, Text) | `-Type`, `-Prompt` |
| `./approve-plan.ps1` | 플랜 승인 절차 | - |
| `./complete-execution.ps1` | 실행 완료 보고 및 검증 전이 | `-Summary` |
| `./validation-runner.ps1` | 검증 명령 실행 | `-Command` |
| `./finalize-task.ps1` | 작업 종료 및 IDLE 복귀 | `-Reason` |
| `./pop-backlog.ps1` | 차례대로 백로그 가져오기 | `-Preview` |

---

## 상태별 행동 지침

### 1. IDLE

* 요청(`$ARGUMENTS`)이 있으면 `./start-plan.ps1` 실행.
* 요청이 없거나 단순히 이 문서의 지시를 따르라는 경우면 `./pop-backlog.ps1 -Preview`로 큐 확인 후, 작업이 있으면 바로 `./pop-backlog.ps1`로 시작.
* 큐가 비어있을 때만 사용자에게 질문한다.

### 2. PLANNING & REVIEWING

* 사용자의 입력에서 태그를 구한다. 태그는 영어 소문자, 단수(예: `document` (O), `documents` (X)), canonical word, 최대 두 단어를 _ 로 연결(예:api_design), 핵심 키워드 위주로 3~5개 추출
* `./.flow/scripts/db.ps1 --query "{요청사항 한줄 요약}" --tags "{앞에서 추출한 태그들}" (예:--query "기계학습" --tags "deep_learning" --topk 5)
* `docs/flow/implements/{feature_name}/need-review-plan.md` 작성 (입력, 출력, 검증, 완료 조건 포함).
* 정보 부족 시 `./human-input.ps1` 사용.
* 작성 완료 후 `./approve-plan.ps1` 실행. 승인(Y) 즉시 `EXECUTING`으로 전환하여 구현 시작.


### 3. EXECUTING

* 플랜의 단계를 순차 실행하고 결과를 보고한다.
* 실패 시 `./finalize-task.ps1`로 중단, 성공 시 `./complete-execution.ps1`로 검증 전이.

### 4. VALIDATING

* `./validation-runner.ps1`로 플랜의 검증 섹션 및 프로파일 수행.
* **성공 시**: `.flow/extensions.json`의 활성 확장을 체크한다. 제안이 없으면 **보고 없이** 즉시 `COMPLETED`로 전이한다.
* **실패 시**: 5회까지 `RETRYING`(AI 자체 수정) 수행, 초과 시 `BLOCKED`.

### 5. COMPLETED

* `result.md` 작성 (템플릿 참조).
* `./finalize-task.ps1 -Reason "작업 완료"` 실행.
* **백로그 여부 판단**: `context-phase.json` 내 `backlog.is_backlog === true`인 경우, 곧바로 `./pop-backlog.ps1`을 실행하여 다음 작업을 이어간다.

---

## 금지 사항

1. 플랜 없이 코드 수정 금지.
2. 상태 파일 확인 전 행동 금지.
3. 5회 초과 재시도 금지.
