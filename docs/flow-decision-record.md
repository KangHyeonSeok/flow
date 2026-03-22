# Flow Decision Record 운영 문서

이 문서는 Flow에서 사용자 결정이 필요한 문제를 별도의 `Decision Record` 타입으로 다루는 이유와 운영 방식을 정리한다.

핵심 목적은 아래 두 가지다.

- 스펙 문서를 미해결 의사결정 메모로 오염시키지 않는다.
- 사용자가 runner의 내부 상태를 따라가지 않고도 `무엇을 결정해야 하는가`에만 집중하게 한다.

Decision Record는 Change Record, Spec, Review Request와 나란한 별도 타입이다. 즉 `결정이 필요하다`는 사실 자체를 독립적으로 저장하고 추적한다.

## 1. 왜 별도 타입이 필요한가

구현 중에는 아래 같은 상황이 반복된다.

- acceptance criteria 해석이 둘 이상 가능하다.
- UX, 정책, 비용, 운영 리스크 때문에 사람 선택이 필요하다.
- agent가 임의로 진행하면 안 되는 갈림길이 있다.
- review request로 표현하기엔 단순 승인/반려가 아니라 설계 선택이 필요하다.

이 상황을 spec 본문 안의 TODO나 open question으로만 남기면 문제가 생긴다.

- 사용자는 어떤 문제를 결정해야 하는지 한눈에 보기 어렵다.
- 나중에 왜 특정 spec이 그런 방향으로 정의되었는지 근거가 흐려진다.
- runner는 내부적으로 block되지만, 사용자는 그 이유보다 상태 이름만 보게 된다.
- 미해결 선택지와 이미 확정된 실행 계약이 같은 문서 안에 섞인다.

따라서 Flow는 `결정이 필요한 문제`를 별도 문서 타입으로 승격하는 편이 맞다.

## 2. Decision Record의 역할

Decision Record는 아래 역할을 담당한다.

- 현재 어떤 문제 때문에 진행이 멈추었는지 설명한다.
- 사용자가 정확히 무엇을 선택해야 하는지 명시한다.
- 조사된 사실, 제약, 대안을 한 문서에 정리한다.
- 최종 선택과 근거를 이력으로 남긴다.
- 선택 결과가 어떤 spec/change/code/test에 반영되어야 하는지 연결한다.

한 줄로 줄이면 아래와 같다.

`Decision Record는 실행 전에 필요한 사람 결정을 구조화하고, 결정 결과를 스펙과 구현에 반영하기 위한 운영 계약이다.`

## 3. 다른 타입과의 구분

Decision Record는 기존 타입과 역할이 다르다.

### 3.1 Change Record와의 차이

- Change Record: 왜 바뀌었는가를 기록하는 외부 변화 입력
- Decision Record: 무엇을 선택해야 하는가를 기록하는 미해결 의사결정 입력

모든 change가 decision은 아니고, 모든 decision이 change도 아니다.

예:

- 고객 정책이 바뀌었다: Change Record
- 정책을 반영하는 구현 방식을 A/B 중 선택해야 한다: Decision Record

### 3.2 Spec과의 차이

- Spec: 구현과 검증의 실행 계약
- Decision Record: 실행 계약을 확정하기 전에 풀어야 하는 선택 문제

spec은 닫힌 계약이어야 한다. Decision Record는 아직 닫히지 않은 질문과 대안을 담는다.

### 3.3 Review Request와의 차이

- Review Request: 현재 산출물에 대한 검토/응답 루프
- Decision Record: 아직 정해지지 않은 방향 자체에 대한 선택 요청

Review Request는 주로 `이 결과가 맞는가`를 묻는다.
Decision Record는 `어느 방향을 택해야 하는가`를 묻는다.

## 4. 언제 Decision Record를 만들어야 하는가

아래 조건 중 하나 이상이면 Decision Record 생성이 적절하다.

