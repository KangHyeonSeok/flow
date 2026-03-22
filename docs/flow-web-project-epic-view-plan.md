# Flow Web 프로젝트/에픽 뷰 계획

이 문서는 [flow-project-document.md](d:\Projects\flow\docs\flow-project-document.md), [flow-epic-plan.md](d:\Projects\flow\docs\flow-epic-plan.md), [flow-webservice-integrated-workspace.md](d:\Projects\flow\docs\flow-webservice-integrated-workspace.md)를 기준으로 flow-web에 프로젝트 뷰와 에픽 뷰를 추가하는 계획을 정리한다.

목표는 단순히 화면을 하나 더 만드는 것이 아니다. 현재 완성된 spec view를 유지한 채, 그 위에 `프로젝트 뷰 -> 에픽 뷰 -> 스펙 뷰` 계층을 세워 상위 문맥, 묶음, 운영 hotspot을 잃지 않게 하는 것이다.

# 1. 문제 정리

현재 flow-web은 사실상 아래 구조다.

```text
Projects list
  Project spec list
    Spec detail
```

이 구조는 spec 상세 작업에는 충분하지만 아래 문제가 남는다.

- 프로젝트가 무엇을 해결하는지 project 레벨에서 설명하는 화면이 없다.
- epic이 planning/navigation 단위로 존재하지 않아 spec 묶음의 목적과 우선순위를 잃기 쉽다.
- `/projects/:projectId`가 사실상 spec list이므로 project overview 역할을 못 한다.
- 현재 sidebar와 breadcrumb가 spec 중심이라 상위 계층 이동성이 약하다.

Flow의 상위 문서가 요구하는 정보 구조는 이미 분명하다.

```text
Project
  Epic
    Spec
      Acceptance Criteria
      Tests
      Evidence
```

따라서 flow-web도 이 계층을 UI와 API에서 반영해야 한다.

# 2. 목표 구조

권장 정보 구조는 아래와 같다.

```text
ProjectsPage
  ProjectOverviewPage
    EpicOverviewPage
      SpecDetailPage
```

권장 라우트는 아래와 같다.

```text
/                               -> ProjectsPage
/projects/:projectId            -> ProjectOverviewPage
/projects/:projectId/epics/:epicId -> EpicOverviewPage
/projects/:projectId/specs/:specId -> SpecDetailPage
```

보조 projection route는 필요 시 아래처럼 추가한다.

```text
/projects/:projectId/specs
/projects/:projectId/reviews
/projects/:projectId/failures
```

중요한 원칙은 아래와 같다.

- spec은 여전히 execution unit이다.
- epic은 planning and navigation unit이다.
- project는 방향, 범위, 우선순위, hotspot을 설명하는 top-level unit이다.
- state transition, assignment, review request는 계속 spec에만 연결된다.

# 3. 프로젝트 뷰가 의미해야 하는 것

프로젝트 뷰는 단순한 프로젝트 제목 화면이 아니라 아래 질문에 답하는 화면이어야 한다.

- 이 프로젝트는 무엇을 해결하는가?
- 현재 어떤 epic이 있고, 각각 왜 필요한가?
- 어떤 epic이 가장 막혀 있는가?
- 어떤 spec들이 어느 epic 아래에 있는가?
- review, failure, dependency hotspot이 어디에 몰려 있는가?

즉 프로젝트 뷰는 `프로젝트 문서 + 운영 집계 + 탐색 허브`여야 한다.

프로젝트 뷰가 보여줘야 하는 정보는 아래 5개 묶음이다.

1. 방향: 프로젝트 정의, 문제, 목표, non-goals, architecture principle
2. 구조: epic index, epic별 목적, 포함 범위
3. 진행: epic별 spec 진행률, 상태 분포, open review 수
4. hotspot: review/failure/dependency bottleneck, 최근 activity
5. 탐색: epic으로 들어가거나 spec으로 바로 가는 entry point

# 4. 프로젝트 뷰 UI 계획

프로젝트 뷰의 권장 섹션은 아래와 같다.

## 4.1 Project Header

표시 정보:

