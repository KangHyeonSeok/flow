# Phase 4: 검토 요청 루프 — 요구사항 및 결정 사항

이 문서는 Phase 4 (검토 요청 루프 테스트) 구현 전에 확인해야 할 요구사항, 이미 구현된 부분, 그리고 결정이 필요한 항목을 정리한다.

---

# 1. 현재 구현 현황

## 1.1 이미 구현된 것 (Phase 1–3)

### 모델

| 항목 | 파일 | 상태 |
|------|------|------|
| ReviewRequest | `Models/ReviewRequest.cs` | Options, Response, Status 포함 |
| ReviewRequestStatus | `Models/Enums.cs` | Open → Answered → Closed → Superseded |
| ReviewResponseType | `Models/ReviewRequest.cs` | ApproveOption, RejectWithComment, PartialEditApprove |
| ReviewResponse | `Models/ReviewRequest.cs` | RespondedBy, RespondedAt, Type, SelectedOptionId, Comment, EditedPayload |
| UserReviewLoopCount | `Models/SpecSnapshot.cs` | RetryCounters에 포함 |

### RuleEvaluator 규칙

| 이벤트 | 소스 상태 | 결과 | 비고 |
|--------|----------|------|------|
| `SpecValidationUserReviewRequested` | Review/InReview | → Review/UserReview | UserReviewLoopCount++ (3회 초과 시 Failed) |
| `UserReviewSubmitted` | Review/UserReview | → Review/InReview | 재평가 대기 |
| `ReviewRequestTimedOut` | Review/UserReview | → Failed/Error | deadline 초과 |
| `SpecValidationFailed` | Review/* | → Failed/Error | terminal failure |
| `SpecValidationReworkRequested` | Review/InReview | → Implementation/Pending | ReworkLoopCount++ (3회 초과 시 Failed) |

### Runner (FlowRunner.cs)

| 기능 | 상태 |
|------|------|
| ReviewRequest deadline timeout 감지 | 구현됨 (ProcessTimeouts) |
| Review/InReview → SpecValidator 호출 | 구현됨 (DispatchTable) |
| Review/UserReview + OpenRR → Wait | 구현됨 (DispatchTable) |
| UserReviewSubmitted → Review/InReview 전이 | RuleEvaluator에 규칙 있음 |
| DummySpecValidator | AcPrecheck(Passed) + SpecValidation(Passed) |

### Storage

| 기능 | 상태 |
|------|------|
| IReviewRequestStore.SaveAsync / LoadAsync | 구현됨 |
| IReviewRequestStore.LoadBySpecAsync | 구현됨 |
| ReviewRequest 파일 저장 (JSON) | FileFlowStore에 구현됨 |

## 1.2 아직 구현되지 않은 것

| 항목 | 설명 |
|------|------|
| UserReviewSubmitted 호출 경로 | runner/webservice가 사용자 응답을 받아서 이 이벤트를 발행하는 메커니즘이 없음 |
| ReviewRequest에 응답 기록 | ReviewResponse를 ReviewRequest에 기록하는 로직이 없음 |
| 응답 후 SpecValidator 재평가 | UserReviewSubmitted 후 Review/InReview로 돌아갈 때 SpecValidator가 응답 내용을 활용하는 로직이 없음 |
| ReviewRequest Supersede | 새 ReviewRequest가 생성될 때 기존 것을 Superseded로 전환하는 로직이 없음 |
| PartialEditApprove 후속 처리 | 사용자가 부분 수정 후 승인했을 때 수정 내용을 spec에 반영하는 로직이 없음 |
| 복수 라운드 시나리오 테스트 | UserReview → InReview → UserReview → … 반복 시나리오의 integration test가 없음 |
| DummySpecValidator의 UserReviewRequested 분기 | SpecValidator가 UserReviewRequested를 반환하는 조건이 제한적 |
| Failed spec 아카이브 | 실패 후 Planner 재등록이 완료된 spec을 아카이브하는 메커니즘이 없음 |

---

# 2. Phase 4 구현 범위

## 2.1 핵심 구현 목표

1. **사용자 응답 제출 메커니즘** — webservice 없이 ReviewRequest에 응답을 기록하고 UserReviewSubmitted 이벤트를 발행하는 경로
2. **SpecValidator 재평가 루프** — 사용자 응답 후 SpecValidator가 응답 내용을 참고하여 재평가
3. **ReviewRequest 상태 관리** — Supersede, Answered 등 상태 전환의 전체 lifecycle
4. **복수 라운드 시나리오 고정** — 1~3회 UserReview 루프가 올바르게 동작하는 golden test
5. **Failed spec Planner 재등록 + 아카이브** — 실패한 spec을 Planner에게 전달하여 새 spec 생성 후 원본을 아카이브

## 2.2 범위 밖 (Phase 5 이후)

- 실제 LLM 기반 SpecValidator prompt
- Webservice UI (검토 요청 화면, 응답 폼)
- Slack 알림 연동
- 실시간 review request push notification
- 아카이브 retention policy (자동 정리)

---

# 3. 결정 사항

## 3.1 사용자 응답 제출 메커니즘

**문제**: webservice 없이 사용자가 ReviewRequest에 어떻게 응답하는가?

**결정**: **ReviewResponseSubmitter 도메인 서비스**
- CLI나 webservice가 공통으로 호출하는 도메인 서비스
- Response 기록 + RR 상태 변경 + UserReviewSubmitted 이벤트 발행을 원자적으로 처리
- Phase 4에서는 테스트에서 직접 호출, Phase 6(webservice)에서 API로 노출

## 3.2 ReviewRequest Supersede 정책

**문제**: SpecValidator가 UserReviewRequested를 다시 발행하면 기존 Open ReviewRequest를 어떻게 처리하는가?

**결정**: **자동 Supersede**
- SpecValidationUserReviewRequested 이벤트 평가 시 기존 Open RR을 모두 Superseded로 전환
- 별도 `SupersedeReviewRequest` side effect를 추가하여 Superseded 상태로 전환
- 사용자 입장에서 열린 RR은 항상 최대 1개

## 3.3 응답 후 SpecValidator 재평가 시 입력 데이터

**문제**: UserReviewSubmitted 이후 SpecValidator에게 어떤 정보를 제공하는가?

**결정**: **전체 ReviewRequest 이력 제공**
- `OpenReviewRequests`를 `ReviewRequests`로 이름 변경
- 모든 상태의 RR을 포함 (Open + Answered + Closed + Superseded)
- SpecValidator가 이전 질문과 응답의 맥락을 파악하여 재질문을 피할 수 있도록 함
- 필터링은 agent 측에서 수행

## 3.4 PartialEditApprove 처리 범위

**문제**: 사용자가 PartialEditApprove로 응답하면 EditedPayload를 spec에 어떻게 반영하는가?

**결정**: **Phase 4에서는 미구현**
- 응답 기록까지만 보장
- EditedPayload 반영은 Phase 5에서 실제 agent와 함께 구현
- Phase 4에서는 PartialEditApprove를 ApproveOption과 동일하게 처리 (응답만 기록, 재평가 트리거)

## 3.5 3회 초과 비수렴 실패의 후속 처리

**문제**: UserReviewLoopCount > 3이면 Failed/Error로 전환된다. 이후 사용자가 할 수 있는 것은?

**결정**: **Planner 재등록 + 원본 아카이브**
- Failed spec은 상태를 변경하지 않고 이력으로 보존
- 사용자가 Failed spec의 RR에 응답하면, Runner가 Planner를 호출하여 실패 원인 + 사용자 피드백을 전달
- Planner는 실패 이유에 따라 조건 변경, 범위 축소, 분할 등을 결정하여 새 spec을 DraftCreated로 등록
- 새 spec에는 원본 Failed spec ID를 `DerivedFrom` 필드에 기록 (추적용)
- Planner 재등록이 완료되면 원본 Failed spec을 **Archived** 상태로 전이하여 runner cycle에서 제거
- 실패 이유별 분기 (비수렴, retry 초과, timeout 등)는 Planner prompt 또는 더미 로직에서 결정
- Phase 4에서는 Planner 호출 경로 + 아카이브까지 구현, 실제 prompt 기반 분기는 Phase 5

## 3.6 ReviewRequest 생성 시 Options/Questions 생성 주체

**문제**: 현재 SideEffect.CreateReviewRequest는 reason만 받는다. Options/Questions는 누가 채우는가?

**결정**: **Agent가 생성**
- AgentOutput에 `ProposedReviewRequest` 필드 추가 (Options, Questions, Summary)
- Runner가 SpecValidationUserReviewRequested 이벤트 처리 시 agent output의 RR 상세를 사용
- 더미 agent 단계에서는 하드코딩된 Options/Questions를 사용

## 3.7 Failed Spec 아카이브

**문제**: 실패해서 Planner 재등록이 완료된 spec이 계속 `specs/` 디렉토리에 남아 매 cycle마다 로드되는 것은 비효율적이다.

**결정**: **FlowState.Archived + 파일 이동**

### 상태 모델

- `FlowState` enum에 `Archived` 추가
- Failed spec에서 Planner 재등록이 완료된 후 원본 spec을 `Archived` 상태로 전이
- `ShouldExclude`에서 `Archived`를 제외 대상에 추가 (이미 Failed와 같은 위치)

### 저장소 분리

- FileFlowStore에서 Archived spec은 `specs/` → `specs-archived/`로 이동
- `LoadAllAsync()`는 `specs/`만 스캔 — Archived spec은 매 cycle 로드 대상에서 제외
- `LoadArchivedAsync(specId)` 메서드 추가 — 개별 조회 가능 (Planner 입력, 디버깅 용도)
- 관련 파일도 함께 이동: assignments, review-requests, activity-log

### 아카이브 트리거

- Planner 재등록 완료 시 Runner가 자동으로 원본 Failed spec을 아카이브
- 수동 아카이브: 사용자가 Failed spec의 RR에 "폐기" 옵션으로 응답한 경우에도 아카이브 처리
- Completed spec의 아카이브는 Phase 4 범위 밖 (나중에 retention policy로 확장 가능)

### 아카이브 시 보존하는 것

| 항목 | 보존 여부 | 위치 |
|------|----------|------|
| spec JSON | 보존 | `specs-archived/{specId}.json` |
| assignments | 보존 | `assignments-archived/{specId}/` |
| review requests | 보존 | `review-requests-archived/{specId}/` |
| activity log | 보존 | `activity-archived/{specId}/` |

### 아카이브 시 하지 않는 것

- 파일 삭제 (discard)는 하지 않음 — 운영 중 수동으로 또는 retention policy로 나중에 처리
- 아카이브된 spec의 downstream dependency cascade는 발생하지 않음 (이미 Failed 시점에 cascade 완료)

---

# 4. Phase 4 구현 계획 (초안)

## 4.1 구현 순서

### Step 1: FlowState.Archived + Storage 확장

모델 변경:
- `FlowState` enum에 `Archived` 추가
- `Spec` 모델에 `DerivedFrom: string?` 필드 추가

Storage 확장:
- `IFlowStore`에 `ArchiveAsync(specId)` 메서드 추가 — spec + 관련 파일을 archived 디렉토리로 이동
- `IFlowStore`에 `LoadArchivedAsync(specId)` 메서드 추가 — 아카이브된 spec 개별 조회
- `FileFlowStore`에 `specs-archived/`, `assignments-archived/`, `review-requests-archived/`, `activity-archived/` 디렉토리 관리 구현
- `DispatchTable.ShouldExclude`에 `FlowState.Archived` 추가

### Step 2: ReviewResponseSubmitter 도메인 서비스

```
tools/flow-core/Runner/ReviewResponseSubmitter.cs
```

- `SubmitResponseAsync(specId, reviewRequestId, ReviewResponse)` → ReviewRequest에 Response 기록 + Status=Answered → UserReviewSubmitted 이벤트 발행 → spec 상태 전이
- Failed spec의 RR 응답 감지 → Planner 재등록 트리거 반환
- IFlowStore를 통한 CAS 보호
- 이 서비스 하나로 CLI/webservice/테스트 모두 동일한 경로 사용

### Step 3: ReviewRequest Supersede 로직

RuleEvaluator 수정:
- `EvalSpecValidationUserReviewRequested`에 기존 Open RR을 Superseded로 전환하는 side effect 추가
- SideEffect에 `SupersedeReviewRequest` kind 추가
- SideEffectExecutor에 Supersede 실행 로직 추가

### Step 4: AgentInput/Output 확장

- AgentInput: `OpenReviewRequests` → `ReviewRequests` (전체 이력)
- AgentOutput: `ProposedReviewRequest` 필드 추가 (Options, Questions, Summary)

### Step 5: DummySpecValidator 확장

기존:
- AcPrecheck → AcPrecheckPassed
- SpecValidation → SpecValidationPassed

추가 분기:
- fixture-review-needed: SpecValidationUserReviewRequested 반환
- fixture-review-reject: SpecValidationReworkRequested 반환
- fixture-review-fail: SpecValidationFailed 반환
- 응답 이력 확인: Answered RR이 있으면 SpecValidationPassed (수렴)
- 응답 이력 3회 초과: UserReviewLoopCount > MaxRetry 시 RuleEvaluator가 자동 Failed

### Step 6: Failed Spec Planner 재등록 + 아카이브 경로

Failed spec의 RR에 사용자가 응답하면:
1. `ReviewResponseSubmitter`가 응답 기록 + Failed spec의 RR 응답을 감지
2. Runner가 Planner에게 실패한 spec 정보 + 사용자 피드백을 AgentInput으로 전달
3. Planner가 새 spec을 DraftCreated로 등록 (DerivedFrom에 원본 ID 기록)
4. Runner가 원본 Failed spec을 `ArchiveAsync()`로 아카이브 — Archived 상태 전이 + 파일 이동

DummyPlanner 확장:
- Failed spec 재등록 분기 추가
- 실패 이유별 더미 분기: 비수렴 → 조건 완화, retry 초과 → 범위 축소, timeout → 재시도

### Step 7: Integration Test — 검토 요청 루프

```
tools/flow-core.tests/ReviewLoopTests.cs
```

테스트 시나리오:

| # | 시나리오 | 기대 결과 |
|---|---------|----------|
| 1 | Happy path → SpecValidationPassed | Review/InReview → Active/Done |
| 2 | UserReviewRequested → 사용자 ApproveOption → SpecValidationPassed | UserReview → InReview → Active |
| 3 | UserReviewRequested → 사용자 RejectWithComment → SpecValidationReworkRequested | UserReview → InReview → Implementation |
| 4 | UserReviewRequested 2회 → 사용자 응답 2회 → 수렴 | 3라운드 이내 Active |
| 5 | UserReviewRequested 3회 초과 → Failed | UserReviewLoopCount > 3 → Failed |
| 6 | ReviewRequest timeout → Failed | deadline 초과 → Failed |
| 7 | ReworkRequested 3회 초과 → Failed | ReworkLoopCount > 3 → Failed |
| 8 | Supersede: 새 RR 생성 시 기존 Open RR이 Superseded | 동시에 Open RR 1개만 존재 |
| 9 | PartialEditApprove 응답 → SpecValidator 재평가 | 응답 기록 확인, 재평가 트리거 확인 |
| 10 | Failed spec → Planner 재등록 → 아카이브 | Failed RR 응답 → Planner → 새 spec → 원본 Archived |
| 11 | 아카이브된 spec이 LoadAllAsync에서 제외됨 | Archived spec은 runner cycle에 포함되지 않음 |
| 12 | LoadArchivedAsync로 아카이브 spec 개별 조회 | 아카이브 파일 보존 확인 |

### Step 8: Golden Scenario 추가

```
tools/flow-core.tests/GoldenScenarioTests.cs
```

- `GoldenScenario_ReviewLoop_SingleRound` — Draft → ... → Review/InReview → UserReview → 응답 → InReview → Active
- `GoldenScenario_ReviewLoop_MultiRound` — 2회 UserReview 루프 → 수렴 → Active
- `GoldenScenario_ReviewLoop_Exhausted` — 3회 초과 → Failed → Planner 재등록 → 새 spec Draft → 원본 Archived

## 4.2 산출물 요약

| 파일 | 유형 | 설명 |
|------|------|------|
| `Models/Enums.cs` | 수정 | `FlowState.Archived` 추가 |
| `Models/Spec.cs` | 수정 | `DerivedFrom` 필드 추가 |
| `Models/SideEffect.cs` | 수정 | `SupersedeReviewRequest` kind 추가 |
| `Storage/IFlowStore.cs` | 수정 | `ArchiveAsync`, `LoadArchivedAsync` 추가 |
| `Storage/FileFlowStore.cs` | 수정 | 아카이브 디렉토리 관리, 파일 이동 구현 |
| `Runner/ReviewResponseSubmitter.cs` | 신규 | 사용자 응답 제출 서비스 |
| `Runner/SideEffectExecutor.cs` | 수정 | Supersede 실행 로직 |
| `Runner/DispatchTable.cs` | 수정 | `ShouldExclude`에 Archived 추가 |
| `Rules/RuleEvaluator.cs` | 수정 | UserReviewRequested에 Supersede 추가 |
| `Agents/IAgentAdapter.cs` | 수정 | AgentInput/Output 확장 |
| `Agents/Dummy/DummySpecValidator.cs` | 수정 | 리뷰 루프 분기 추가 |
| `Agents/Dummy/DummyPlanner.cs` | 수정 | Failed spec 재등록 분기 추가 |
| `Runner/FlowRunner.cs` | 수정 | ReviewResponseSubmitter 통합, Planner 재등록, 아카이브 |
| `tests/ReviewLoopTests.cs` | 신규 | 12개 시나리오 테스트 |
| `tests/GoldenScenarioTests.cs` | 수정 | 3개 golden scenario 추가 |

---

# 5. 테스트 전략

## 5.1 단위 테스트 (RuleEvaluator)

이미 Phase 1에서 100+개 테스트로 규칙이 고정되어 있다. Phase 4에서 추가/수정하는 규칙:
- Supersede side effect가 올바르게 생성되는지
- UserReviewLoopCount 경계값 (2, 3, 4)

## 5.2 Storage 테스트

- `ArchiveAsync` — spec + assignments + review-requests + activity-log가 archived 디렉토리로 이동되는지
- `LoadAllAsync` — archived spec이 포함되지 않는지
- `LoadArchivedAsync` — archived spec을 개별 조회할 수 있는지
- 아카이브 후 원본 디렉토리에 파일이 남지 않는지

## 5.3 통합 테스트 (ReviewLoopTests)

- ReviewResponseSubmitter → RuleEvaluator → SideEffectExecutor → FileFlowStore 전체 경로
- FakeTimeProvider로 deadline 제어
- DummySpecValidator의 분기 조건으로 시나리오 제어
- Failed → Planner 재등록 → 아카이브 전체 경로

## 5.4 Golden Scenario

- 전체 루프 (Draft → Active 또는 Failed → Archived)를 activity log 순서로 검증
- 각 시나리오에서 ReviewRequest 생성/응답/Supersede/Close 이력 확인
- Exhausted 시나리오에서 원본 spec이 Archived되고 새 spec이 Draft로 생성되는지 확인

---

# 6. 위험 요소

| 위험 | 영향 | 완화 방안 |
|------|------|----------|
| ReviewResponseSubmitter의 CAS 경합 | 사용자 응답 유실 | retry 1회 + 실패 시 명확한 에러 메시지 |
| DummySpecValidator 분기 로직 복잡화 | fixture 이름 기반 분기가 지저분해짐 | fixture 이름 대신 spec 필드 (예: metadata)로 분기 검토 |
| AgentOutput 확장이 Phase 5 agent 설계와 충돌 | 재작업 | ProposedReviewRequest를 optional로 설계, Phase 5에서 확장 |
| PartialEditApprove 미구현으로 인한 테스트 gap | Phase 5에서 발견되는 버그 | Phase 4에서 "응답 기록" 자체는 테스트, 반영 로직만 defer |
| 아카이브 파일 이동 중 실패 | 부분 이동 상태 | 이동을 원자적으로 처리 (전체 성공 or 롤백). 또는 이동 전 복사 → 검증 → 원본 삭제 순서로 진행 |
| 아카이브된 spec의 downstream 참조 깨짐 | dependency 체크 시 upstream을 못 찾음 | `HasIncompleteUpstream`에서 존재하지 않는 upstream은 pass 처리 (이미 구현됨) |