- acceptance criteria를 둘 이상의 상충하는 방식으로 해석할 수 있다.
- 비용, 일정, 운영 리스크가 커서 사람 승인 없이 진행하면 안 된다.
- 정책, UX, 권한, 데이터 모델, 외부 계약에 영향이 있다.
- agent가 임의 선택을 하면 나중에 근거 설명이 어려워진다.
- review request의 단순 승인/반려로는 답할 수 없는 구조적 선택 문제다.

반대로 아래는 Decision Record 없이 처리하는 편이 낫다.

- 순수 구현 세부사항이고 acceptance criteria가 이미 명확하다.
- 테스트 실패나 lint 오류처럼 기계적으로 해결 가능한 문제다.
- 단순 사실 확인만 필요하고 실질적인 대안 선택이 없다.

## 5. 문서에 들어가야 할 내용

Decision Record는 최소한 아래를 포함해야 한다.

1. 문제 상황
2. 왜 지금 결정이 필요한지
3. 사용자가 결정해야 하는 정확한 질문
4. 알려진 사실과 제약
5. 조사한 내용과 참고 자료
6. 현실적인 대안 목록
7. 각 대안의 장단점
8. 추천안과 추천 이유
9. 선택 시 영향을 받는 spec/change/code/test
10. 최종 선택과 근거

즉 사용자는 이 문서를 읽고 아래 셋 중 하나를 할 수 있어야 한다.

- 제시된 대안 중 하나를 선택한다.
- 정보가 부족하다고 판단해 추가 조사를 요청한다.
- 제시되지 않은 다른 방법을 제안한다.

## 6. 권장 문서 템플릿

아래 템플릿을 권장한다.

## Document Summary

- 이 결정 문서가 다루는 문제를 한 단락으로 요약한다.

## Problem

- 현재 무엇이 불명확하거나 충돌하는가.

## Why Decision Is Needed Now

- 왜 지금 이 문제를 미룰 수 없는가.

## Required User Decision

- 사용자가 무엇을 선택해야 하는지 한두 문장으로 명확히 적는다.

## Known Facts

- 이미 확인된 사실만 적는다.

## Constraints

- 정책, 일정, 기술, 운영 제약을 적는다.

## Research / Investigation

- 조사한 내용, 비교한 자료, 실험 결과를 정리한다.

## Options

- 대안 2~4개를 정리한다.
- 각 대안마다 장점, 단점, 리스크, 비용을 적는다.

## Recommended Option

- 추천안을 적고 이유를 설명한다.

## Impact

- 선택 시 영향을 받는 spec, change, code, tests, evidence를 적는다.

## Missing Information

- 아직 부족한 정보가 있다면 적는다.

## Final Decision

- 사용자가 선택한 결과를 남긴다.

## Decision Rationale

- 왜 그렇게 결정했는지 한두 단락으로 남긴다.

## Related Records

- 관련 project, epic, spec, change, review request를 연결한다.

## 7. 권장 JSON 스키마

초기 저장 계약은 아래 정도가 적절하다.

