# Flow Iteration Rules

이 저장소에서는 매 시간 반복되는 개발 루프를 아래 순서로 수행한다.

## 목적

- 문서, 구현, 테스트 상태를 매 반복마다 다시 일치시킨다.
- 현재 반복에서 해야 할 일을 문서로 남기고, 그 문서를 기준으로 바로 구현한다.
- 다음 반복이 더 나아지도록 중요한 운영 지식을 이 파일에 축적한다.

## 반복 순서

1. 문서와 구현을 함께 읽고 현재 상태를 동기화한다.
2. 어긋난 문서를 먼저 수정한다.
3. 이번 반복의 할 일을 문서에 `Now`, `Next`, `Later` 또는 동등한 우선순위 구조로 정리한다.
4. `Now` 항목 중 가장 중요한 것부터 직접 구현한다.
5. 구현 후 테스트, 수동 확인, 남은 리스크를 문서에 반영한다.
6. 반복 종료 전에 이 파일의 `Iteration Notes`에 핵심 학습을 추가한다.

## 1단계: 문서 동기화와 할 일 정리

매 반복 시작 시 아래를 먼저 확인한다.

- 관련 spec 문서, 프로젝트 문서, 설계 문서
- 최근에 수정된 구현 파일과 테스트
- 직전 반복에서 남긴 `Iteration Notes`
- 현재 미완료 작업과 알려진 실패 상태

이 단계의 필수 결과물은 아래다.

- 구현과 맞지 않는 설명을 수정한 문서 변경
- 이번 반복의 작업 목록이 들어 있는 문서 변경
- 가정, 제약, 확인이 필요한 항목의 명시

문서에 할 일을 남길 때는 반드시 아래를 포함한다.

- 왜 필요한지
- 어떤 파일 또는 기능을 건드리는지
- 완료를 어떻게 확인할지

## 2단계: 할 일 기반 구현

- 문서에 정리한 `Now` 항목만 집어서 구현한다.
- 구현 중 범위가 커지면 먼저 문서의 할 일을 다시 쪼갠다.
- 구현 후에는 관련 테스트나 검증을 즉시 수행한다.
- 결과가 문서와 다르면 코드만 남기지 말고 문서도 함께 갱신한다.

## 반복 종료 규칙

매 반복이 끝날 때 아래를 반드시 남긴다.

- 완료한 항목
- 완료하지 못한 항목과 이유
- 다음 반복의 첫 작업
- 반복 중 새로 확인한 제약, 함정, 결정 사항
- 재사용할 명령, 테스트 절차, 확인 포인트

## Iteration Notes

아래 형식을 유지하면서 최신 항목을 위에 추가한다.

```md
## 2026-03-22 HH:00

- Context: 이번 반복에서 다룬 범위
- Done: 실제로 끝낸 작업
- Next: 다음 반복이 바로 시작할 작업 1~3개
- Risks: 남은 문제, 불확실성, 막힌 지점
- Learnings: 다음 반복이 반드시 기억해야 할 점
- Verification: 실행한 테스트, 확인한 화면, 확인하지 못한 항목
```

중복되는 긴 서술은 피하고, 다음 반복이 바로 활용할 수 있는 사실만 남긴다.

## 2026-03-22 20:20

- Context: 가능한 spec을 직접 실행해 결과를 확인할 수 있는 `살아있는 spec` 개념을 문서화했다.
- Done: live execution/playground를 선택적 spec capability로 정의하는 새 문서를 추가했고, README, 최소 스키마, web integrated workspace 문서에 interactive run 섹션과 `liveExecution` 방향을 반영했다.
- Next: `liveExecution` 필드의 최소 저장 계약과 evidence 저장 형식, web의 Live Run section UI를 구체 설계한다.
- Risks: 아직 실제 runtime과 sandbox model이 없어서 어떤 실행 target을 허용할지, destructive action을 어떻게 차단할지, evidence 저장 형식을 어떻게 통일할지는 미정이다.
- Learnings: 살아있는 spec은 모든 spec의 공통 요구사항이 아니라, deterministic하고 안전한 spec에 선택적으로 붙는 capability로 정의해야 전체 모델이 무너지지 않는다.
- Verification: 새 문서와 README/flow-schema/flow-webservice-integrated-workspace 서술을 다시 읽어 live execution이 테스트 대체가 아니라 보강 계층으로 일관되게 설명되는지 점검했다.

## 2026-03-22 19:10

