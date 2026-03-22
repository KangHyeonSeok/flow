# Flow 에픽 계획

이 문서는 Flow를 epic 단위로 운영하기 위한 계획 문서다. 목적은 개별 spec을 직접 나열하는 것이 아니라, 여러 spec을 어떤 사용자 가치와 운영 흐름으로 묶을지 정의하는 데 있다.

# 1. 왜 epic이 필요한가

현재 Flow는 개별 spec 문서를 충분히 다룰 수 있는 방향으로 가고 있다. 하지만 spec 수가 늘어나면 아래 문제가 생긴다.

- 어떤 spec이 같은 목적을 향하는지 한눈에 보기 어렵다.
- 프로젝트 레벨 목표와 개별 spec 사이의 연결이 약해진다.
- spec 목록만으로는 우선순위와 milestone을 설명하기 어렵다.
- 큰 기능을 분해한 뒤 다시 묶어보는 상위 문맥이 부족하다.

Epic은 이 간극을 메운다.

# 2. Epic 정의

Flow에서 epic은 여러 spec을 묶는 상위 계획 단위다.

Epic의 성격은 아래와 같다.

- 하나의 사용자 가치 또는 운영 능력을 설명한다.
- 여러 spec을 포함할 수 있다.
- 자체적인 목표, scope, milestone, 완료 조건을 가진다.
- 상태 전이의 직접 단위는 아니다.

즉 epic은 execution unit이 아니라 planning and navigation unit이다.

# 3. Project / Epic / Spec 관계

```text
Project
  Epic
    Spec
      Assignment
      Review Request
      Test
      Evidence
```

정리하면 아래와 같다.

- Project: 전체 비전과 범위
- Epic: 큰 가치 묶음과 일정 단위
- Spec: 실제 구현/검증 단위
- Assignment/Review/Test/Evidence: 운영 단위

# 4. Epic 문서의 최소 필드

Epic 문서는 최소한 아래 내용을 가져야 한다.

## 4.1 필수

- epic id
- title
- problem
- goal
- scope
- non-goals
- success criteria
- child specs
- dependencies
- milestone 또는 target state

## 4.2 권장

- owner 또는 owning subsystem
- risk summary
- open questions
- related design docs
- rollout note

# 5. Epic 문서 권장 템플릿

아래는 Flow에서 권장하는 epic 문서 템플릿이다.

## Document Summary

- 이 epic이 무엇인지 한 단락으로 설명한다.

## Problem

- 현재 어떤 운영상/사용자상 문제가 있는가.

## Goal

- 이 epic이 끝났을 때 무엇이 달라져야 하는가.

## Scope

- 어떤 spec 묶음까지 포함하는가.

## Non-goals

- 이번 epic에서 의도적으로 제외하는 범위.

## Success Criteria

- epic 완료를 판단하는 상위 기준.

## Child Specs

- 포함되는 spec 목록과 각 spec의 역할.

## Dependencies

- 선행 epic 또는 외부 제약.

## Milestones

- spec들을 어떤 순서로 닫아갈지.

## Risks

- 실패 가능성과 병목.

## Related Docs

- 설계 결정 문서, phase 문서, API 문서.

# 6. Epic 운영 규칙

## 6.1 너무 큰 umbrella epic을 만들지 않는다

Epic은 목록을 예쁘게 보이기 위한 상자가 아니라 실제 우선순위와 완료 조건을 설명해야 한다. 하위 spec이 지나치게 많아지면 epic도 다시 분리한다.

## 6.2 spec을 대체하지 않는다

Epic은 spec의 acceptance criteria를 직접 들고 있지 않는다. 검증 가능한 세부 조건은 계속 spec에 둔다.

## 6.3 cross-cutting rule은 epic보다 상위 문서에 둔다

프로젝트 전체에 적용되는 제약은 프로젝트 문서나 설계 문서에 두고, 특정 epic 문서에 중복하지 않는다.

## 6.4 epic 완료는 spec 묶음 완료와 동일하지 않을 수 있다

Spec이 모두 닫혀도 rollout, migration, 운영 검증 같은 상위 조건이 남아 있을 수 있다. 따라서 epic은 별도 success criteria를 가져야 한다.

# 7. 현재 Flow 기준 추천 Epic 분해

아래는 현재 저장소 기준으로 자연스러운 epic 분해안이다.