```json
{
  "decisionId": "DR-001",
  "projectId": "flow",
  "epicId": "EPIC-B",
  "title": "사용자 결정 문서를 review request와 분리할지 여부",
  "summary": "runner가 내부 상태를 노출하지 않고도 사용자에게 선택지를 제시할 별도 타입이 필요하다.",
  "problem": "review request만으로는 설계 선택 문제와 산출물 검토 문제를 구분하기 어렵다.",
  "whyNow": "phase 4 review loop와 change-driven 모델을 확장하기 전에 결정 흐름을 분리해야 한다.",
  "requiredDecision": "Decision Record를 Change Record, Spec, Review Request와 별도 타입으로 둘 것인가?",
  "status": "open",
  "priority": "high",
  "decisionType": "product",
  "blocking": {
    "blocksRunner": true,
    "blockReason": "spec-definition-ambiguous"
  },
  "knownFacts": [
    "review request는 검토 응답 루프를 담당한다.",
    "change record는 외부 변화의 원인을 기록한다.",
    "spec은 execution contract여야 한다."
  ],
  "constraints": [
    "사용자는 runner state를 직접 따라가지 않아야 한다."
  ],
  "researchSummary": [
    "별도 Decision Record가 있어야 사용자 decision inbox를 만들기 쉽다."
  ],
  "options": [
    {
      "id": "opt-a",
      "label": "spec 내부 open question으로 유지",
      "description": "새 타입을 만들지 않고 spec 본문 안에 미결 질문을 둔다.",
      "pros": ["구현이 가장 단순하다."],
      "cons": ["실행 계약과 미해결 선택지가 섞인다."],
      "risks": ["장기 운영 시 근거 추적이 약해진다."]
    },
    {
      "id": "opt-b",
      "label": "Change Record 하위 decision으로 포함",
      "description": "변경 문서 안에 선택 문제를 포함한다.",
      "pros": ["외부 변화와 결정이 같이 보인다."],
      "cons": ["모든 결정이 change로 오해되기 쉽다."],
      "risks": ["개념 경계가 흐려진다."]
    },
    {
      "id": "opt-c",
      "label": "별도 Decision Record 타입 도입",
      "description": "결정 문제를 독립 객체로 저장하고 spec/change와 링크한다.",
      "pros": ["가장 명확한 책임 분리가 가능하다."],
      "cons": ["새 저장 계약과 UI가 필요하다."],
      "risks": ["생성 기준이 약하면 남용될 수 있다."]
    }
  ],
  "recommendedOptionId": "opt-c",
  "finalDecision": null,
  "decisionRationale": null,
  "relatedSpecIds": ["F-001"],
  "relatedChangeIds": ["CH-001"],
  "createdBy": "runner",
  "createdAt": "2026-03-22T09:00:00Z",
  "resolvedBy": null,
  "resolvedAt": null,
  "version": 1
}
```

## 8. 상태 모델

권장 상태는 아래 정도면 충분하다.

- `open`: 아직 사용자의 선택이 필요하다.
- `investigating`: 정보가 부족해 추가 조사 중이다.
- `answered`: 사용자가 선택 또는 대안 제안을 남겼다.
- `applied`: 선택 결과가 관련 spec/change에 반영되었다.
- `superseded`: 다른 decision으로 대체되었다.
- `cancelled`: 더 이상 유효하지 않다.

핵심 규칙:

- `answered`로 끝나면 안 된다. spec 또는 change에 반영된 뒤 `applied`까지 가야 한다.
- 같은 문제를 새 decision으로 다시 열면 기존 문서는 `superseded`로 남긴다.
- `cancelled`도 이력으로 보존한다.

## 9. Runner와의 연결 방식

중요한 원칙은 아래다.

- 사용자는 runner의 phase/state를 직접 따라갈 필요가 없다.
- runner는 내부적으로 멈추더라도, 사용자에게는 `결정이 필요한 문제 목록`만 보여야 한다.

권장 흐름은 아래와 같다.

1. runner 또는 agent가 결정 필요를 감지한다.
2. core가 Decision Record를 생성한다.
3. 관련 spec에는 `pendingDecisionIds` 또는 동등한 block metadata를 기록한다.
4. runner는 해당 spec을 dispatch 대상에서 제외한다.
5. 사용자 UI는 spec 상태 대신 open decision inbox를 보여준다.
6. 사용자가 선택하면 Decision Record가 `answered`가 된다.
7. core가 선택 결과를 spec/change에 반영한다.
8. 반영이 끝나면 Decision Record를 `applied`로 전환하고 runner가 다시 진행한다.

즉 내부 모델은 `spec is blocked by decision`이지만, 사용자 경험은 `this problem needs your decision`이어야 한다.

## 10. Relevant Context Retrieval 전략

Decision Record가 늘어나면 agent가 어떤 Change Record와 Decision Record까지 읽어야 하는지가 중요해진다. 모든 관련 문서를 무조건 다 읽으면 context window를 빠르게 소모하고, 반대로 너무 적게 읽으면 잘못된 판단을 하게 된다.

