# Flow Webservice Integrated Workspace

이 문서는 Flow webservice를 "스펙 편집기", "전체 스펙 뷰어", "칸반", "검토 요청함"으로 나누지 않고, 하나의 통합 workspace view로 재구성하기 위한 정보 구조 문서다.

핵심 아이디어는 Jupyter notebook의 사용감을 참고하되, 실제 Jupyter 실행 모델을 그대로 가져오는 것이 아니라 "spec 중심 notebook-style workspace"를 만드는 것이다.

# 1. 목표

- 하나의 spec 안에서 보기, 편집, 검토, 테스트, evidence, 실행 요청을 모두 다룬다.
- 목록 화면과 상세 화면의 단절을 줄인다.
- 칸반, 검토 요청함, 스펙 문서 뷰어를 같은 데이터의 다른 projection으로 통합한다.
- 사용자는 한 줄 spec을 훑다가 필요한 spec만 펼쳐 모든 운영 정보를 본다.
- 상태 변경은 여전히 rule evaluator와 Spec Manager를 통해서만 일어나게 한다.

# 2. 핵심 원칙

## 2.1 notebook-style, not notebook-engine

이 화면은 Jupyter처럼 보일 수는 있지만, 실제 notebook cell 실행 모델을 그대로 따를 필요는 없다.

중요한 것은 아래다.

- 문서와 실행 컨텍스트가 한 화면에 붙어 있어야 한다.
- 사용자는 spec의 한 줄 요약과 전체 세부 정보를 같은 흐름 안에서 오갈 수 있어야 한다.
- 각 섹션은 독립적으로 열고 닫을 수 있어야 한다.
- 각 섹션은 자체 저장/갱신 단위를 가져야 한다.

즉 이 뷰는 "Jupyter notebook"보다 "접을 수 있는 spec workspace"에 더 가깝다.

## 2.2 state 변경은 직접 하지 않는다

UI는 state를 직접 변경하면 안 된다.

- UI action은 event proposal 또는 command request만 생성한다.
- 실제 state transition은 rule evaluator와 Spec Manager가 수행한다.
- UI는 결과를 보여주는 projection이어야 한다.

## 2.3 section 단위 저장

전체 spec을 한 번에 저장하는 방식보다 section 단위 저장이 낫다.

예:

- summary 저장
- acceptance criteria 저장
- review response 제출
- test evidence 추가
- 구현 요청 생성

이렇게 하면 optimistic concurrency 충돌을 줄일 수 있다.

## 2.4 lazy loading

모든 정보를 처음부터 다 렌더링하면 무거워진다.

- 기본 목록은 collapsed summary만 보여준다.
- 펼칠 때 assignment, activity, tests, evidence, review request를 로드한다.
- activity는 전체 event stream이 아니라 최근 window + summary를 먼저 보여준다.

# 3. 화면 구조

## 3.1 기본 레이아웃

통합 workspace는 아래 3영역으로 보는 것이 좋다.

1. 상단 global controls
2. 중앙 spec stream
3. 우측 또는 하단 context panel

### 상단 global controls

- 검색
- 필터
- 상태별 카운트
- 검토 요청 수
- 보류/실패 수
- 칸반 보기 전환
- review-only 보기 전환

### 중앙 spec stream

- spec 한 줄 카드 목록
- 각 카드는 펼치면 notebook-style section 집합이 나온다.

### context panel

- 현재 선택된 spec의 quick action
- 최근 review request
- 최근 activity
- dependency graph mini view

# 4. spec stream 구조

## 4.1 collapsed row

기본적으로 각 spec은 한 줄 row로 보인다.

최소 표시 정보:

- title
- state
- processingStatus
- risk level
- open review request count
- assignment 상태 요약
- 최근 activity 시간
- dependency badge

한 줄에서는 "무슨 spec이고 지금 막혀 있는가"만 빠르게 판단할 수 있으면 된다.

## 4.2 expanded notebook view

펼치면 같은 spec 아래에 section이 순서대로 열린다.

권장 section 순서는 아래와 같다.

1. Summary
2. Goal & Context
3. Acceptance Criteria
4. Dependencies
5. Assignments
6. Review Requests
7. Tests & Evidence
8. Activity Timeline
9. Actions

이 순서는 아래 이유로 적절하다.

- 먼저 이해한다.
- 그 다음 검증 기준을 본다.
- 그 다음 현재 실행 상태를 본다.
- 마지막에 action을 수행한다.

# 5. section 정보 구조

## 5.1 Summary

목적:

- 이 spec이 무엇인지 10초 안에 파악하게 한다.

필수 정보:

- spec title
- type
- state
- processingStatus
- risk level
- project id
- current version
- last updated at

편집 가능 항목:

- title
- type
- risk level

금지:

- state 직접 수정
- processingStatus 직접 수정

## 5.2 Goal & Context

필수 정보:

- problem
- goal
- scope note
- planner note
- architect note 요약

이 섹션은 "왜 이 spec이 존재하는가"를 설명한다.

## 5.3 Acceptance Criteria

필수 정보:

- AC 목록
- 각 AC의 testable 여부
- 관련 test ids
- 현재 충족 여부 요약

편집 가능 항목:

- AC text
- AC notes

중요 규칙:

- AC 수정은 section 저장으로 처리한다.
- 저장 후 필요하면 AC precheck를 다시 요청할 수 있어야 한다.

## 5.4 Dependencies

필수 정보:

- dependsOn
- blocks
- upstream 상태 요약
- 현재 보류 사유