- project title
- 한 줄 summary
- 현재 주요 목표
- 전체 spec 수
- active epics 수
- open review 수
- failed/on-hold spec 수
- last activity at

이 영역은 사용자가 프로젝트에 들어오자마자 `무슨 프로젝트이고 지금 어디가 뜨거운가`를 파악하게 해야 한다.

## 4.2 Project Document Summary

표시 정보:

- problem
- goals
- non-goals
- context and constraints
- architecture overview

여기서는 [flow-project-document.md](d:\Projects\flow\docs\flow-project-document.md)의 핵심 내용을 재구성한다. 전체 문서를 그대로 덤프하는 것이 아니라, project view에 맞는 section 카드로 나눠서 보여주는 편이 낫다.

## 4.3 Epic Index

프로젝트 뷰의 핵심 섹션이다.

각 epic 카드에 최소한 아래 정보를 표시한다.

- epic id
- title
- short summary
- owning subsystem
- priority
- milestone
- child spec count
- completed / active / blocked counts
- open review count
- dependency risk badge
- 링크: epic view로 이동

이 섹션은 project view의 중심이어야 한다. spec list가 프로젝트 뷰의 중심이 되면 다시 spec-first 화면으로 돌아간다.

## 4.4 Hotspots

표시 정보:

- review hotspot top N
- failure/on-hold hotspot top N
- dependency hotspot top N
- recent activity summary

이 영역은 운영적인 우선순위를 보여준다. project 문서만 보여주고 끝내면 운영 화면으로서 가치가 약하다.

## 4.5 Spec Index Preview

프로젝트 뷰에는 전체 spec 상세 대신 아래 정도만 둔다.

- 최근 업데이트 spec
- review 필요 spec
- blocked spec
- 검색 또는 quick filter entry

전체 spec list는 별도 projection이나 하단 접이식 섹션으로 두는 편이 낫다. 프로젝트 뷰가 spec 목록으로 잠식되면 계층이 무너진다.

# 5. 프로젝트 뷰 데이터 구조

프로젝트 뷰 데이터는 `문서 원본 모델`과 `화면용 read model`로 나누는 것이 안전하다.

이 구분이 필요한 이유는 아래와 같다.

- 프로젝트 문서의 서술형 본문은 사람이 관리한다.
- open review count, recent activity, hotspot은 런타임에서 계산된다.
- 편집 가능한 서술 필드와 계산 필드를 한 JSON에 섞으면 책임이 흐려진다.

## 5.1 ProjectDocument JSON

이 모델은 사람이 작성하는 상위 문맥이다.

```json
{
  "projectId": "flow",
  "version": 1,
  "title": "Flow",
  "summary": "Spec-based development workflow platform.",
  "problem": "요구사항, 구현, 테스트, 검토, evidence가 분리되어 추적이 어렵다.",
  "goals": [
    "spec을 authoritative source로 삼는다.",
    "runner, webservice, CLI, extension이 같은 core contract를 공유한다.",
    "구현, 테스트, 검토, evidence를 하나의 흐름으로 연결한다."
  ],
  "nonGoals": [
    "범용 PM 툴 전체 대체",
    "모든 코드 호스팅 플랫폼 워크플로 직접 흡수"
  ],
  "contextAndConstraints": [
    "상태 전이는 core가 담당한다.",
    "UI는 state를 직접 수정하지 않는다.",
    "project/epic은 planning and navigation layer다."
  ],
  "architectureOverview": [
    "spec graph",
    "flow core",
    "runner",
    "webservice",
    "tooling"
  ],
  "milestones": [
    {
      "id": "M1",
      "title": "Core runtime stabilization",
      "status": "inProgress"
    },
    {
      "id": "M2",
      "title": "Integrated workspace hierarchy",
      "status": "planned"
    }
  ],
  "relatedDocs": [
    "docs/flow-project-document.md",
    "docs/flow-epic-plan.md",
    "docs/flow-webservice-integrated-workspace.md"
  ]
}
```

## 5.2 ProjectView JSON

이 모델은 flow-web이 실제로 렌더링하는 read model이다.