따라서 Flow는 `Relevant Context Retrieval`을 별도 전략으로 가져가는 편이 맞다.

핵심 원칙은 아래와 같다.

- agent는 항상 전체 이력을 읽지 않는다.
- 현재 spec 실행과 직접 연결된 문맥부터 작은 집합으로 읽는다.
- 필요할 때만 상위 범위로 점진적으로 확장한다.
- relevance 판단 근거도 activity나 summary에 남겨 재현 가능하게 한다.

### 10.1 기본 검색 범위

spec 이행 또는 decision 대응 시 기본 읽기 범위는 아래 순서가 적절하다.

1. 현재 Spec
2. 현재 Spec에 직접 연결된 open Decision Records
3. 현재 Spec에 직접 연결된 open 또는 recently applied Change Records
4. 현재 Spec의 parent Epic 문서
5. 현재 Spec과 직접 dependency를 가지는 upstream/downstream spec 중 active 또는 blocked 상태의 항목

즉 기본 원칙은 `현재 spec 중심의 1-hop 문맥`이다.

### 10.2 우선순위 규칙

관련 문서를 읽을 때는 아래 우선순위를 권장한다.

1. `open` 상태 Decision Record
2. `open` 또는 `in-progress` 상태 Change Record
3. 현재 spec의 acceptance criteria와 tests에 직접 연결된 records
4. 현재 spec의 parent epic에서 명시적으로 참조한 records
5. 최근 N회 activity에서 반복 언급된 records
6. 이미 `applied` 되었지만 현재 block reason과 직접 연결된 최근 records

즉 relevance는 단순 링크 존재 여부가 아니라 `현재 실행과의 거리`, `상태`, `최근성`, `block 이유`를 함께 봐야 한다.

### 10.3 읽지 말아야 할 문맥

아래는 기본적으로 제외하는 편이 낫다.

- 이미 `superseded` 또는 `cancelled` 된 Decision Record 전체 본문
- 현재 spec과 2-hop 이상 떨어진 오래된 Change Record 상세 본문
- 단순 history 보관용 archived spec의 전체 activity log
- 현재 phase와 직접 관련 없는 closed review requests 전체 본문

다만 아래 경우에는 예외적으로 확장할 수 있다.

- current decision이 과거 superseded decision의 실패 이유를 재검토해야 할 때
- 반복적으로 같은 block가 발생해 historical rationale이 필요한 때
- 상위 epic 또는 project policy 변경이 현재 선택지에 직접 제약을 줄 때

### 10.4 점진적 확장 방식

Context Retrieval은 한 번에 넓게 읽기보다 아래처럼 단계적으로 확장하는 편이 적절하다.

1. 현재 spec + linked open decision/change만 읽는다.
2. 정보가 부족하면 parent epic summary와 direct dependency spec summaries를 읽는다.
3. 그래도 부족하면 related applied records의 summary만 읽는다.
4. 마지막에만 archived/full history를 펼친다.

즉 retrieval은 `full scan`이 아니라 `small set -> summary expansion -> selective deep read` 순서여야 한다.

### 10.5 요약 캐시 권장

context window 절약을 위해 각 Change Record, Decision Record에는 아래 요약 필드를 두는 편이 좋다.

- one-line summary
- current status summary
- current recommendation
- affectedSpecIds / affectedEpicIds
- lastMeaningfulUpdateAt

agent는 전체 본문보다 먼저 이 요약 필드를 읽고, 필요한 경우에만 상세 섹션으로 내려간다.

### 10.6 retrieval 결과물

agent는 문맥을 읽은 뒤 아래 정도의 내부 결과물을 남기는 편이 좋다.

- 어떤 records를 읽었는가
- 왜 그 records를 relevant하다고 판단했는가
- 어떤 records는 의도적으로 제외했는가
- 아직 부족한 정보가 무엇인가

