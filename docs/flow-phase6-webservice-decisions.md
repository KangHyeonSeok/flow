# Phase 6 UI 구현: 미결 의사결정과 부족한 정보

이 문서는 Phase 6 (UI) 구현을 시작하기 전에 결정해야 할 사항과 현재 flow-core API에서 부족한 부분을 정리한다.

Phase 6의 목표는 `flow-구현-로드맵.md` §2.6에 정의되어 있다. 핵심은 flow-core API 위에 사용자가 spec을 조회·편집·관리할 수 있는 UI를 제공하는 것이다.

---

# 1. 구현 전략: 2단계 접근

## 결정 사항

**Phase 6a → 6b 순차 구현으로 결정.**

### Phase 6a: TUI (Terminal UI)

- **기술**: C# + Spectre.Console, flow-core 직접 참조
- **목적**: flow-core API 갭을 실제로 부딪히며 메우고, CLI JSON 인터페이스를 완성
- **장점**:
  - `dotnet run`으로 즉시 실행, stdout으로 AI 디버깅 가능
  - 별도 서버/빌드 파이프라인 불필요
  - 독립적으로 실용적 (TUI만으로도 spec 관리 가능)

### Phase 6b: Node.js 웹서비스

- **기술**: Node.js (Express/Fastify) + 프론트엔드 (React 등)
- **목적**: 브라우저 기반 통합 spec workspace
- **연동 방식**: `flow.exe` CLI를 child process로 호출 → JSON 파싱
- **장점**:
  - TUI에서 검증된 CLI JSON 인터페이스를 그대로 활용
  - 프론트엔드와 단일 언어 (JavaScript/TypeScript)
  - flow-core를 직접 참조하지 않으므로 서버 기술 선택이 자유로움

### 이 순서의 근거

1. TUI가 flow-core의 **테스트베드** 역할 — API 갭을 실사용하며 발견·해결
2. Phase 6b 시작 시점에 **CLI JSON 인터페이스가 이미 안정화**되어 있음
3. 각 단계가 독립적으로 쓸모 있음
4. VS Code Extension은 AI 디버깅이 어렵고, ASP.NET Core + React는 인프라 복잡도가 높아 제외

---

# 2. Phase 6a (TUI) 설계 결정

## 2.1 라이브러리 선택

**결정 필요**: TUI 라이브러리

| 선택지 | 장점 | 단점 |
|--------|------|------|
| Spectre.Console | 테이블, 트리, 선택 메뉴, 마크업 | interactive 기능 제한적 |
| Terminal.Gui | 전체 화면 TUI, 윈도우/다이얼로그 | 학습 곡선, AI 학습 데이터 적음 |

**내 의견**: Spectre.Console로 시작한다. 명령형 CLI (서브커맨드 기반)이면 Spectre.Console의 테이블/트리/프롬프트로 충분하다. 전체 화면 대시보드가 필요해지면 Terminal.Gui를 검토한다.

## 2.2 TUI 기능 범위

**결정 필요**: TUI MVP에서 어디까지 구현할 것인가?

**권장 MVP 범위**:

1. Spec 목록 조회 (테이블, 상태별 필터/정렬)
2. Spec 상세 보기 (모든 section 읽기 전용)
3. Review request 응답 제출
4. 사용자 이벤트 제출 (spec_completed, cancel_requested)
5. Activity timeline 조회 (최근 20개)

**MVP 이후**:

6. Section 편집 (inline 또는 `$EDITOR` 위임)
7. Dependency 관계 트리 보기
8. Evidence 상세 보기

## 2.3 CLI JSON 인터페이스 설계

TUI는 flow-core를 직접 참조하지만, Phase 6b를 위해 **CLI에서 JSON 출력을 지원하는 서브커맨드**도 함께 만든다.

```
flow spec list --json                    # 전체 spec 목록 JSON
flow spec show <id> --json               # spec 상세 JSON
flow spec event <id> <event-type>        # 사용자 이벤트 제출
flow review respond <rr-id> <decision>   # review 응답 제출
flow activity list <spec-id> --json      # activity 목록 JSON
```

