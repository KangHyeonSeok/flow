# Flow 프로젝트 문서

이 문서는 Flow 전체를 설명하는 상위 프로젝트 문서다. 개별 spec 문서가 기능 단위의 작업 계약이라면, 이 문서는 왜 Flow가 존재하는지, 어떤 문제를 풀고 있는지, 어떤 하위 epic과 spec으로 나뉘는지 설명하는 기준 문서다.

# 1. 문서 목적

- Flow 프로젝트의 문제 정의와 목표를 한 문서에서 파악할 수 있게 한다.
- 여러 phase 문서, 설계 결정 문서, 개별 spec 문서의 상위 문맥을 제공한다.
- 프로젝트 수준의 scope, non-goals, architecture principle, epic index를 고정한다.
- web, runner, CLI, extension, build/test 도구가 같은 방향을 보게 한다.

# 2. 프로젝트 개요

## 2.1 한 줄 정의

Flow는 스펙 기반 개발 워크플로를 정의하고 실행하는 통합 도구 모음이다.

## 2.2 해결하려는 문제

일반적인 개발 흐름에서는 요구사항 문서, 작업 분해, 구현 상태, 테스트 결과, 검토 요청, 증거 수집이 서로 다른 도구와 화면에 흩어진다. 이 구조에서는 아래 문제가 반복된다.

- 요구사항과 실제 구현 상태가 분리된다.
- 리뷰와 테스트가 문서의 일부가 아니라 사후 활동으로 밀린다.
- 자동화 agent가 무엇을 읽고 어떤 상태를 바꿔야 하는지 계약이 불명확하다.
- 사람과 agent가 같은 단위를 중심으로 협업하지 못한다.

Flow는 spec을 authoritative source로 삼아 이 문제를 줄이는 것을 목표로 한다.

# 3. 프로젝트 목표

## 3.1 핵심 목표

1. spec을 기준으로 문제, 목표, acceptance criteria, 테스트, 상태, evidence를 함께 관리한다.
2. runner, webservice, CLI, extension, 외부 채널이 같은 core contract를 공유하게 한다.
3. 구현, 테스트, 검토, 사용자 응답, 활동 로그를 하나의 추적 가능한 흐름으로 연결한다.

## 3.2 제품 목표

- spec 작성부터 완료 판정까지의 흐름을 끊기지 않게 만든다.
- 상태 전이를 문서 밖 로직이 아니라 명시적인 규칙으로 관리한다.
- 단일 spec 문서와 다중 spec 운영 뷰를 같은 모델 위에 올린다.
- 사람과 agent 모두가 읽을 수 있는 문서 구조를 유지한다.

# 4. Non-goals

현재 Flow가 직접 해결하려는 범위는 아래까지다. 아래 항목은 의도적으로 중심 목표에서 제외한다.

- 범용 PM 툴 전체를 대체하는 일
- 모든 코드 호스팅 플랫폼의 워크플로를 직접 흡수하는 일
- 노트북 실행 엔진 자체를 제공하는 일
- 별도 계약 없이 임의의 agent가 상태를 직접 수정하게 허용하는 일
- 런타임에서 old/new spec 포맷을 동시에 장기 지원하는 일

# 5. 핵심 원칙

## 5.1 Spec이 authoritative source다

- 프로젝트 운영의 기준 단위는 spec이다.
- 상위 프로젝트 문서와 epic 문서는 spec을 대체하지 않고 방향과 묶음을 제공한다.

## 5.2 상태 전이는 core가 담당한다

- UI와 외부 채널은 state를 직접 수정하지 않는다.
- 상태 변경은 event proposal 또는 command request를 통해서만 일어난다.

## 5.3 문서와 운영 맥락은 분리하되 연결한다

- problem, goal, context, acceptance criteria는 문서 본문이다.
- assignment, review request, evidence, activity는 운영 정보다.
- 둘은 같은 spec 안에서 함께 보이되 책임은 분리한다.

## 5.4 큰 문서는 계층으로 나눈다

큰 프로젝트는 한 문서에 모든 내용을 넣지 않는다. Flow의 기본 계층은 아래와 같다.

1. 프로젝트 문서
2. epic 문서
3. spec 문서
4. acceptance criteria / tests / evidence

여기에 실행 전 선택이 필요한 문제는 `Decision Record`, 외부 변화 입력은 `Change Record`로 별도 연결한다.

# 6. 정보 구조

## 6.1 권장 계층

```text
Project
  Epic
    Spec
      Acceptance Criteria
      Tests
      Evidence
```

## 6.2 각 계층의 역할

### 프로젝트 문서