이 결과는 activity log 또는 decision preparation summary에 남기면 다음 루프에서 같은 검색을 반복하는 비용을 줄일 수 있다.

## 11. Human-in-the-loop 최소화 전략

Decision Record의 목적은 사람을 더 자주 호출하는 것이 아니라, 불가피한 사람 결정을 가장 작은 단위로 압축하는 데 있다.

따라서 Decision Record가 열릴 때 agent는 단순히 `A 아니면 B를 골라 달라`고 던지기보다, 먼저 `상황 분석 및 대안별 리스크/이득 보고서`를 초안으로 작성하는 편이 맞다.

### 11.1 왜 필요한가

사람이 단순 선택지만 받으면 아래 문제가 생긴다.

- 왜 이 선택이 필요한지 이해하지 못한 채 답하게 된다.
- 대안의 리스크와 장점이 비교되지 않아 재질문이 늘어난다.
- 결국 추가 설명을 다시 요청하게 되어 human-in-the-loop가 늘어난다.

반대로 agent가 먼저 분석 보고서를 초안으로 만들면 아래 이점이 있다.

- 사용자는 문제 이해에 드는 시간을 줄일 수 있다.
- 대안별 trade-off를 비교한 뒤 더 빠르게 선택할 수 있다.
- 정보가 부족한 경우에도 무엇이 부족한지 바로 지적할 수 있다.

### 11.2 agent가 먼저 작성해야 할 것

Decision Record 생성 시 agent는 최소한 아래를 먼저 채워야 한다.

- 상황 요약
- 지금 결정이 필요한 이유
- 현재까지 조사한 사실
- 제약 조건
- 현실적인 대안 목록
- 각 대안의 기대 이득
- 각 대안의 리스크와 실패 모드
- 추천안과 추천 이유
- 추가 정보가 있다면 더 좋아질 항목

즉 사용자에게 가는 첫 문서는 빈 질문지가 아니라 `초안이 있는 의사결정 보고서`여야 한다.

### 11.3 권장 보고서 구조

Decision Record 안에서 아래 구조를 권장한다.

1. Situation Analysis
2. Why Blocked Now
3. Relevant Facts
4. Options Comparison
5. Risk / Benefit Matrix
6. Recommended Path
7. What The User Needs To Decide
8. What Additional Information Would Change The Recommendation

### 11.4 질문은 최소화해야 한다

Human-in-the-loop 최소화를 위해 agent 질문은 아래 원칙을 따라야 한다.

- 가능하면 하나의 핵심 질문으로 축약한다.
- 이미 조사 가능한 것은 사용자에게 다시 묻지 않는다.
- 대안 수는 2~4개로 제한한다.
- 각 대안은 실제로 선택 가능한 수준까지 구체화한다.
- recommendation이 있다면 명시적으로 제시한다.

나쁜 질문 예:

- `어떻게 할까요?`
- `이 문제를 어떻게 생각하세요?`

좋은 질문 예:

- `접근성 기준과 기존 디자인 일관성을 같이 만족시키려면 안 A가 기본 추천입니다. 다만 릴리즈 속도를 우선하면 안 B가 더 단순합니다. 어느 목표를 우선할지 선택해 주세요.`

### 11.5 정보 부족 응답도 구조화한다

사용자가 `정보가 부족하다`고 답할 수 있어야 한다. 이 경우 agent는 단순 대기하지 않고 아래를 다시 수행하는 편이 좋다.

1. 부족한 정보 항목을 명시한다.
2. 어떤 추가 조사로 그 정보를 채울 수 있는지 적는다.
3. 조사가 끝난 뒤 Decision Record 초안을 업데이트한다.
4. 필요하면 질문을 더 작은 단위로 재구성한다.

즉 `investigating` 상태는 빈 대기가 아니라 `추가 조사 후 더 나은 decision package를 만드는 상태`여야 한다.

### 11.6 사람이 해야 하는 일의 최소 단위