이 JSON 인터페이스가 Phase 6b에서 Node.js가 호출하는 계약이 된다.

---

# 3. Phase 6b (Node.js 웹) 설계 결정

Phase 6a 완료 후 구체화한다. 현재 시점에서 열어두는 결정 사항만 정리한다.

## 3.1 서버 프레임워크

| 선택지 | 장점 | 단점 |
|--------|------|------|
| Express | 생태계 최대, 레퍼런스 풍부 | 오래됨, 미들웨어 관리 |
| Fastify | 빠름, 스키마 검증 내장 | Express보다 작은 생태계 |
| Hono | 경량, 엣지 호환 | 비교적 신생 |

## 3.2 프론트엔드 프레임워크

| 선택지 | 장점 | 단점 |
|--------|------|------|
| React + Vite | 생태계, 컴포넌트 라이브러리 풍부 | 빌드 파이프라인 |
| Svelte | 경량, 빠른 프로토타이핑 | 작은 생태계 |
| HTMX + 서버 템플릿 | JS 최소, 단순 | 복잡한 인터랙션 제한 |

## 3.3 API 프로토콜

- REST JSON API로 시작하는 것이 현실적
- CLI JSON 출력을 거의 그대로 API 응답으로 전달 가능

## 3.4 실시간 업데이트

- MVP는 polling (15~30초)으로 시작
- runner cycle이 30초이므로 polling만으로도 실용적
- 이후 SSE로 업그레이드 가능

---

# 4. flow-core API 갭 분석

현재 flow-core가 제공하는 API와 UI가 필요로 하는 API 사이의 차이를 분석한다. Phase 6a (TUI) 구현 시 이 갭을 메운다.

## 4.1 이미 있는 것 (그대로 사용 가능)

| 기능 | flow-core API | 비고 |
|------|--------------|------|
| Spec CRUD | ISpecStore.LoadAsync, LoadAllAsync, SaveAsync, DeleteSpecAsync, ArchiveAsync | CAS 포함 |
| Assignment 조회 | IAssignmentStore.LoadAsync, LoadBySpecAsync | |
| ReviewRequest 조회/저장 | IReviewRequestStore.LoadAsync, LoadBySpecAsync, SaveAsync | |
| Activity 조회 | IActivityStore.LoadRecentAsync | maxCount 지정 가능 |
| Evidence 조회 | IEvidenceStore.LoadBySpecAsync, LoadManifestAsync | CreatedAt 최신순 |
| Review 응답 제출 | ReviewResponseSubmitter.SubmitResponseAsync | 검증 + event proposal |
| 상태 전이 규칙 | RuleEvaluator.Evaluate | pure function |
| Dependency 평가 | DependencyEvaluator.Evaluate, DetectCycles | |
| Dispatch 판단 | DispatchTable.Decide | |

## 4.2 없거나 부족한 것

### High: 사용자 이벤트 제출 API

**현재 상태**: FlowRunner.SubmitReviewResponseAsync가 유일한 외부 이벤트 제출 경로다. 그러나 UI에서 필요한 사용자 이벤트는 이것 외에도 있다:

- `spec_completed` (Active → Completed)
- `cancel_requested` (여러 상태에서 → Failed)
- `rollback_requested`
- `draft_updated` (spec 편집 후 재검증 요청)
- `spec_archived` (Completed → Archived)

**결정**: FlowRunner에 범용 `SubmitEventAsync`를 추가한다. CAS, activity logging, dependency cascade를 한 곳에서 처리해야 하므로 UI 계층이 직접 조합하면 로직이 분산된다.

### Medium: Spec 편집 범위와 권한 규칙

**현재 상태**: spec의 어떤 필드가 어떤 상태에서 편집 가능한지 규칙이 없다.

**필요한 것**:

