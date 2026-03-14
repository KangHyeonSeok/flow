# Flow 최소 스키마

이 문서는 1차 구현에 필요한 최소 JSON 스키마 계약을 정리한 문서다. 목적은 완전한 모델링이 아니라, state rule, runner, webservice, Slack이 공통으로 사용할 필수 필드를 먼저 고정하는 데 있다.

# 1. 원칙

- `spec.json`이 authoritative source다.
- activity는 `activity/*.jsonl`에 append-only로 저장한다.
- review request는 별도 객체로 관리한다.
- assignment는 spec에 종속되지만 독립 식별자를 가진다.
- 초기 구현은 최소 필드만 고정하고 확장 필드는 나중에 추가한다.

추가 원칙:

- `state`는 phase를 나타내고, `processingStatus`는 해당 phase의 실행 상태를 나타낸다.
- Agent는 state를 직접 바꾸지 않고 event 또는 proposed event를 제출한다.
- Spec Manager는 event와 현재 spec version을 기준으로 state transition을 적용한다.
- assignment는 단순 lock 정보가 아니라 실행 단위 task다.
- review request는 무기한 열려 있지 않도록 deadline을 가질 수 있어야 한다.

# 2. spec.json

## 2.1 최소 필드

```json
{
  "id": "spec-001",
  "projectId": "proj-001",
  "title": "사용자 로그인 버튼 수정",
  "type": "task",
  "problem": "로그인 버튼이 비활성 상태를 명확히 보여주지 않는다.",
  "goal": "비활성 상태를 시각적으로 명확히 표현한다.",
  "acceptanceCriteria": [
    {
      "id": "ac-001",
      "text": "비활성 상태 버튼은 회색 배경과 비활성 커서를 표시한다.",
      "testable": true,
      "relatedTestIds": ["test-001"]
    }
  ],
  "state": "구현",
  "processingStatus": "처리중",
  "riskLevel": "low",
  "dependencies": {
    "dependsOn": [],
    "blocks": []
  },
  "assignments": ["asg-001"],
  "reviewRequestIds": [],
  "testIds": ["test-001"],
  "tests": [
    {
      "id": "test-001",
      "type": "unit",
      "title": "비활성 버튼 스타일 검증",
      "acIds": ["ac-001"],
      "status": "not-run"
    }
  ],
  "createdAt": "2026-03-14T10:00:00Z",
  "updatedAt": "2026-03-14T10:10:00Z",
  "version": 1
}
```

## 2.2 필드 설명

- `id`: spec 식별자
- `projectId`: 프로젝트 식별자
- `title`: 짧은 제목
- `type`: `feature | task`
- `problem`: 문제 정의
- `goal`: 목표 정의
- `acceptanceCriteria`: 검증 가능한 기준 목록
- `state`: 비즈니스 phase
- `processingStatus`: 해당 phase의 실행 상태
- `riskLevel`: `low | medium | high | critical`
- `dependencies`: 의존 관계
- `assignments`: 현재 또는 과거 assignment 참조 목록
- `reviewRequestIds`: review request 참조 목록
- `testIds`: 연결된 테스트 식별자 목록. 빠른 조회용 인덱스이며, 실제 AC 연결은 test의 `acIds` 또는 AC의 `relatedTestIds`로 표현한다.
- `tests`: test 객체 배열. 각 test의 정의와 상태를 spec.json 안에 inline으로 저장한다.
- `createdAt`, `updatedAt`: 타임스탬프
- `version`: optimistic concurrency 제어용 버전

## 2.3 상태 일관성 규칙

스키마 자체가 모든 규칙을 강제하지는 않지만, 최소한 아래 의미를 유지해야 한다.

- `state`는 현재 spec이 어느 phase에 있는지를 나타낸다.
- `processingStatus`는 그 phase 안에서 실행, 검증, 사용자 검토가 어느 단계에 있는지를 나타낸다.
- `state=초안`이면 `processingStatus`는 `대기 | 처리중 | 보류` 중 하나다.
- `state=대기`이면 `processingStatus`는 `대기 | 보류` 중 하나다.
- `state=구현 검토`이면 `processingStatus`는 `대기 | 처리중 | 실패 | 보류` 중 하나다.
- `state=구현`이면 `processingStatus`는 `대기 | 처리중 | 검토 | 실패 | 보류` 중 하나다.
- `state=테스트 검증`이면 `processingStatus`는 `대기 | 처리중 | 검토 | 실패 | 보류` 중 하나다.
- `state=검토`이면 `processingStatus`는 `검토 | 사용자검토 | 완료 | 실패 | 보류` 중 하나다.
- `state=활성`이면 `processingStatus`는 `완료` 중 하나다.
- `state=실패`이면 `processingStatus`는 `실패` 중 하나다.
- `state=완료`이면 `processingStatus`는 `완료` 중 하나다.
- `processingStatus`만 바뀌고 `state`는 유지될 수 있지만, phase 자체가 바뀌는 이벤트는 항상 `state` 변경으로 반영해야 한다.