- Context: Decision Record 문서에 context retrieval 전략과 human-in-the-loop 최소화 원칙을 추가로 내렸다.
- Done: agent가 어떤 Change/Decision Record를 읽을지 정하는 Relevant Context Retrieval 규칙과, 사용자가 빈 질문 대신 상황 분석 및 대안별 리스크/이득 보고서를 받도록 하는 Decision Record 초안 원칙을 문서화했고 README의 핵심 철학/Next에도 반영했다.
- Next: retrieval summary cache 필드, decision preparation report 스키마, create/apply/resume 이벤트를 core/agent contract 수준으로 세분화한다.
- Risks: 아직 runtime 구현이 없어서 retrieval ranking, summary cache 저장 위치, decision draft 생성 책임 분담은 문서 가정에 머물러 있다.
- Learnings: Decision Record는 문서 타입만 정의해서는 부족하다. agent가 무엇을 얼마나 읽을지와 사용자에게 올라가기 전에 얼마나 분석을 채워 둘지를 함께 정의해야 실제 human-in-the-loop 감소 효과가 생긴다.
- Verification: decision 문서의 새 섹션과 README의 보강 문장을 다시 읽어 retrieval 전략, decision draft, runner resume 흐름이 서로 충돌하지 않는지 점검했다.

## 2026-03-22 20:00

- Context: flow-web-project-epic-view-plan Phase 2 — ProjectOverviewPage 구현 및 라우트 승격
- Done:
  - ProjectOverviewPage 신규 생성 (`tools/flow-web/src/pages/ProjectOverviewPage.tsx`)
    - project header (title, summary, last activity)
    - 6개 stat card (specs, epics, active epics, open reviews, failed, on hold)
    - Epic Index 섹션 (EpicCard: progress bar, spec counts, priority badge, epic 링크)
    - Project Document 섹션 (problem, goals, non-goals, context, architecture — 접힌 상태 기본)
    - All Specs 바로가기 링크
  - 라우트 승격: `/projects/:projectId` → ProjectOverviewPage, `/projects/:projectId/specs` → SpecsPage
  - Layout breadcrumb 확장: Specs 중간 경로 표시, spec detail에서 Specs 링크 추가
  - Sidebar를 spec detail 화면에서만 표시하도록 변경 (overview/specs list에서는 숨김)
  - useProjectView, useEpics 훅 추가 (`tools/flow-web/src/hooks/useSpecs.ts`)
- Next:
  1. EpicOverviewPage 구현 (Phase 3) — `/projects/:projectId/epics/:epicId`
  2. Spec view 상위 문맥 연결 (Phase 4) — breadcrumb에 epic, sidebar에 epic context
- Risks: 실제 project.json / epic JSON 파일이 없으면 ProjectOverviewPage가 빈 데이터를 보여줌. Hotspots 섹션은 아직 미구현.
- Learnings:
  - Sidebar를 project overview에서 숨기면 overview가 더 넓게 보이고, spec-only sidebar가 overview에서 의미 없는 빈 패널이 되는 문제를 피할 수 있다.
  - Layout의 breadcrumb에서 `useLocation`을 함께 사용하면 route param만으로 구분하기 어려운 중간 경로(specs list)를 처리할 수 있다.
- Verification: tsc --noEmit 통과, flow-core 325/325, flow-api 42/42 테스트 통과.

## 2026-03-22 18:50

- Context: README에서 Decision Record를 change와 대칭적인 핵심 개념으로 더 명확히 드러내도록 보강했다.
- Done: 운영 모델 도식에 Decision Record 축을 추가했고, 별도 섹션으로 왜 필요한지 설명했으며, runner 철학과 현재 우선순위 문장에도 decision 흐름을 반영했다.
- Next: README의 서술을 기준으로 Decision Record create/apply/resume 계약과 web의 open decisions projection을 구체 문서로 내린다.
- Risks: README는 개념을 분명히 했지만 Decision Record runtime 상태, 이벤트, 저장 위치는 아직 설계 문서 수준에 머물러 있다.
- Learnings: README에서 Decision Record를 단순 용어 추가로만 두면 약하다. change와 대칭적인 입력 축, user-facing problem document, runner resume 근거라는 세 역할을 함께 적어야 개념이 선명해진다.
- Verification: README 본문을 다시 읽어 opening questions, 운영 모델, 개별 설명, 현재 우선순위가 모두 Decision Record를 일관되게 포함하는지 확인했다.

## 2026-03-22 19:00

- Context: flow-web-project-epic-view-plan Phase 1 — 프로젝트/에픽 데이터 계약 추가
- Done:
  - flow-core에 ProjectDocument, EpicDocument, ProjectView, EpicView 등 도메인 모델 추가 (ProjectDocument.cs, EpicDocument.cs)
  - flow-api에 프로젝트/에픽 읽기 API 엔드포인트 추가 (ProjectEndpoints 확장, EpicEndpoints 신규, ProjectDocumentStore/EpicDocumentStore 신규)
    - `GET /api/projects/{projectId}/view` — ProjectView (스펙 집계 + 에픽 요약 포함)
    - `GET /api/projects/{projectId}/document` — ProjectDocument JSON
    - `GET /api/projects/{projectId}/epics` — 에픽 문서 목록
    - `GET /api/projects/{projectId}/epics/{epicId}/view` — EpicView (자식 스펙 집계)
    - `GET /api/projects/{projectId}/epics/{epicId}/document` — EpicDocument JSON
    - `GET /api/projects/{projectId}/epics/{epicId}/specs` — 에픽 범위 스펙 목록
  - flow-web에 TypeScript 타입 및 API 클라이언트 함수 추가 (flow.ts, client.ts)