| 필드 | 편집 가능 상태 | 편집 불가 상태 |
|------|--------------|--------------|
| title | Draft, Queued | Implementation 이후 |
| problem, goal | Draft, Queued | Implementation 이후 |
| acceptanceCriteria | Draft, Queued | Implementation 이후 (재검증 필요) |
| riskLevel | 전체 | |
| type | Draft | Queued 이후 |
| state, processingStatus | 직접 편집 불가 | 전체 |

**결정**: flow-core에 `SpecEditPolicy`를 둔다. TUI와 웹 모두 같은 규칙을 써야 하므로 공유 계층에 있어야 한다.

### Medium: Spec 목록 필터/정렬

**현재 상태**: `ISpecStore.LoadAllAsync()`는 모든 spec을 메모리에 로드한다.

**결정**: MVP에서는 LoadAllAsync → 인메모리 필터로 충분하다. 프로젝트당 spec 수가 수백 수준이면 실용적이다.

### Low: Dependency Graph 조회

**현재 상태**: `DependencyEvaluator`는 cascade 계산용이다. upstream/downstream 시각화 데이터를 반환하는 API가 없다.

**결정**: TUI에서는 트리 형태로 표시하면 충분하다. 별도 서비스는 Phase 6b에서 필요 시 추가한다.

### Low: Activity 조회 개선

- 프로젝트 전체의 최근 activity
- 커서 기반 페이징

**결정**: MVP에서는 spec별 LoadRecentAsync로 충분하다.

---

# 5. flow-core 사전 작업 목록

Phase 6a 구현과 함께 flow-core에 추가해야 할 변경 사항.

## 5.1 필수 (High)

1. **SubmitEventAsync**: FlowRunner에 사용자 이벤트 제출 범용 메서드 추가
   - 입력: specId, FlowEvent, ActorKind (기본 User)
   - CAS, dependency cascade, activity logging 포함
   - SubmitReviewResponseAsync와 유사한 패턴

2. **SpecEditPolicy**: spec 편집 가능 범위 규칙
   - 상태별 편집 가능 필드 정의
   - `CanEdit(Spec spec, string sectionName) → bool`

## 5.2 권장 (Medium)

3. **CLI JSON 출력 서브커맨드**: Phase 6b를 위한 안정적 JSON 인터페이스
   - `spec list --json`, `spec show <id> --json`, `activity list <id> --json`

## 5.3 나중 (Low)

4. **DependencyGraphBuilder**: 시각화용 그래프 데이터 생성
5. **프로젝트 전체 Activity 조회**: 대시보드용

---

# 6. 프로젝트 구조

```
tools/
  flow-core/          # 기존
  flow-core.tests/    # 기존
  flow-cli/           # 기존 (TUI 기능 추가)
  flow-cli.tests/     # 기존

# Phase 6b 시점에 추가
web/
  server/             # Node.js (Express/Fastify)
  client/             # 프론트엔드
```

TUI는 기존 flow-cli에 서브커맨드로 추가한다. 별도 프로젝트가 아니다.

---

# 7. 열린 질문

1. **Spec 생성 주체**: 새 spec을 UI에서 직접 만들 수 있는가? 현재 Planner agent가 draft를 만드는 것이 원칙이지만, 사용자가 직접 Draft를 만들고 싶을 수 있다.

2. **Runner 동시 실행**: TUI/웹과 runner가 같은 파일에 동시 접근할 때 CAS가 보호하지만, 파일 I/O 레벨에서 경합이 발생할 수 있다. AtomicWriteAsync의 .tmp → rename이 충분한가?

3. **멀티 프로젝트 지원**: Phase 6b에서 여러 projectId를 동시에 서빙해야 하는가?

---

# 8. 결론

Phase 6 구현은 2단계로 진행한다:

1. **Phase 6a (TUI)**: Spectre.Console 기반 터미널 UI. flow-core 직접 참조, API 갭 해소, CLI JSON 인터페이스 완성
2. **Phase 6b (Node.js 웹)**: CLI JSON 인터페이스 위에 Node.js 서버 + 프론트엔드. Phase 6a 완료 후 구체화

Phase 6a 착수 전에 flow-core에 **SubmitEventAsync**와 **SpecEditPolicy**를 먼저 추가해야 한다.