## 2.4 비워도 되는 필드

다음은 비어 있으면 저장 시 생략 가능하다.

- `dependencies.dependsOn`
- `dependencies.blocks`
- `reviewRequestIds`
- `testIds`

# 3. acceptance criterion

```json
{
  "id": "ac-001",
  "text": "비활성 상태 버튼은 회색 배경과 비활성 커서를 표시한다.",
  "testable": true,
  "notes": "UI snapshot 또는 style assertion으로 검증 가능",
  "relatedTestIds": ["test-001"]
}
```

최소 필드:

- `id`
- `text`
- `testable`

권장 필드:

- `notes`
- `relatedTestIds`

# 4. test

test는 AC와의 연결이 명시되어야 한다. top-level `testIds`만으로는 어떤 테스트가 어떤 AC를 검증하는지 알 수 없기 때문이다.

test 객체는 `spec.json`의 `tests` 배열에 inline으로 저장한다. `testIds`는 빠른 조회용 인덱스이고, 실제 test 정의는 같은 파일 안에 있어야 한다.

```json
{
  "id": "test-001",
  "type": "unit",
  "title": "비활성 버튼 스타일 검증",
  "acIds": ["ac-001"],
  "status": "not-run"
}
```

최소 필드:

- `id`
- `type`
- `acIds`

권장 필드:

- `title`
- `status`
- `lastRunAt`
- `lastResult`

허용 값:

- `type`: `unit | e2e | user`
- `status`: `not-run | passed | failed | skipped`

# 5. assignment

```json
{
  "id": "asg-001",
  "specId": "spec-001",
  "agentRole": "developer",
  "type": "implementation",
  "status": "running",
  "startedAt": "2026-03-14T10:01:00Z",
  "lastHeartbeatAt": "2026-03-14T10:03:00Z",
  "timeoutSeconds": 300,
  "finishedAt": null,
  "resultSummary": null,
  "cancelReason": null,
  "worktree": {
    "id": "wt-001",
    "path": "D:/Projects/flow/.worktrees/spec-001"
  }
}
```

최소 필드:

- `id`
- `specId`
- `agentRole`
- `type`
- `status`
- `startedAt`
- `lastHeartbeatAt`
- `timeoutSeconds`

권장 필드:

- `finishedAt`
- `resultSummary`
- `cancelReason`
- `worktree`

assignment는 실행 단위 task로 해석한다. 하나의 spec에는 서로 다른 목적의 assignment가 동시에 존재할 수 있다.

예:

- implementation task
- test validation task
- spec validation task

허용 값:

- `agentRole`: `planner | architect | developer | test-validator | spec-validator | spec-manager`
- `type`: `planning | ac-precheck | architecture-review | implementation | test-validation | spec-validation | state-transition`
- `status`: `queued | running | completed | failed | cancelled`

# 6. review request

```json
{
  "id": "rr-001",
  "specId": "spec-001",
  "createdBy": "spec-validator",
  "createdAt": "2026-03-14T10:20:00Z",
  "reason": "버튼 비활성 시각 표현이 접근성 기준을 충족하는지 사용자 확인 필요",
  "summary": "회색 배경 대비와 cursor 처리 중 어떤 안을 채택할지 결정 필요",
  "questions": [
    "안 A와 안 B 중 어떤 시각 표현을 선택할 것인가?"
  ],
  "options": [
    {
      "id": "opt-a",
      "label": "안 A",
      "description": "진한 회색 배경 + not-allowed cursor"
    },
    {
      "id": "opt-b",
      "label": "안 B",
      "description": "밝은 회색 배경 + pointer 유지"
    }
  ],
  "status": "open",
  "deadlineAt": "2026-03-15T10:20:00Z",
  "response": null,
  "resolution": null
}
```