```json
{
  "projectId": "flow",
  "title": "Flow",
  "summary": "Spec-based development workflow platform.",
  "documentVersion": 1,
  "lastActivityAt": "2026-03-22T07:40:00Z",
  "stats": {
    "specCount": 42,
    "epicCount": 5,
    "activeEpicCount": 3,
    "openReviewCount": 4,
    "failedSpecCount": 2,
    "onHoldSpecCount": 1
  },
  "document": {
    "problem": "요구사항, 구현, 테스트, 검토, evidence가 분리되어 추적이 어렵다.",
    "goals": [
      "spec을 authoritative source로 삼는다.",
      "동일 contract를 여러 채널이 공유한다."
    ],
    "nonGoals": [
      "범용 PM 툴 전체 대체"
    ],
    "contextAndConstraints": [
      "UI는 state를 직접 수정하지 않는다."
    ],
    "architectureOverview": [
      "flow core",
      "runner",
      "webservice"
    ]
  },
  "epics": [
    {
      "epicId": "EPIC-C",
      "title": "Integrated Workspace Experience",
      "summary": "Project, epic, spec hierarchy in one workspace.",
      "priority": "high",
      "milestone": "M2",
      "owner": "flow-web",
      "specCounts": {
        "total": 8,
        "completed": 2,
        "active": 3,
        "blocked": 1,
        "review": 2
      },
      "hotspots": {
        "openReviewCount": 2,
        "failedSpecCount": 0,
        "dependencyRisk": "medium"
      },
      "entrySpecIds": ["F-001", "F-018"]
    }
  ],
  "hotspots": {
    "review": [
      {
        "specId": "F-018",
        "title": "Epic grouping filter",
        "epicId": "EPIC-C",
        "reason": "2 open review requests"
      }
    ],
    "failure": [],
    "dependency": [
      {
        "specId": "F-021",
        "title": "Project document persistence",
        "epicId": "EPIC-C",
        "reason": "blocked by F-003, F-010"
      }
    ]
  },
  "recentActivity": [
    {
      "timestamp": "2026-03-22T07:35:00Z",
      "specId": "F-001",
      "message": "review response submitted"
    }
  ],
  "specPreview": {
    "recent": ["F-001", "F-018"],
    "reviewNeeded": ["F-018"],
    "blocked": ["F-021"]
  }
}
```

## 5.3 해석 원칙

- `ProjectDocument`는 authoring source다.
- `ProjectView`는 API에서 조합한 projection이다.
- project view는 상태 기계를 가지지 않는다.
- project progress는 epic/spec aggregate로 계산한다.

# 6. 에픽 뷰가 의미해야 하는 것

에픽 뷰는 project와 spec 사이의 다리다.

에픽 뷰는 아래 질문에 답해야 한다.

- 이 epic은 왜 존재하는가?
- 어떤 spec 묶음으로 이 epic을 실현하는가?
- 어떤 spec이 지금 병목인가?
- milestone 관점에서 어디까지 왔는가?
- 어떤 관련 설계 문서를 먼저 읽어야 하는가?

즉 에픽 뷰는 `서술형 계획 + child spec 운영 허브`여야 한다.

중요한 점은 아래다.

- epic은 spec을 대체하지 않는다.
- epic은 직접 assignment를 가지지 않는다.
- epic 상태는 직접 저장하지 않고 child spec 집계에서 유도해도 충분하다.

# 7. 에픽 뷰 UI 계획

권장 섹션은 아래와 같다.

## 7.1 Epic Header

표시 정보:

- epic id
- title
- short summary
- owner
- priority
- milestone
- progress bar
- child spec count
- open review count
- blocked spec count

## 7.2 Epic Narrative

표시 정보:

- problem
- goal
- scope
- non-goals
- success criteria

이 섹션은 [flow-epic-plan.md](d:\Projects\flow\docs\flow-epic-plan.md)의 epic 문서 권장 템플릿과 맞춰야 한다.

## 7.3 Child Specs

에픽 뷰의 중심 섹션이다.

표시 정보:

