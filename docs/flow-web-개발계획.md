# Flow Web 개발 계획

## 현재 상태

flow-web은 기본적인 프로젝트/스펙 CRUD와 목록/상세 뷰가 구현되어 있다.
flow-api는 전체 도메인(Spec, Assignment, Review, Event, Activity, Evidence)에 대한 REST API가 완성됨.
flow-core에는 BDD(TestDefinition), 의존성 그래프, 상태머신 등 풍부한 도메인 모델이 존재하나 UI에 반영되지 않은 부분이 많다.

### 코드 확인 기준
- 프런트엔드 위치: `tools/flow-web`
- 라우팅: 프로젝트 목록, 스펙 목록, 스펙 상세 문서 뷰 구성 완료
- API 연동: `projects`, `specs`, `assignments`, `review-requests`, `activity`, `evidence` 조회와 spec 생성/수정/삭제 지원
- 현재 상세 페이지는 이미 notebook-style에 가까운 cell 문서 뷰를 일부 구현한 상태

### 확인된 구현 현황

| 영역 | 상태 | 확인 내용 |
|---|---|---|
| 다크 테마 | 완료 | CSS 변수 기반 다크 테마 적용 완료 |
| 기본 레이아웃 | 완료 | 상단 헤더 + 좌측 사이드바 + 메인 문서 영역 구성 완료 |
| 프로젝트 목록 | 완료 | 프로젝트 목록 조회 및 이동 가능 |
| 스펙 목록 | 완료 | 상태 필터, 생성 폼, 목록 이동 가능 |
| 스펙 상세 개요 | 완료 | title, state, processing status, pipeline, problem, goal 노출 |
| Dependencies Cell | 부분 완료 | 요약 카드, 관계 표, 링크 이동, dependsOn / blocks 표시 구현. 그래프 시각화는 미구현 |
| AC Cell | 부분 완료 | 목록 + 테스트 연결 수 표시 구현, 인라인 편집 및 저장 지원 |
| BDD Cell | 부분 완료 | 테스트 상태, 타입, AC 연결 표시 구현, Given/When/Then 단계 강조 지원 |
| Implementation Status Cell | 완료 | assignment 기반 진행률 및 상태 표시 구현 |
| Review / Evidence / Activity Cell | 부분 완료 | Review 응답 UI, Activity 타임라인, Evidence 아티팩트 카드 구현. 고급 액션/상세 drill-down은 미구현 |
| 헤더 액션 | 완료 | edit / export / validate 액션 구현 |
| 인라인 편집 | 부분 완료 | Overview / AC 편집과 저장, 충돌 처리 구현 |
| Add Cell | 미구현 | 신규 셀 추가 UI 및 데이터 모델 없음 |
| User Scenarios Cell | 미구현 | 프런트/백엔드 모델 모두 미반영 |
| 의존성 그래프 시각화 | 미구현 | 텍스트/배지만 있음 |
| Export | 부분 완료 | Markdown export 구현, PDF export 미구현 |
| Validate All | 부분 완료 | 전용 validation command endpoint, 헤더 validate 액션, outcome 선택 UI 구현. batch validate는 미구현 |

### 현재 구조에서 확인된 제약
- `POST /events`는 여전히 웹 사용자 기준으로 `UserReviewSubmitted`, `SpecCompleted`, `CancelRequested`, `RollbackRequested`만 허용됨
- 이를 우회하지 않고 해결하기 위해 `POST /specs/{id}/validate` 전용 command endpoint를 추가했고, 이 경로는 `SpecValidator` actor로 상태 전이를 수행함
- `PATCH /specs/{id}`는 title, problem, goal, acceptanceCriteria, riskLevel 수정 가능하므로 편집 모드는 즉시 구현 가능
- user scenarios, persona, note/custom cell은 현재 API/도메인 모델 정의가 부족하여 프런트만 먼저 만들면 저장 구조가 어색해짐

## 목표

Jupyter Notebook 스타일의 셀 기반 스펙 문서 뷰를 구현하여, 스펙의 모든 상세(Overview, Dependencies, AC, BDD, User Scenarios, Implementation Status)를 하나의 스크롤 가능한 문서로 표현한다.

부가 목표는 아래와 같다.
- 읽기 전용 상태를 문서 작업 UI로 진화시킨다.
- 현재 존재하는 backend contract만 우선 활용해 실제 저장 가능한 편집 경험을 만든다.
- domain model이 없는 기능은 placeholder UI가 아니라 저장 구조부터 정의한 뒤 확장한다.