- 프로젝트 전체 비전과 범위를 설명한다.
- 하위 epic의 우선순위와 관계를 보여준다.
- 공통 제약과 의사결정 문맥을 유지한다.

### Epic 문서

- 하나의 사용자 가치 또는 큰 운영 흐름을 설명한다.
- 여러 spec을 묶는 상위 목표와 완료 조건을 제공한다.
- cross-spec dependency와 milestone을 설명한다.

### Spec 문서

- 실제 구현/검증 가능한 작업 단위다.
- 상태 전이와 assignment dispatch의 기준 단위다.

# 7. 주요 구성 요소

Flow는 아래 서브시스템으로 구성된다.

- spec graph: 스펙 생성, 검증, 그래프 분석, 영향 분석
- flow core: 상태 규칙, 저장 계약, review request, decision record, activity, assignment 모델
- runner: dispatch, timeout recovery, review loop, 자동화 orchestration
- webservice / flow-web: notebook-style spec workspace, projection mode, section 저장
- build/test: 플랫폼 감지, 빌드, 테스트, 실행
- e2e: 시나리오 기반 end-to-end 검증
- rag db: 작업 기록과 문서 검색
- capture / vlm: 시각적 evidence 보조
- vscode extension: 에디터 안에서 Flow 작업 보조

# 8. 프로젝트 문서의 권장 목차

앞으로 Flow에서 프로젝트 문서를 만들 때는 아래 목차를 기본으로 삼는다.

1. Document Summary
2. Problem
3. Goals
4. Non-goals
5. Context and Constraints
6. Architecture Overview
7. Epic Index
8. Milestones
9. Risks and Open Questions
10. Related Specs and Design Docs

# 9. 현재 Flow의 Epic Index

현재 저장소 기준의 상위 epic 묶음은 아래처럼 보는 것이 자연스럽다.

## Epic A. Core Spec Runtime

- 상태 규칙
- 저장 계약
- review request lifecycle
- activity / assignment / evidence 모델

관련 문서:

- flow-state-rule.md
- flow-schema.md
- flow-phase1-requirements.md
- flow-phase2-requirements.md

## Epic B. Runner Automation

- dispatch
- retry / timeout / review loop
- assignment orchestration

관련 문서:

- flow-phase3-runner-decisions.md
- flow-phase4-review-loop-decisions.md
- flow-phase5-real-agent-decisions.md
- flow-runner-agent-contract.md

## Epic C. Integrated Workspace Web

- notebook-style spec detail
- section 단위 저장
- review/evidence/activity projection
- kanban/review-only/failure view

관련 문서:

- flow-phase6-webservice-decisions.md
- flow-webservice-integrated-workspace.md
- flow-web-개발계획.md

## Epic D. Developer Tooling

- build/test wrapper
- e2e scenario execution
- capture / embed / rag integration
- console / extension experience

# 10. 운영 기준

프로젝트 문서는 아래 역할을 수행해야 한다.

- 새로운 사람이나 agent가 프로젝트 방향을 10분 안에 파악하게 한다.
- 각 epic이 왜 필요한지 설명한다.
- 개별 spec이 어디에 속하는지 추적 가능하게 한다.
- 상위 범위 변경 시 어떤 epic과 spec이 영향을 받는지 판단할 수 있게 한다.

반대로 아래 역할은 프로젝트 문서의 책임이 아니다.

- 개별 구현 단계의 세부 acceptance criteria 정의
- 개별 assignment 상태 추적
- event 단위 활동 로그 보관

# 11. Web 관점에서의 적용

Flow web에서 프로젝트 문서는 별도 top-level view로 두는 것이 적절하다.

권장 구성:

1. Project Overview
2. Epic List
3. Spec Index
4. Recent Activity Summary
5. Review / Failure / Dependency Hotspots

즉 현재 단일 spec 상세 화면은 유지하되, 그 위에 project overview와 epic overview가 추가되는 구조가 이상적이다.

# 12. 성공 기준

프로젝트 문서가 잘 작동한다고 보려면 아래 질문에 답할 수 있어야 한다.

- Flow가 무엇을 해결하는 프로젝트인가?
- 현재 어떤 epic이 있고 우선순위는 무엇인가?
- 특정 spec이 어떤 epic과 목표에 속하는가?
- 공통 제약과 운영 원칙은 무엇인가?

# 13. 다음 문서

이 문서의 하위 문서로는 아래가 자연스럽다.

- change-driven 운영 문서
- decision record 운영 문서
- epic 문서
- epic별 spec index
- 개별 spec 문서
- 운영 projection 문서
- CLI 반복 운영 지침 문서