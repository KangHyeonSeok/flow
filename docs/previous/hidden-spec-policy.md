# Hidden Spec Policy

Flow는 spec을 SoT로 취급한다. 다만 현실적으로 모든 구현이 항상 spec 변경을 선행하지는 않는다. 이 문서는 code에 먼저 반영된 동작, 규칙, 제약을 어떻게 다룰지 정의한다.

## 목적

- code에 숨어 들어간 계약을 방치하지 않는다.
- 지속되는 동작은 결국 spec graph에 수렴시킨다.
- 구현 디테일과 외부 계약을 구분해 spec의 밀도를 유지한다.
- agent와 사람이 동일한 기준으로 hidden spec을 분류하고 처리하게 한다.

## 핵심 원칙

1. spec은 지속되는 계약의 SoT다.
2. code가 spec보다 먼저 바뀔 수는 있지만, 그 상태로 작업을 종료해서는 안 된다.
3. 숨은 스펙은 예외가 아니라 spec debt다.
4. spec에 편입되지 못한 외부 계약 변경은 완료된 구현으로 보지 않는다.
5. 모든 code를 spec으로 올리지는 않는다. 외부 계약과 검증 가능한 조건만 spec으로 관리한다.

## 범위 구분

### 1. 반드시 spec으로 승격해야 하는 것

아래 항목은 product behavior 또는 system contract이므로 hidden spec으로 남겨두면 안 된다.

- 사용자에게 보이는 동작 변화
- CLI 명령, 옵션, 출력 포맷, 종료 코드 변화
- API 입출력 형식, 파일 포맷, 저장 구조의 호환성 규칙
- 상태 전이 규칙, 큐잉 규칙, 자동화 agent의 의사결정 규칙
- 테스트 시나리오가 검증해야 하는 acceptance condition
- 운영상 반드시 지켜야 하는 제약, 예외 처리 규칙, fallback 규칙

이 경우 기존 feature에 condition을 추가하거나, 새 feature/task를 만들어 spec graph에 편입한다.

### 2. spec으로 올리지 않아도 되는 것

아래 항목은 implementation detail일 수 있으므로 기본적으로 code와 test에 남긴다.

- private helper 분리
- 내부 리팩터링
- 성능 최적화의 세부 구현 방식
- 캐시 자료구조 선택
- 로그 문구 미세 조정
- 외부 계약을 바꾸지 않는 내부 배선 변경

단, 구현 디테일이 반복적으로 의사결정에 영향을 주거나 운영 규칙이 되면 spec 또는 운영 정책으로 승격한다.

### 3. 유지할지 먼저 판단해야 하는 것

아래 항목은 hidden spec이라기보다 accidental behavior일 수 있다.

- 우연히 생긴 버그 호환성
- 임시 workaround
- 특정 환경에서만 통하던 예외 처리

이 경우 먼저 유지 여부를 결정한다.

- 유지한다면: spec으로 승격
- 유지하지 않는다면: 제거
- 판단이 끝나지 않았다면: task로 기록 후 decision pending 상태로 추적

## 처리 원칙

hidden spec이 발견되면 다음 순서로 처리한다.

1. 탐지한다.
2. 외부 계약인지 내부 구현인지 분류한다.
3. 지속되는 계약이면 spec graph에 편입한다.
4. codeRefs, tests, evidence를 연결한다.
5. 그 후에만 verified 또는 done으로 닫는다.

즉, code-first는 허용되지만 specless completion은 허용하지 않는다.

## 표준 처리 플로우

### A. 기존 spec에 흡수

기존 feature의 일부 계약이 code에서 먼저 구체화된 경우:

1. 관련 feature를 찾는다.
2. 누락된 acceptance condition을 추가한다.
3. condition에 codeRefs를 연결한다.
4. 테스트 어노테이션과 evidence를 추가한다.
5. spec-validate, spec-check-refs를 통과시킨다.

### B. 새 feature로 승격

기존 spec으로 설명되지 않는 새 계약이 생긴 경우:

1. 새 feature spec을 만든다.
2. 필요 시 parent, dependencies를 연결한다.
3. 최소 1개 이상의 condition을 정의한다.
4. codeRefs와 테스트를 연결한다.

### C. task로 분리

일회성 처리이거나 정책 판단이 필요한 경우:

1. feature가 아니라 task로 기록한다.
2. 왜 feature가 아닌지 description에 남긴다.
3. 후속 feature가 필요하면 dependency로 연결한다.
4. 일회성 작업이 끝나면 done으로 종료한다.

## 리뷰 규칙

다음 상황이면 리뷰어 또는 agent는 hidden spec 후보로 간주한다.

- 외부 계약이 바뀌었는데 spec 변경이 없음
- 테스트가 추가되었는데 spec condition과 연결되지 않음
- 새로운 상태값, 옵션, 출력 포맷, 에러 규칙이 code에만 존재함
- README나 운영 문서가 바뀌었는데 대응 feature/condition이 없음
- agent 동작 규칙이 prompt나 구현에만 들어가 있고 spec graph에 없음

리뷰 코멘트의 기준 문장은 다음으로 통일한다.

"이 변경은 implementation detail인가, 아니면 지속되는 계약인가? 계약이라면 spec에 편입되어야 한다."

## CI / 자동화 권장 규칙

가능하면 다음 검사를 자동화한다.

1. spec-validate --strict
2. spec-check-refs --strict
3. spec condition과 연결되지 않은 테스트 어노테이션 또는 unmapped test 탐지
4. 변경된 파일 중 외부 계약 후보가 있는데 docs/specs 또는 운영 정책 문서 변경이 없는 경우 경고

자동화는 hidden spec을 완전히 판별하지 못한다. 대신 후보를 빠르게 올리고, 최종 판단은 리뷰에서 한다.

## 운영 문서도 spec인가

그렇다. 다만 product spec과 같은 층위로 섞으면 안 된다.

구분은 다음과 같다.

- product spec: 기능, 상태, 조건, 의존성, 검증 기준을 정의한다.
- governance spec: spec을 어떻게 만들고 수정하고 검증할지 정의한다.
- process/task doc: 반복 운영 절차나 일회성 작업 방법을 설명한다.

이 문서는 product spec이 아니라 governance spec이다. 즉, "제품이 어떻게 동작해야 하는가"가 아니라 "SoT를 어떻게 유지해야 하는가"를 정의한다.

## 문서화 원칙

철학, 처리 규칙, 승인 기준, 예외 규칙처럼 agent와 사람이 반복적으로 따라야 하는 내용은 문서화한다.

문서화 대상:

- hidden spec 처리 규칙
- spec 작성 규칙
- 상태 전이 규칙
- review/CI 게이트 규칙
- agent 운영 원칙

문서화하지 않아도 되는 대상:

- 한 번 쓰고 버리는 메모
- 로컬 실험 로그
- 코드만 보면 충분히 분명한 내부 구현 세부사항

## 권장 운영 문장

Flow의 SoT 원칙은 "항상 spec이 code보다 먼저여야 한다"가 아니라, "지속되는 계약은 반드시 최종적으로 spec에 수렴해야 한다"로 해석한다.

## 체크리스트

변경을 마무리하기 전에 다음을 확인한다.

- 이 변경이 외부 계약을 바꾸는가?
- 기존 spec으로 설명 가능한가?
- 아니라면 새 feature 또는 task가 필요한가?
- 조건(condition) 단위로 검증 가능한가?
- codeRefs가 연결되었는가?
- 테스트 또는 evidence가 연결되었는가?
- spec-validate와 spec-check-refs를 통과하는가?