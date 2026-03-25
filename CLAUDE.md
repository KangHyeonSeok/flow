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

Iteration Notes는 장기 로그가 아니라 다음 반복이 5분 안에 맥락을 복원하는 현재 스냅샷만 유지한다.
최신 상태로 아래 섹션만 갱신하고, 지난 시간대별 기록은 이 파일에 누적하지 않는다.

```md
## Current Iteration Snapshot

- Last Updated: YYYY-MM-DD HH:MM
- Focus: 이번 반복의 핵심 범위
- Done: 직전 반복까지 실제로 끝낸 작업
- Now: 지금 바로 구현할 1~3개
- Next: 다음 반복이 바로 시작할 작업 1~3개
- Risks: 남은 문제, 불확실성, 막힌 지점
- Learnings: 다음 반복이 반드시 기억해야 할 점
- Verification: 실행한 테스트, 확인한 화면, 확인하지 못한 항목
```

중복되는 긴 서술은 피하고, 현재 우선순위와 검증 결과만 남긴다.

## Current Iteration Snapshot

- Last Updated: 2026-03-25
- Focus: Sidebar Hotspots quick links + ListEditor 공통 컴포넌트 추출.
- Done:
  - Phase 1-5 완료.
  - Hotspots 섹션 구현 (ProjectOverviewPage — review/failure/onHold).
  - useSpecEpic fallback 제거 (spec.epicId만 참조).
  - **Sidebar Hotspots quick links**: `ProjectSidebar`에 Hotspots 섹션 추가. `useProjectView`에서 hotspots 데이터를 읽어 review(yellow)/failure(red)/onHold(orange) spec 링크 표시. 핫스팟 없으면 섹션 숨김. Sections 앵커에도 `hotspots` 추가.
  - **ListEditor 공통 컴포넌트 추출**: `components/ListEditor.tsx` 신규 생성. `ProjectOverviewPage`의 `ListEditor`와 `EpicOverviewPage`의 `NarrativeListEditor`를 삭제하고 공통 컴포넌트로 교체. 미사용 `X` import 정리.
- Now:
  - 수동 UI 확인 (Hotspots sidebar + 편집 동작).
- Next:
  - `liveExecution` 최소 저장 계약과 evidence 형식 구체화.
  - Hotspots dependency bottleneck 구현 (spec.Dependencies 일관성 확보 후).
  - ProjectSidebar에 hotspot count badge 추가 (section anchor에 빨간 badge).
- Risks:
  - Hotspots UI (본문 + sidebar) 실제 데이터로 수동 확인 미완.
  - dependency bottleneck은 onHold 대리 지표 사용 중 — spec.Dependencies가 런타임에서 일관되지 않음.
- Learnings:
  - ListEditor 추출로 ProjectOverviewPage ~30줄, EpicOverviewPage ~30줄 감소. 동일 패턴이므로 공유 컴포넌트가 적절.
  - ProjectSidebar에서 `useProjectView`는 이미 호출 중이므로 hotspots에 추가 쿼리 불필요.
- Verification:
  - flow-web `tsc --noEmit` 통과 (에러 0).
  - flow-api `dotnet build` 통과.
  - 수동 UI 확인 미완.

## 작업 방식 규칙

- 문서 업데이트 없이 구현만 하고 끝내지 않는다.
- TODO는 채팅에만 남기지 말고 저장소 문서에도 남긴다.
- 막힌 이유가 있으면 원인과 재시도 조건을 적는다.
- 큰 결정은 설계 문서에 반영하고, 이 파일에는 요약과 후속 액션만 남긴다.
- 다음 반복이 시작 5분 안에 맥락을 복원할 수 있어야 한다.

## 2026-03-25 (scheduled iteration — Sidebar Hotspots + ListEditor)

- Context: Hotspots 구현 완료 후 — Sidebar quick links 추가와 공통 컴포넌트 추출.
- Done: ProjectSidebar에 Hotspots quick links 섹션 추가 (review/failure/onHold 색상 구분). ListEditor 공통 컴포넌트 추출 (ProjectOverviewPage, EpicOverviewPage 중복 제거).
- Next: liveExecution 계약, dependency bottleneck 구현, hotspot count badge 빨간 액센트.
- Risks: Hotspots UI (본문+sidebar) 실제 데이터로 수동 확인 미완. dependency bottleneck은 onHold 대리 지표.
- Learnings: ListEditor 추출로 ~60줄 중복 제거. ProjectSidebar에서 useProjectView 이미 호출 중이므로 hotspots 추가 쿼리 불필요.
- Verification: `tsc --noEmit` 통과, `dotnet build` 통과.