- spec id
- title
- state
- processingStatus
- risk level
- open review count
- dependency badge
- last activity

여기서는 current SpecsPage를 거의 그대로 재사용할 수 있다. 다만 범위가 `프로젝트 전체 spec`이 아니라 `해당 epic의 child specs`여야 한다.

## 7.4 Milestones and Dependencies

표시 정보:

- epic milestone 목록
- 선행 epic 또는 외부 dependency
- blocked child specs
- dependency risk summary

## 7.5 Related Docs and Hotspots

표시 정보:

- related design docs
- review hotspot
- failure hotspot
- recently updated specs

# 8. 에픽 뷰 데이터 구조

에픽도 프로젝트와 마찬가지로 `문서 원본 모델`과 `화면용 read model`로 나누는 편이 낫다.

## 8.1 EpicDocument JSON

```json
{
  "projectId": "flow",
  "epicId": "EPIC-C",
  "version": 1,
  "title": "Integrated Workspace Experience",
  "summary": "단일 spec view를 project/epic/spec 계층으로 확장한다.",
  "problem": "spec 상세는 강하지만 상위 계획과 묶음 탐색이 부족하다.",
  "goal": "프로젝트 문맥, epic 문맥, spec 작업 화면을 한 흐름으로 연결한다.",
  "scope": [
    "project overview",
    "epic overview",
    "epic grouping and filtering",
    "spec 상위 breadcrumb"
  ],
  "nonGoals": [
    "epic 자체 상태 기계 도입",
    "assignment를 epic에 직접 연결"
  ],
  "successCriteria": [
    "spec이 어느 epic에 속하는지 잃지 않는다.",
    "project에서 epic으로, epic에서 spec으로 자연스럽게 이동한다."
  ],
  "childSpecIds": ["F-001", "F-018", "F-021"],
  "dependencies": ["EPIC-A", "EPIC-B"],
  "milestones": [
    {
      "id": "M2-1",
      "title": "Project overview read model",
      "status": "inProgress"
    },
    {
      "id": "M2-2",
      "title": "Epic overview navigation",
      "status": "planned"
    }
  ],
  "relatedDocs": [
    "docs/flow-webservice-integrated-workspace.md",
    "docs/flow-phase6-webservice-decisions.md"
  ]
}
```

## 8.2 EpicView JSON

```json
{
  "projectId": "flow",
  "epicId": "EPIC-C",
  "title": "Integrated Workspace Experience",
  "summary": "Project, epic, spec hierarchy in one workspace.",
  "documentVersion": 1,
  "priority": "high",
  "owner": "flow-web",
  "milestone": "M2",
  "progress": {
    "totalSpecs": 8,
    "completedSpecs": 2,
    "activeSpecs": 3,
    "blockedSpecs": 1,
    "completionRatio": 0.25
  },
  "narrative": {
    "problem": "spec 상세는 강하지만 상위 계획과 묶음 탐색이 부족하다.",
    "goal": "project, epic, spec 계층을 한 화면 흐름으로 연결한다.",
    "scope": [
      "project overview",
      "epic overview",
      "spec breadcrumb"
    ],
    "nonGoals": [
      "epic 상태 전이 도입"
    ],
    "successCriteria": [
      "epic 단위 child spec 추적 가능"
    ]
  },
  "childSpecs": [
    {
      "specId": "F-001",
      "title": "Project document review",
      "state": "review",
      "processingStatus": "userReview",
      "riskLevel": "medium",
      "openReviewCount": 1,
      "dependencyStatus": "clear",
      "lastActivityAt": "2026-03-22T07:35:00Z"
    }
  ],
  "hotspots": {
    "review": ["F-001"],
    "blocked": ["F-021"],
    "recent": ["F-018", "F-001"]
  },
  "dependencies": {
    "epicDependsOn": ["EPIC-A", "EPIC-B"],
    "blockedBySpecs": ["F-003"]
  },
  "relatedDocs": [
    "docs/flow-webservice-integrated-workspace.md"
  ]
}
```

# 9. 스펙과 에픽 연결 방식

프로젝트/에픽 뷰를 넣으려면 spec이 어느 epic에 속하는지 알아야 한다.