## Epic 1. Spec Foundation

목표:

- spec schema, 상태 규칙, 저장 계약을 안정화한다.

포함 범위:

- flow-state-rule
- flow-schema
- phase1 / phase2 requirements 반영

예상 child spec 예시:

- spec schema 필드 확장
- retry counter 규칙 보강
- activity / review / assignment 저장 계약 개선

## Epic 2. Runner Automation Loop

목표:

- spec dispatch부터 review loop까지 자동화 흐름을 안정화한다.

포함 범위:

- assignment orchestration
- timeout recovery
- rework/user review/architect review 루프

예상 child spec 예시:

- test generation timeout 분리
- stale assignment recovery
- retry exhaustion handling

## Epic 3. Integrated Workspace Experience

목표:

- 단일 spec 문서와 다중 spec 운영 뷰를 같은 workspace 안에 통합한다.

포함 범위:

- spec detail notebook view
- project overview
- epic overview
- spec index / projection mode

예상 child spec 예시:

- overview/context/non-goals 편집
- dependencies / BDD / evidence 가독성 개선
- project document view 추가
- epic grouping / filtering 추가

## Epic 4. Validation and Evidence

목표:

- AC, tests, evidence의 추적 가능성을 강화한다.

포함 범위:

- AC-test 연결
- evidence surfacing
- validation commands
- user review evidence

## Epic 5. Tooling and External Channels

목표:

- build/test/e2e/rag/capture/extension 경로를 Flow 작업 흐름에 자연스럽게 연결한다.

포함 범위:

- build wrappers
- e2e adapters
- capture / vlm
- VS Code / Slack integration

# 8. Web 구현 관점의 단계별 계획

프로젝트 문서와 epic 기능을 flow-web에 도입하려면 아래 순서가 적절하다.

## Phase A. 읽기 모델 추가

- 프로젝트 overview 화면 추가
- 프로젝트 문서 본문 섹션 정의
- epic 카드 또는 표 기반 인덱스 추가
- spec 목록에 epic 그룹핑 정보 노출

완료 기준:

- 사용자가 프로젝트에 들어오면 spec 목록보다 먼저 상위 문맥을 볼 수 있다.

## Phase B. 내비게이션 연결

- project -> epic -> spec 이동 구조 추가
- spec 상세에서 상위 epic 링크 노출
- epic별 필터링과 집계 표시

완료 기준:

- 어떤 spec이 어느 epic에 속하는지 잃지 않는다.

## Phase C. 편집 모델 추가

- project document section 저장
- epic 문서 section 저장
- version conflict 처리

완료 기준:

- 프로젝트/epic 수준 문맥을 web에서 수정 가능하다.

## Phase D. 운영 projection 통합

- epic별 review hotspot
- epic별 dependency risk
- epic milestone progress

완료 기준:

- 단순 문서가 아니라 운영 뷰로서도 의미가 생긴다.

# 9. API / 저장 모델 제안

현재 모델은 `Project`와 `Spec` 중심이다. epic을 도입할 때는 처음부터 상태 기계를 epic으로 확장하기보다, 문서/탐색 모델로 가볍게 시작하는 편이 안전하다.

권장 순서:

1. project document를 project 메타데이터 또는 별도 문서 파일로 도입
2. epic document를 project 하위 컬렉션으로 도입
3. spec에 `epicId` 또는 상응하는 grouping 필드를 추가
4. 집계와 projection은 read model에서 계산

초기에는 아래 원칙을 유지한다.

- epic은 state transition 단위가 아니다.
- assignment는 여전히 spec에만 연결된다.
- review request도 spec에만 연결된다.

# 10. 완료 판단 기준

에픽 계획이 유효하다고 보려면 아래 질문에 답할 수 있어야 한다.

- 프로젝트 목표가 어떤 epic으로 분해되는가?
- 각 epic은 어떤 spec 묶음으로 실현되는가?
- 어떤 epic이 병목인지, 왜 그런지 설명 가능한가?
- 상위 계획 변경이 spec 구조에 어떻게 반영되는가?

# 11. 다음 작업 제안

이 문서 다음으로 자연스러운 작업은 아래다.

1. project document의 저장 계약 정의
2. epic 문서 최소 스키마 정의
3. spec과 epic 연결 방식 결정
4. flow-web의 project overview / epic overview 와이어프레임 작성