추가 표시:

- downstream impact count
- cascade risk badge

이 섹션은 칸반의 병목 정보 일부를 대체한다.

## 5.5 Assignments

필수 정보:

- active assignment
- recent assignments
- retry count
- timeout 상태
- worktree 정보

표시 방식:

- 현재 실행 중 assignment는 상단 pinned card
- 과거 assignment는 접을 수 있는 이력

이 섹션은 "누가 지금 이 spec을 잡고 있는가"를 보여준다.

## 5.6 Review Requests

필수 정보:

- open review requests
- deadline
- 질문
- 대안 옵션
- 최근 응답
- 재요청 횟수

행동:

- 대안 선택 승인
- 반려
- 부분 수정 후 승인

이 섹션은 기존 "검토 요청함"의 상세 뷰를 spec 내부에 흡수한 것이다.

## 5.7 Tests & Evidence

필수 정보:

- tests 목록
- AC 연결 관계
- 최근 결과
- evidence 링크
- user test 결과

표시 방식:

- AC별로 그룹화
- 실패 테스트 우선 정렬

이 섹션은 검증 가능성을 가장 직접적으로 보여준다.

## 5.8 Activity Timeline

필수 정보:

- 최근 상태 전이
- assignment 시작/종료
- review request 생성/응답
- dependency cascade
- timeout recovery

표시 방식:

- 기본은 최근 20개 event
- 더보기 시 과거 로그 로드
- event type별 아이콘 또는 색 구분

중요:

- agent 입력에는 전체 log를 넣지 않는다.
- UI도 전체 event stream을 한 번에 렌더링하지 않는다.

## 5.9 Actions

이 섹션은 직접 상태를 바꾸는 버튼 모음이 아니라, event proposal을 생성하는 action tray다.

예:

- 구현 요청
- 재검토 요청
- review response 제출
- rollback 요청
- 완료 요청
- evidence 추가

각 action은 아래를 가져야 한다.

- 수행 가능 조건
- 생성되는 event 또는 command
- 예상 side effect

# 6. projection 모드

통합 workspace 하나로도 여러 목적을 달성하려면 같은 데이터를 다른 projection으로 보여줘야 한다.

## 6.1 document mode

- spec stream 중심
- 문서 읽기와 편집에 최적화

## 6.2 kanban mode

- 같은 spec을 state lane 기준으로 재배치
- 카드 클릭 시 같은 expanded notebook view를 연다.

즉 칸반은 별도 데이터 구조가 아니라 동일 spec stream의 정렬 방식이다.

## 6.3 review-only mode

- open review request가 있는 spec만 필터링
- review request section을 자동으로 펼친다.

즉 검토 요청함도 별도 제품이 아니라 통합 뷰의 필터 projection이다.

## 6.4 failure / hold mode

- `processingStatus=실패 | 보류` 위주로 필터링
- 병목 spec을 빠르게 찾는 운영 모드

# 7. interaction 규칙

## 7.1 한 줄에서 가능한 일

- 펼치기/접기
- quick approve 같은 제한된 action
- assignee 상태 확인
- review request 여부 확인

## 7.2 펼친 상태에서 가능한 일

- section 편집
- review request 응답
- test evidence 확인
- action 실행

## 7.3 충돌 처리

- section 저장 시 baseVersion 검증 필요
- 버전 충돌 시 자동 merge하지 말고 충돌 배너 표시
- 사용자는 최신 내용 rebase 후 다시 저장해야 한다.

# 8. UX 장점

이 구조의 장점은 아래와 같다.

- 목록과 상세가 분리되지 않는다.
- 문서, 상태, 검토, 테스트가 한 맥락 안에 있다.
- 칸반과 검토 요청함이 중복 화면이 아니라 같은 데이터의 다른 뷰가 된다.
- 사용자에게 "왜 이 spec이 막혔는지"를 설명하기 쉽다.

# 9. 리스크와 방어선

## 9.1 화면이 너무 무거워질 위험

해결:

- collapsed first
- lazy load
- activity recent window
- evidence는 필요 시만 로드

## 9.2 문서 편집과 운영 action이 섞여 혼란스러울 위험

해결:

- 편집 section과 action section을 시각적으로 분리
- 저장 action과 실행 action을 다르게 표현

## 9.3 state 직접 수정 유혹

해결:

- state/status는 badge로만 보여주고 직접 편집은 금지
- 모든 운영 버튼은 event proposal 형태로만 제공

# 10. 내 의견

이 방향은 좋다. 특히 Flow 같은 시스템에서는 "문서 뷰어", "칸반", "검토 요청함", "테스트 화면"이 따로 놀기 시작하면 사용자가 현재 맥락을 계속 잃는다.

가장 좋은 구현 방향은 아래다.

- 기본은 spec stream 한 줄 목록
- 펼치면 notebook-style 상세
- 칸반과 review inbox는 별도 앱이 아니라 projection mode
- action은 state mutation이 아니라 event proposal

즉 이건 Jupyter를 그대로 복제하는 게 아니라, Flow 운영 모델에 맞는 notebook-like spec workspace를 만드는 일이다.

# 11. 후속 작업

이 문서를 바탕으로 다음이 자연스럽다.

1. collapsed row와 expanded notebook view 와이어프레임 정의
2. 각 section의 API 계약 정의
3. 어떤 action이 어떤 event proposal로 연결되는지 action matrix 작성
4. webservice MVP에서 어떤 projection부터 먼저 구현할지 우선순위 결정