초기 권장 방식은 아래다.

## 9.1 Spec에 epicId 추가

spec 저장 모델에 아래 필드를 추가한다.

```json
{
  "id": "F-001",
  "projectId": "flow",
  "epicId": "EPIC-C"
}
```

이 방식의 장점은 아래와 같다.

- epic별 child spec 필터가 단순해진다.
- breadcrumb와 backlink를 쉽게 구성할 수 있다.
- spec 상세에서 상위 epic context를 바로 로드할 수 있다.

## 9.2 초기 이행 전략

기존 spec에 epicId가 없을 수 있으므로 초기에는 아래 순서가 적절하다.

1. epic document의 `childSpecIds`를 기준으로 read model 생성
2. 이후 spec에 `epicId`를 반영
3. 최종적으로는 `spec.epicId`를 기본 참조로 삼고 epic document의 childSpecIds는 검증용으로만 사용

즉 처음부터 저장소 전체 마이그레이션을 강제할 필요는 없다.

# 10. 현재 flow-web UI에 넣는 방법

현재 flow-web 구조는 아래와 같다.

- `/` = project list
- `/projects/:projectId` = spec list
- `/projects/:projectId/specs/:specId` = spec detail
- 왼쪽 sidebar는 project 내부 spec 탐색에 특화되어 있음

이 상태에서 가장 자연스러운 변경은 아래다.

## 10.1 라우트 승격

`/projects/:projectId`를 더 이상 spec list로 쓰지 않고 `ProjectOverviewPage`로 승격한다.

기존 spec list는 아래 둘 중 하나로 이동한다.

1. `/projects/:projectId/specs`
2. project view 내부 탭 또는 section

권장은 1번이다. 이유는 route 의미가 더 명확하기 때문이다.

## 10.2 EpicOverviewPage 추가

새 route:

```text
/projects/:projectId/epics/:epicId
```

이 화면은 현재 SpecsPage의 list 성격과 SpecDetailPage의 section 성격을 절충한 브리지 화면이 된다.

## 10.3 SpecDetailPage는 유지하되 상위 문맥만 추가

현재 spec view는 이미 강하다. 따라서 아래만 추가하면 된다.

- breadcrumb: `Flow > Project > Epic > Spec`
- spec header에 epic badge/link
- sidebar 상단에 current epic summary card
- spec related navigation에 sibling specs in same epic

spec detail의 notebook-style section 구조는 그대로 두는 편이 맞다.

## 10.4 Sidebar를 route-aware로 변경

현재 Sidebar는 spec 중심이다. 앞으로는 route에 따라 모드를 바꿔야 한다.

### Project view sidebar

- project sections
- epic list
- hotspot quick links
- spec search shortcut

### Epic view sidebar

- epic sections
- child spec list
- milestone anchors
- related docs

### Spec view sidebar

- 기존 문서 section navigation 유지
- 상단에 project/epic context block 추가

이 방식이면 layout을 새로 갈아엎지 않고 기존 shell을 확장할 수 있다.

# 11. API 계획

현재 API는 project 목록과 spec 단위 API만 있다. 프로젝트/에픽 뷰를 위해 아래 read endpoint가 필요하다.

## 11.1 Project endpoints

```text
GET /api/projects
GET /api/projects/{projectId}/view
GET /api/projects/{projectId}/document
GET /api/projects/{projectId}/epics
```

권장 응답:

- `/view` -> `ProjectView`
- `/document` -> `ProjectDocument`
- `/epics` -> epic summary list

## 11.2 Epic endpoints

```text
GET /api/projects/{projectId}/epics/{epicId}/view
GET /api/projects/{projectId}/epics/{epicId}/document
GET /api/projects/{projectId}/epics/{epicId}/specs
```

권장 응답:

- `/view` -> `EpicView`
- `/document` -> `EpicDocument`
- `/specs` -> epic 범위로 제한된 spec list

## 11.3 기존 endpoint와의 관계

- `GET /api/projects/{projectId}/specs`는 유지한다.
- 여기에 `epicId` query filter를 추가해도 좋다.
- spec detail 관련 endpoint는 그대로 유지한다.