최소 필드:

- `id`
- `specId`
- `createdBy`
- `createdAt`
- `reason`
- `questions`
- `options`
- `status`

권장 필드:

- `summary`
- `deadlineAt`
- `response`
- `resolution`

허용 값:

- `status`: `open | answered | closed | superseded`

review request와 spec의 연결 규칙은 아래처럼 해석한다.

- review request가 `open`이면 일반적으로 spec의 `state=검토`, `processingStatus=사용자검토`다.
- review request가 `answered`가 되면 runner 또는 rule evaluator가 `user_review_submitted`를 발생시키고 spec은 다시 `processingStatus=검토`로 돌아간다.
- deadline을 넘긴 open review request는 timeout 정책 대상이 된다.

# 7. review response

```json
{
  "respondedBy": "user",
  "respondedAt": "2026-03-14T10:25:00Z",
  "type": "approve-option",
  "selectedOptionId": "opt-a",
  "comment": "안 A로 진행"
}
```

허용 값:

- `type`: `approve-option | reject-with-comment | partial-edit-approve`

최소 필드:

- `respondedBy`
- `respondedAt`
- `type`

조건부 필드:

- `selectedOptionId`: `approve-option`일 때 필요
- `comment`: `reject-with-comment`, `partial-edit-approve`에서 권장
- `editedPayload`: `partial-edit-approve`일 때 필요

# 8. activity event

activity는 `activity/<date>.jsonl` 같은 파일에 한 줄씩 저장한다.

```json
{
  "eventId": "evt-001",
  "timestamp": "2026-03-14T10:03:00Z",
  "specId": "spec-001",
  "actor": "runner",
  "action": "assignment_started",
  "sourceType": "scheduler",
  "baseVersion": 1,
  "state": "구현",
  "processingStatus": "처리중",
  "message": "developer assignment started",
  "assignmentId": "asg-001",
  "correlationId": "run-001-loop-03",
  "payload": {}
}
```

최소 필드:

- `eventId`
- `timestamp`
- `specId`
- `actor`
- `action`
- `sourceType`
- `baseVersion`
- `state`
- `processingStatus`
- `message`

권장 필드:

- `assignmentId`
- `reviewRequestId`
- `correlationId`
- `payload`

activity event는 replay, audit, debug 기준 데이터다. 따라서 상태 전이, assignment 변경, review request 생성, timeout recovery 같은 중요한 변화는 모두 이 스키마로 기록하는 편이 맞다.

# 9. agent input payload

```json
{
  "spec": {},
  "activeAssignment": {},
  "recentActivity": [],
  "openReviewRequests": [],
  "context": {
    "projectId": "proj-001",
    "runId": "run-001",
    "loopCount": 3,
    "currentVersion": 1
  }
}
```

최소 필드:

- `spec`
- `context.projectId`
- `context.runId`
- `context.currentVersion`

# 10. agent output payload

```json
{
  "result": "success",
  "baseVersion": 1,
  "proposedEvent": {
    "type": "implementation_submitted",
    "summary": "버튼 스타일 수정 완료",
    "payload": {}
  },
  "artifacts": [],
  "evidence": [],
  "message": "implementation finished"
}
```

최소 필드:

- `result`
- `baseVersion`
- `proposedEvent`
- `message`

허용 값:

- `result`: `success | retryable-failure | terminal-failure | no-op`

`baseVersion`은 agent가 어떤 spec 버전을 기준으로 작업했는지 나타낸다. Spec Manager는 제출 시점의 현재 `spec.version`과 `baseVersion`이 다르면 충돌로 판단하고 자동 적용을 거부할 수 있어야 한다.

# 11. 구현 메모

- 초기 구현은 JSON Schema 정식 문서보다 C# 모델과 serialization contract를 먼저 맞추는 편이 현실적이다.
- 다만 enum 값과 필수 필드는 여기서 먼저 고정해야 한다.
- 빈 필드 제거는 저장 전에 수행하되, 검증 로직은 내부 모델 기준으로 수행하는 편이 안전하다.
- spec, assignment, review request, activity append는 저장 경계가 다르므로 완전한 트랜잭션처럼 보장되지 않는다.
- 그래서 activity event에 `baseVersion`과 correlation 정보를 남기고, Spec Manager가 optimistic concurrency로 충돌을 감지하는 편이 현실적이다.