궁극적으로 사용자는 아래 세 가지 중 하나만 하면 되는 상태가 이상적이다.

- 추천안 승인
- 다른 대안 선택
- 추가 정보 요청

이보다 넓은 서술형 결정을 계속 요구하면 Decision Record의 목적이 약해진다.

### 11.7 agent 남용 방지

agent가 Human-in-the-loop를 줄인다는 명분으로 과도하게 강한 recommendation을 밀어붙이면 안 된다. 따라서 아래 안전장치가 필요하다.

- recommendation에는 근거와 가정을 함께 적는다.
- high-risk decision에서는 불확실성을 명시한다.
- 사용자 정책, 비용, 권한, 법적 제약은 agent가 임의 확정하지 않는다.
- 추천안이 있어도 대안의 핵심 trade-off는 숨기지 않는다.

## 12. 상태 기계에 넣는 방법

Decision Record 도입이 반드시 새로운 top-level spec state를 의미하지는 않는다.

권장 방식:

- spec의 현재 phase는 유지한다.
- dispatch exclusion reason에 pending decision을 추가한다.
- spec metadata에 `pendingDecisionIds`를 둔다.
- UI projection에서 이를 `Pending Decisions`로만 노출한다.

이 방식을 권장하는 이유는 아래와 같다.

- 상태 기계를 불필요하게 복잡하게 만들지 않는다.
- decision 대기는 phase 자체보다 block reason에 가깝다.
- review, implementation, test-validation 어느 단계에서든 동일하게 적용할 수 있다.

## 13. Web / UX 관점

사용자는 특정 spec의 `Review/UserReview`나 `Implementation/Pending` 같은 내부 상태를 순회해서 문제를 찾으면 안 된다.

대신 아래 경험이 적절하다.

- 홈 또는 프로젝트 화면에 `Open Decisions` 영역이 있다.
- 각 항목은 상태명이 아니라 문제 요약으로 보인다.
- 문서를 열면 문제, 사실, 대안, 추천안이 구조화되어 있다.
- 사용자는 선택, 추가 조사 요청, 대안 제안을 남긴다.
- 선택이 적용되면 관련 spec 링크와 반영 결과를 확인할 수 있다.

즉 Decision Record는 사용자에게 보여 주는 문제 단위이자, 내부적으로는 runner 재개를 위한 근거 단위다.

## 14. 운영 규칙

- 하나의 Decision Record는 하나의 핵심 질문만 다룬다.
- agent는 쉽게 decision을 생성해 책임을 회피하면 안 된다.
- decision 생성 시 왜 agent가 자체 판단할 수 없는지 근거를 남겨야 한다.
- 사용자가 선택하지 않은 대안도 이력으로 남긴다.
- `정보 부족` 응답은 정상 응답으로 허용한다.
- 선택 후 spec 반영은 수동 메모가 아니라 구조화된 반영 단계로 남겨야 한다.
- retrieval은 작은 관련 집합부터 시작하고, 필요할 때만 확장한다.
- Decision Record 초안은 가능하면 agent의 분석 보고서가 먼저 채운다.

## 15. Change / Decision / Review 경계 정리

| 타입 | 핵심 질문 | 주된 원인 | 결과 |
| --- | --- | --- | --- |
| Change Record | 왜 바뀌었는가 | 외부 변화 | impact analysis, spec/code/test 갱신 |
| Decision Record | 무엇을 선택해야 하는가 | 미해결 선택 문제 | 사용자 선택, spec/change 반영 |
| Review Request | 이 결과가 맞는가 | 검토 필요 | approve/reject/rework/user response |

이 표를 유지해야 세 타입이 서로를 잠식하지 않는다.

## 16. 구현 우선순위

### Now

- [x] Decision Record를 Change Record, Spec, Review Request와 구분되는 별도 타입으로 문서화한다.
  왜 필요한가: 사용자 결정 문제를 독립 운영 객체로 다루기 위한 기준이 필요하다.
  변경 대상: `docs/flow-decision-record.md`
  완료 기준: 역할, 상태, 스키마, runner 연동 방식이 문서에 정리되어 있다.