# 12. 구현 단계 제안

## Phase 1. 데이터 계약 추가

목표:

- `ProjectDocument`, `ProjectView`, `EpicDocument`, `EpicView` 타입 정의
- 최소 mock 또는 hand-authored JSON 준비
- spec와 epic 매핑 규칙 정의

완료 기준:

- 프론트엔드가 project/epic view를 렌더링할 데이터 계약을 갖는다.

## Phase 2. ProjectOverviewPage 도입

목표:

- `/projects/:projectId`를 project overview로 교체
- project header, document summary, epic index, hotspots 렌더링
- sidebar project mode 추가

완료 기준:

- 사용자가 project에 진입하면 spec list가 아니라 project 문맥을 먼저 본다.

## Phase 3. EpicOverviewPage 도입

목표:

- epic header, narrative, child specs, milestone/dependency 섹션 구현
- epic route와 breadcrumb 추가
- spec 상세와 epic 사이 backlink 연결

완료 기준:

- project -> epic -> spec 이동이 자연스럽다.

## Phase 4. Spec view 상위 문맥 연결

목표:

- spec header에 epic 정보 노출
- sidebar에 project/epic context block 추가
- same-epic sibling navigation 추가

완료 기준:

- spec 작업 중에도 상위 문맥을 잃지 않는다.

## Phase 5. 편집 및 저장 모델

목표:

- project document section 저장
- epic document section 저장
- optimistic concurrency 및 version conflict 처리

완료 기준:

- 프로젝트/에픽 문맥도 web에서 수정 가능하다.

# 13. 권장 결론

정리하면 flow-web의 다음 단계는 `spec 뷰를 더 복잡하게 만드는 것`이 아니라 `spec 위에 상위 계층을 세우는 것`이다.

권장 방향은 아래와 같다.

1. `/projects/:projectId`를 project overview로 승격한다.
2. `/projects/:projectId/epics/:epicId`를 새로 만든다.
3. spec은 계속 상세 실행 단위로 유지한다.
4. project와 epic은 JSON 기반 document model + view model로 도입한다.
5. sidebar와 breadcrumb를 route-aware hierarchy로 확장한다.

이렇게 하면 Flow가 문서에서 정의한 계층 구조를 UI에서도 그대로 반영할 수 있다.

# 14. 이번 반복 할 일 (2026-03-22)

## Done

- [x] Phase 1: flow-core에 ProjectDocument, EpicDocument 도메인 모델 추가
- [x] Phase 1: flow-api에 프로젝트/에픽 읽기 API 엔드포인트 추가
- [x] Phase 1: flow-web에 ProjectDocument, EpicDocument, ProjectView, EpicView TypeScript 타입 추가
- [x] Phase 2: ProjectOverviewPage 구현 및 라우트 승격
  - `/projects/:projectId` → ProjectOverviewPage (header, stats, epic index, document summary, All Specs 링크)
  - `/projects/:projectId/specs` → SpecsPage (기존 spec list 이동)
  - Layout breadcrumb에 Specs 중간 경로 추가
  - useProjectView, useEpics 훅 추가
  - Sidebar는 spec detail에서만 표시 (overview/specs list에서는 숨김)

## Now

- [ ] EpicOverviewPage 구현 (Phase 3)
  대상: `tools/flow-web/src/pages/EpicOverviewPage.tsx`
  라우트: `/projects/:projectId/epics/:epicId`
  내용: epic header, narrative, child specs list, milestone/dependency, related docs
  완료 기준: project → epic → spec 이동이 자연스러움

## Next

- [ ] Spec view에 상위 문맥 연결 (Phase 4): breadcrumb에 epic 정보, sidebar에 epic context block
- [ ] Sidebar route-aware 모드 전환: project view / epic view / spec view 별 sidebar 내용

## Later

- [ ] Spec에 epicId 필드 추가 및 연결
- [ ] 편집 및 저장 모델 (Phase 5)
- [ ] Hotspots 섹션 (review/failure/dependency bottleneck)