- Next:
  1. ProjectOverviewPage 구현 (라우트 승격, header/epic index/hotspots 렌더링)
  2. EpicOverviewPage 구현
  3. 샘플 project.json, epic JSON 파일 생성하여 실제 API 동작 확인
- Risks: 프로젝트/에픽 문서 저장소가 아직 JSON 파일 직접 읽기 방식이라 쓰기 API 없음. 실제 project.json/epic JSON이 없으면 view API가 빈 데이터를 반환함.
- Learnings:
  - 프로젝트/에픽 저장 경로: `{FLOW_HOME}/projects/{projectId}/project.json`, `{FLOW_HOME}/projects/{projectId}/epics/{epicId}.json`
  - EpicDocument.ChildSpecIds 기반으로 스펙을 에픽에 매핑 (spec에 epicId 추가는 Later)
- Verification: core 325/325, API 42/42 테스트 통과. flow-web tsc --noEmit 통과.

## 2026-03-22 18:35

- Context: 사용자 결정 문제를 spec/review와 분리해 별도 운영 객체로 다루는 문서 모델을 추가했다.
- Done: Decision Record를 독립 타입으로 정의하는 설계 문서를 새로 작성했고 README, 프로젝트 문서, 최소 스키마 문서에 이 타입의 위치와 링크를 반영했다.
- Next: Decision Record 저장 위치, create/apply/supersede 이벤트, pendingDecisionIds 같은 runtime 연결 필드를 core 계약으로 구체화한다.
- Risks: 현재는 설계 문서 수준이며 runner block/resume 규칙과 web decision inbox projection은 아직 구현 계약으로 내려오지 않았다.
- Learnings: 사용자 결정 문제는 review request의 변형으로 흡수하기보다 Change Record와 나란한 독립 입력으로 둘 때 UX와 runtime 책임이 동시에 선명해진다.
- Verification: 새 문서와 README/flow-project-document/flow-schema 서술을 대조해 Decision Record, Change Record, Spec, Review Request 경계가 서로 겹치지 않도록 점검했다.

## 2026-03-22 18:10

- Context: README를 설치/명령 안내 중심에서 project/epic/spec/change/runner를 아우르는 상위 개발 철학 문서로 다시 정리했다.
- Done: 관련 프로젝트 문서, 에픽 계획, change-driven 운영 문서, runner 계약 문서를 다시 읽고 README를 개발 철학과 현재 우선순위 중심으로 전면 재구성했다.
- Next: README 서술을 기준으로 Change Record 저장 계약과 impact/close 흐름을 core 문서와 구현에 연결한다.
- Risks: README는 방향을 정리했지만 change record와 project/epic 계층은 아직 runtime contract와 web model에 완전히 반영되지 않았다.
- Learnings: 이 저장소의 README는 도구 카탈로그보다 상위 운영 모델을 압축하는 문서일 때 다른 설계 문서와 더 잘 연결된다.
- Verification: README와 핵심 설계 문서의 용어를 대조해 project/epic/spec/change/runner 책임이 충돌하지 않도록 문서 기준으로 점검했다.

## 작업 방식 규칙

- 문서 업데이트 없이 구현만 하고 끝내지 않는다.
- TODO는 채팅에만 남기지 말고 저장소 문서에도 남긴다.
- 막힌 이유가 있으면 원인과 재시도 조건을 적는다.
- 큰 결정은 설계 문서에 반영하고, 이 파일에는 요약과 후속 액션만 남긴다.
- 다음 반복이 시작 5분 안에 맥락을 복원할 수 있어야 한다.

## 2026-03-22 17:31

- Context: 장기 운영 중 발생하는 변경을 project/epic/spec 구조와 어떻게 연결할지 정리했다.
- Done: change record를 독립 입력으로 두고 spec graph, code, tests, evidence로 전파하는 운영 문서를 추가했고 README와 프로젝트 문서에서 링크했다.
- Next: Change Record 스키마와 `change-impact`, `change-close` 같은 CLI/API 계약을 설계한다.
- Risks: 아직 flow-core 저장 계약과 상태 규칙에는 change record 개념이 들어가지 않아 문서와 런타임 사이 간극이 남아 있다.
- Learnings: `변화가 스펙에 영향을 준다`와 `변화가 스펙 그래프에 들어오는 입력이다`는 다르다. 전자는 결과, 후자는 원인과 운영 추적 단위다.
- Verification: 새 문서에 개념 설명, 예시, 운영 규칙, 다음 작업을 반영했고 상위 문서 링크를 함께 갱신했다.