- [x] 상위 문서에서 Decision Record 문서를 찾을 수 있게 연결한다.
  왜 필요한가: 프로젝트 철학과 저장 계약 문맥에서 이 타입을 바로 발견할 수 있어야 한다.
  변경 대상: `README.md`, `docs/flow-project-document.md`, `docs/flow-schema.md`
  완료 기준: 관련 문서 목록 또는 저장 계약 설명에 Decision Record가 반영되어 있다.

### Next

- [ ] Decision Record 최소 JSON 스키마와 저장 위치를 `flow-core` 계약으로 구체화한다.
  왜 필요한가: 문서 개념을 실제 저장 모델로 내리기 위해 필요하다.
  변경 대상: `docs/flow-schema.md` 또는 별도 schema 문서
  완료 기준: 필수 필드, 상태, 적용 규칙이 확정된다.

- [ ] Relevant Context Retrieval 규칙과 summary cache 필드를 runtime 계약으로 내린다.
  왜 필요한가: agent가 어떤 records를 읽어야 하는지 일관되게 결정하고 context window를 통제하기 위해 필요하다.
  변경 대상: runner/core 설계 문서
  완료 기준: 기본 검색 범위, 확장 순서, 제외 규칙, 요약 필드가 문서화된다.

- [ ] decision create/apply/supersede 흐름의 CLI/API 계약을 설계한다.
  왜 필요한가: 사용자의 선택이 문서에 남고 runtime에 반영되는 경로가 필요하다.
  변경 대상: core/CLI/API 설계 문서
  완료 기준: create, answer, apply, supersede 동작이 문서화된다.

- [ ] Decision Record 초안용 `상황 분석 및 대안별 리스크/이득 보고서` 생성 규칙을 agent contract에 반영한다.
  왜 필요한가: human-in-the-loop를 줄이려면 질문 전에 분석 패키지를 먼저 만들어야 한다.
  변경 대상: agent contract 또는 decision flow 설계 문서
  완료 기준: 보고서 필수 섹션, recommendation 규칙, 정보 부족 처리 규칙이 합의된다.

### Later

- [ ] flow-web에 project 단위 `Open Decisions` projection과 decision detail view를 추가한다.
  왜 필요한가: 사용자가 runner 내부 상태가 아니라 문제 단위로 확인하고 응답할 수 있어야 한다.
  변경 대상: web read model과 UI 문서
  완료 기준: 사용자가 open decision 목록에서 문서를 열고 선택을 제출할 수 있다.

- [ ] project/epic/spec 화면에 `relevant context` 요약과 decision preparation report를 노출한다.
  왜 필요한가: 사용자가 대안 비교와 근거를 별도 조사 없이 바로 읽을 수 있어야 한다.
  변경 대상: web read model과 UI 문서
  완료 기준: open decision detail에서 context summary, options comparison, recommendation이 보인다.

## 17. 결론

Flow에서 사용자 결정은 spec의 부속 메모가 아니라, 별도의 운영 객체로 다루는 편이 맞다.

그 이유는 아래와 같다.

- spec은 execution contract로 유지해야 한다.
- change는 원인 기록으로 유지해야 한다.
- review request는 검토 루프로 유지해야 한다.
- decision은 미해결 선택 문제를 구조화하고, 사용자 응답과 runtime 반영을 연결해야 한다.
- relevant context retrieval은 agent가 읽을 문맥을 작게 유지하면서도 판단 품질을 보장해야 한다.
- human-in-the-loop 최소화는 빈 질문을 줄이고 분석 보고서가 먼저 가도록 만드는 방향이어야 한다.

따라서 Flow는 `Decision Record`를 별도 타입으로 도입하고, 이를 통해 사용자에게는 문제 중심 경험을 제공하고, 내부적으로는 runner block/resume과 spec 반영을 제어하는 방향으로 가는 것이 적절하다.