---

## Phase 1: 기반 구조 (Layout & Theme)

### 1-1. 다크 테마 적용
- CSS 커스텀 속성 기반 다크 테마 (Deep Navy #0A111F)
- 기존 라이트 테마 코드를 다크로 전환

### 1-2. 사이드바 레이아웃
- 좌측 240px 사이드바: 프로젝트 내 스펙 트리 네비게이터
- 계층형 폴더 구조 (스펙 그룹핑)
- 상태 인디케이터 (green=완료, yellow=진행중, gray=미시작)
- 검색바
- 메인 콘텐츠 영역과 분리된 스크롤

### 1-3. 헤더 개선
- 브레드크럼 네비게이션 (Flow > Project > Spec)
- "Run Validation", "Export" 버튼
- 마지막 업데이트 시간 표시

### Phase 1 실제 상태
- 브레드크럼: 기본 구현 완료
- 사이드바 검색: 구현 완료
- 상태 그룹핑: 구현 완료, 현재 선택된 spec 아래에 문서 섹션이 붙는 혼합 트리 구조 구현 완료
- 문서 헤더 액션 바: edit/export/validate 구현 완료
- 마지막 업데이트 시간: 페이지 헤더와 overview cell 모두 표시

---

## Phase 2: 셀 기반 문서 뷰 (Core)

### 2-1. Cell 컴포넌트 시스템
- `<Cell>` 베이스 컴포넌트: 접기/펼치기, 드래그 핸들, 좌측 색상 바
- 셀 타입별 색상: Overview(blue), Dependencies(purple), AC(green), BDD(orange), Scenarios(teal), Status(red)
- 셀 순서 드래그 앤 드롭 (react-dnd 또는 dnd-kit)

### Phase 2 실제 상태
- Cell 베이스 컴포넌트: 완료
- collapse / expand: 완료
- 좌측 accent bar: 완료
- 셀별 색상 체계: 완료
- drag handle / drag ordering: 미구현
- Overview / Dependencies / AC / BDD / Status / Review / Evidence / Activity 셀 구현 완료
- Overview / AC는 편집 가능, Dependencies / BDD는 가독성 강화 완료

### 2-2. Overview Cell
- 스펙 제목 + 상태 뱃지
- 리치 텍스트 설명 (Problem, Goal)
- 태그 칩, Priority 뱃지, Owner 표시
- 인라인 편집 모드

### 2-3. Dependencies Cell
- 의존성 노드 그래프 미니 시각화
- 테이블: 의존 스펙명, 상태, 버전, 링크
- API: Spec.Dependencies (DependsOn, Blocks) 데이터 활용

### 2-4. Acceptance Criteria Cell
- 체크리스트 UI (구현 상태 반영)
- 각 AC 항목: 체크박스 + 설명 + 연결된 테스트 수 pill
- AC 추가/편집/삭제 인라인

### 2-5. BDD Scenarios Cell
- 코드블록 스타일 서브셀 (모노스페이스)
- Given/When/Then 키워드 구문 강조
- 실행 상태 아이콘 (pass/fail/pending)
- API: Spec.Tests (TestDefinition) 데이터 활용

### 2-6. User Scenarios Cell
- 카드형 사용자 시나리오
- 페르소나 아바타 + 이름
- 수평 스텝 플로우 다이어그램

### 2-7. Implementation Status Cell
- 전체 진행률 프로그레스 바
- 기능별 상태/담당자/PR 링크 테이블
- Assignment 데이터 기반 자동 계산

---

## Phase 3: 인터랙션 & 편집

### 3-1. 인라인 편집 모드
- Edit 토글로 문서 전체를 편집 모드로 전환
- 각 셀 내용 직접 수정 → PATCH /specs/{id} 호출
- Optimistic update + version conflict 처리

### 3-2. Add Cell 기능
- 플로팅 하단 바: "+ Add Cell" 버튼
- 드롭다운: AC, BDD, Scenario, Note, Custom
- 새 셀 추가 시 해당 데이터 생성 API 호출

### 3-3. Validate All
- "Run Validation" 버튼 → POST /specs/{id}/validate
- draft 상태에서는 pass/reject, review/inReview 상태에서는 pass/rework/userReview/fail outcome 선택 지원
- 실행 결과는 spec/activity/review/assignment/evidence 쿼리 갱신으로 즉시 반영

### Phase 3 실제 상태
- Edit 토글: 구현 완료
- Optimistic update: 미구현
- version conflict 처리 UI: 구현 완료
- Add Cell: 미구현
- Validate All: 단건 validate 액션과 outcome 선택 UI 구현 완료, 전체 문서 batch validate는 미구현

---

## Phase 4: 고급 기능

### 4-1. 의존성 그래프 시각화
- D3.js 또는 reactflow 기반 인터랙티브 그래프
- 노드 클릭 시 해당 스펙으로 이동

### 4-2. 실시간 업데이트
- 현재 polling(3-5초)을 유지하되, 셀 단위 갱신으로 최적화
- 향후 WebSocket/SSE 전환 고려

### 4-3. Export
- Markdown 내보내기
- PDF 내보내기 (선택적)

### 4-4. 검색 & 필터
- 사이드바 전체 텍스트 검색
- AC/BDD 내용 검색
- 상태, 태그, 담당자 필터 조합

### Phase 4 실제 상태
- 사이드바 제목/ID 검색만 구현
- AC/BDD 본문 검색 미구현
- 조합 필터 미구현
- Markdown export 구현
- graph / realtime optimization / PDF export 미구현

---

## 우선 개발 전략

현재 코드와 API를 기준으로 보면 다음 순서가 가장 효율적이다.

### Sprint 1: 문서 작업 UX 완성
- 상세 문서 헤더 추가
- Edit 모드 토글 추가
- Overview Cell 편집
- Acceptance Criteria 편집
- Markdown Export 추가
- 저장 중 / 저장 실패 / version conflict 메시지 처리

### Sprint 2: 셀 품질 고도화
- Dependencies Cell을 테이블 + 상태 링크 구조로 개선
- BDD Cell에 Given/When/Then 시각 강조 추가
- Activity / Evidence 셀의 가독성 개선
- 사이드바를 상태 그룹에서 트리/섹션 혼합 구조로 개선

### Sprint 2 진행 상태
- Dependencies Cell 테이블/링크 구조: 구현 완료
- BDD Given/When/Then 시각 강조: 구현 완료
- Activity / Evidence 셀 가독성 개선: 구현 완료
- Review Request 응답 UI: 구현 완료
- 사이드바 구조 개선: 현재 스펙 문서 섹션 점프 + 현재 섹션 하이라이트 + 데이터 기반 섹션 노출 + 접힌 셀 자동 펼침 + 현재 spec 아래 혼합 트리 구조 구현 완료

### Sprint 3: 저장 구조 확장
- User Scenarios 데이터 모델 정의
- Note / Custom Cell 저장 계약 정의
- Add Cell 기능 구현
- 셀 순서 저장 구조 설계

### Sprint 4: 고급 기능
- dependency graph 시각화
- section 단위 polling 최적화
- validate outcome 선택 UI 및 batch validate 확장
- PDF export 검토

---

## 이번 착수 범위

이번 작업에서는 아래를 바로 개발한다.

1. Spec detail 상단에 문서 헤더 액션 바 추가
2. Export Markdown 기능 추가
3. Edit Mode 추가
4. Overview / Acceptance Criteria 저장 UI 추가
5. 저장 상태와 충돌 처리 추가

이번 작업에서 남겨둔 범위는 아래다.

1. User Scenarios Cell
2. Add Cell / drag and drop
3. dependency graph 시각화
4. validate outcome 선택 UI / batch validate

---

## 구현 우선순위

| # | 항목 | 난이도 | 영향도 |
|---|------|--------|--------|
| 1 | 다크 테마 | 낮음 | 높음 |
| 2 | Cell 컴포넌트 시스템 | 중간 | 높음 |
| 3 | Overview Cell | 낮음 | 높음 |
| 4 | AC Cell | 중간 | 높음 |
| 5 | BDD Cell | 중간 | 높음 |
| 6 | 사이드바 레이아웃 | 중간 | 중간 |
| 7 | Dependencies Cell | 중간 | 중간 |
| 8 | Implementation Status Cell | 낮음 | 중간 |
| 9 | 인라인 편집 | 높음 | 높음 |
| 10 | User Scenarios Cell | 중간 | 낮음 |
| 11 | Add Cell 기능 | 중간 | 중간 |
| 12 | 의존성 그래프 시각화 | 높음 | 중간 |
| 13 | Export | 중간 | 낮음 |

---

## 메모

- `Run Validation`은 전용 backend command endpoint로 연결했다. generic event 경로 제약은 유지하되, validation만 별도 actor 경로로 처리한다.
- 다음 확장 포인트는 batch validate, graph visualization, custom cell 저장 모델이다.
