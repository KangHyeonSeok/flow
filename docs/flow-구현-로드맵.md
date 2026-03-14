# Flow 구현 로드맵

이 문서는 Flow를 새 운영 모델로 재구성할 때의 권장 개발 순서를 정리한 문서다. 핵심 관점은 단순 기능 구현 순서가 아니라, 어디까지를 먼저 고정해야 이후 단계의 재작업 비용이 줄어드는가에 있다.

결론부터 말하면 구현 순서는 `상태 규칙 -> core 저장/도구 -> runner orchestration -> 검토 요청 루프 -> 실제 agent -> webservice -> Docker -> Slack`이 가장 안전하다. 이 순서를 벗어나면 prompt, UI, 외부 연동을 먼저 만들고 나중에 상태 규칙을 뜯어고치는 상황이 반복될 가능성이 높다.

# 1. 구현 원칙

## 1.1 먼저 고정해야 하는 것

- 상태 전이 규칙
- 처리 상태 규칙
- review request lifecycle
- assignment/lock/timeout 규칙
- spec JSON과 activity log 저장 계약

이 다섯 가지는 뒤 단계의 공용 바닥이다. runner, agent, webservice, Slack은 모두 이 계약 위에 올라간다.

## 1.2 늦게 붙여야 하는 것

- 실제 LLM prompt 최적화
- webservice의 풍부한 UI
- Slack 대화 UX
- Docker 기반 운영 편의 기능

이 요소들은 중요하지만, core contract가 흔들리는 동안 먼저 붙이면 재작업 비용만 커진다.

## 1.3 테스트 전략 원칙

- 상태 규칙은 unit test로 먼저 고정한다.
- runner 루프는 fixture 기반 integration test로 검증한다.
- review request는 UI보다 먼저 도메인 테스트로 고정한다.
- agent는 더미 adapter로 orchestration을 먼저 확인한 뒤 실제 prompt 기반 adapter로 교체한다.
- 로그는 사람이 읽는 설명보다 구조화된 event를 우선으로 기록한다.

# 2. 권장 구현 순서

## 2.1 1단계: State Rule 구현과 테스트

이 단계는 전체 시스템의 바닥이다. 여기서 상태 규칙이 흔들리면 이후 단계는 모두 불안정해진다.

구현 범위:

- 상태와 처리 상태 모델 정의
- 상태 전이 규칙표 정의
- dependency failure cascade 규칙 정의
- stale assignment timeout 회수 규칙 정의
- review request 생성 및 재검토 진입 규칙 정의
- Spec Validator와 Spec Manager의 책임 경계 구현

필수 테스트:

- table-driven state transition test
- 허용된 전이 테스트
- 금지된 전이 테스트
- dependency failure cascade test
- retry 3회 초과 시 실패 전환 test
- stale assignment 회수 test
- review request 생성 조건 test

완료 기준:

- 규칙표의 모든 전이에 대응하는 테스트가 있다.
- 상태 전이 로직이 agent 구현과 분리되어 있다.
- runner 없이 rule evaluator만으로 검증할 수 있다.

내 의견:

- 여기서는 rule engine을 최대한 순수 함수처럼 유지하는 편이 좋다.
- 입력은 spec snapshot, assignment snapshot, incoming event 정도로 제한하고, 출력은 mutation proposal과 side effect request 정도로 줄이는 편이 테스트하기 쉽다.
- 이 단계가 끝나기 전에는 prompt tuning에 들어가지 않는 편이 낫다.

## 2.2 2단계: flow core 구현

`flow`는 CLI가 아니라 공용 core여야 한다. runner, webservice, Slack이 모두 같은 저장과 로그 로직을 사용해야 한다.

구현 범위:

- spec JSON load/save
- 빈 필드 제외 로직
- activity log append
- review request load/save
- assignment/lock metadata load/save
- fixture spec 초기화 도구
- 상태 규칙 테스트용 helper

권장 원칙:

- `flow spec show`는 JSON 원본에서 빈 필드만 제거한 결과를 기준으로 둔다.
- 파일 I/O 계층과 도메인 계층을 분리한다.
- activity log는 append-only로 유지한다.
- CLI는 thin wrapper로 두고, 실제 로직은 library에 둔다.

필수 테스트:

- spec load/save round-trip test
- empty field pruning test
- activity log append test
- review request persistence test
- concurrent write conflict 기본 방어 test
- fixture initialization test

완료 기준:

- runner와 webservice가 공통으로 호출할 저장 API가 준비된다.
- fixture spec 세트를 명령 하나로 초기화할 수 있다.

내 의견:

- 이 단계에서 storage contract를 명확히 못 박아야 webservice와 Slack이 별도 저장 포맷을 만들지 않는다.

## 2.3 3단계: runner skeleton과 더미 agent

이 단계의 목적은 agent 품질이 아니라 orchestration 정확성 검증이다.

구현 전에 별도 의사결정이 필요하면 `flow-phase3-runner-decisions.md`를 기준 문서로 삼는 편이 좋다.

구현 범위:

- runner loop
- 상태 기반 dispatch
- rule 호출과 agent 호출 분기
- 구조화된 호출/결과 logging
- 더미 Planner, Architect, Developer, Test Validator, Spec Validator, Spec Manager
- fixture spec 세트를 순회하는 simulation

필수 테스트:

- 상태에 따라 올바른 rule 또는 agent가 호출되는지 검증
- dependency blocked 상태에서 잘못 진행되지 않는지 검증
- stale assignment 회수 뒤 다음 루프에서 재평가되는지 검증
- 여러 루프 실행 뒤 기대 상태에 도달하는지 golden scenario test
- event log sequence가 기대와 일치하는지 golden log test

완료 기준:

- 더미 agent만으로도 전체 루프가 끊기지 않고 돈다.
- 호출 순서와 결과 로그가 예측 가능하다.
- fixture별 기대 상태와 이벤트 시퀀스가 테스트로 고정된다.

내 의견:

- runner에서 가장 중요한 것은 "왜 저 상태가 되었는지 로그만 보고 설명 가능한가"다.
- 그래서 구조화된 event log를 먼저 설계하는 편이 좋다.

## 2.4 4단계: 검토 요청 루프 테스트

검토 요청은 운영 중 가장 쉽게 꼬이는 부분이라 별도 묶음으로 고정하는 편이 좋다.

구현 범위:

- review request 생성 로직
- 사용자 응답 fixture
- 부정확한 응답에 대한 재요청 루프
- 부분 수정 후 승인 재판정 로직
- 3회 초과 비수렴 실패 처리

필수 테스트:

- review request가 생성되어야 하는 경우와 생성되면 안 되는 경우
- 사용자 응답 타입별 후속 처리
- 부정확한 응답 이후 재요청 생성
- 재검토 후 완료 또는 실패 전환 검증

완료 기준:

- review request lifecycle이 runner와 분리된 테스트로 고정된다.
- webservice 없이도 review request 흐름을 재현할 수 있다.

내 의견:

- review request는 UI 기능이 아니라 핵심 도메인 규칙이다.
- 따라서 이 단계가 끝나기 전에는 검토 요청 UI를 크게 만들 필요가 없다.

## 2.5 5단계: 실제 agent MVP와 prompt tuning

이 단계부터 더미 agent를 실제 agent adapter로 교체한다. 다만 복잡한 spec이 아니라 짧고 수렴이 빠른 fixture부터 시작해야 한다.

구현 범위:

- agent 입력 payload 고정
- agent 출력 schema 고정
- timeout/error contract 고정
- 더미 agent를 실제 prompt 기반 agent로 점진 교체
- 반복 실행 기반 prompt tuning

권장 fixture 특성:

- 사용자 의사결정이 거의 필요 없음
- acceptance criteria가 명확함
- 테스트 시간이 짧음
- 변경 파일 수가 적음
- dependency가 단순함

필수 테스트:

- 동일 fixture 반복 실행 시 수렴 경향 확인
- agent 출력 schema validation test
- agent 오류 시 runner recovery test
- evidence/report 첨부 contract 검증

완료 기준:

- 최소 한 세트의 spec fixture에서 Planner 제외 루프가 자동 종료된다.
- prompt 수정이 상태 규칙과 schema contract를 깨지 않는다.

내 의견:

- prompt tuning은 여기서만 한다.
- 그 전 단계에서 tuning을 시작하면 규칙 변경과 prompt 변경이 뒤엉켜 디버깅이 어려워진다.

## 2.6 6단계: webservice

이 시점에는 backend behavior가 이미 테스트로 고정되어 있어야 한다. webservice는 새로운 규칙을 만들면 안 된다.

구현 범위:

- 통합 spec workspace view
- collapsed row + expanded notebook-style 상세 섹션
- document / kanban / review-only / failure-hold projection mode
- section 단위 저장 API와 optimistic concurrency 처리
- review request 응답, evidence 추가, 구현 요청 같은 action tray
- assignment, dependency, activity, tests/evidence lazy loading
- context panel과 dependency mini graph

필수 테스트:

- 주요 API integration test
- projection mode별 동일 데이터 정렬/필터 검증
- section save baseVersion 충돌 처리 test
- review request 응답 API test
- action request가 event proposal로만 변환되는지 검증
- spec edit/save round-trip test
- lazy loading section API test
- runner가 생성한 데이터를 UI가 정상 렌더링하는지 검증

완료 기준:

- 사용자는 한 workspace 안에서 spec 조회, section 편집, review request 응답, evidence 확인, 실행 요청을 모두 처리할 수 있다.
- 칸반과 검토 요청함은 별도 제품이 아니라 같은 spec stream의 projection으로 동작한다.
- UI action은 직접 상태를 바꾸지 않고 항상 event proposal 또는 command request를 통해 core API로 전달된다.
- 대규모 spec 집합에서도 collapsed first + lazy loading으로 초기 로딩 비용이 통제된다.

내 의견:

- webservice는 thin layer로 두는 편이 낫다.
- 상태 변경과 review 판단은 모두 core API를 통하게 해야 한다.
- 이 단계의 중심은 화면 수를 늘리는 것이 아니라, spec 문서/운영/검토/테스트를 하나의 notebook-like workspace로 통합하는 것이다.

## 2.7 7단계: Docker packaging

Docker는 단순 배포 포장이 아니라 운영 경계 고정 단계다.

구현 범위:

- runner container
- webservice container
- shared volume 경로 설계
- 환경 변수 체계
- 로그/아티팩트 마운트 규칙

필수 테스트:

- container 내부 runner loop 실행
- webservice와 runner 동시 실행
- shared home persistence 확인
- restart 이후 상태 복구 확인

완료 기준:

- 로컬 환경과 컨테이너 환경이 같은 spec storage contract를 사용한다.

내 의견:

- webservice MVP 직후 Docker를 붙이는 편이 환경 차이를 일찍 발견하는 데 유리하다.

## 2.8 8단계: Slack integration

Slack은 Planner 대화와 review request 응답을 외부 입력 채널로 확장하는 단계다.

구현 범위:

- Planner와 Slack 대화 연결
- Slack 기반 spec 생성 트리거
- review request 알림 전송
- review request 응답 수집

필수 테스트:

- Slack message -> spec 생성 흐름 검증
- Slack action -> review response 반영 검증
- 중복 응답, 지연 응답, 잘못된 응답 처리 검증

완료 기준:

- Slack은 webservice와 동일한 review request contract를 사용한다.
- Slack 경로가 별도 상태 전이 로직을 만들지 않는다.

내 의견:

- Slack은 별도 시스템이 아니라 추가 입력 채널이다.
- 따라서 Slack에서 직접 상태를 바꾸게 하면 안 되고, 항상 core API로 변환해 흘려야 한다.

# 3. 우선순위 요약

내가 보는 추천 순서는 아래와 같다.

1. State Rule
2. flow core
3. runner skeleton + 더미 agent
4. 검토 요청 루프 테스트
5. 실제 agent MVP + prompt tuning
6. webservice
7. Docker
8. Slack integration

즉 `규칙 -> 저장/도구 -> orchestration -> review loop -> 실제 agent -> UI -> 배포 -> 외부 채널` 순서다.

# 4. 추가 운영 제안

## 4.1 fixture-first 전략

처음부터 아래 고정 fixture를 두는 편이 좋다.

- 정상 완료되는 단순 spec
- review request가 한 번 필요한 spec
- dependency failure로 `보류` 전파가 필요한 spec
- stale assignment 회수가 필요한 spec

이 fixture들은 runner, agent, webservice 테스트에서 공통 기준 샘플이 된다.

## 4.2 golden log 테스트

상태만 검증하지 말고, 특정 fixture 실행 시 어떤 event log sequence가 나와야 하는지도 golden test로 잡는 편이 좋다.

이유:

- runner 회귀를 빨리 잡을 수 있다.
- 왜 잘못됐는지 디버깅하기 쉽다.
- agent adapter 교체 시 side effect를 빨리 볼 수 있다.

## 4.3 agent contract 선고정

prompt를 먼저 다듬기보다 아래 계약을 먼저 고정하는 편이 좋다.

- agent 입력 payload
- agent 출력 schema
- 오류 반환 방식
- timeout 처리 방식
- evidence/report 첨부 방식

이 계약이 없으면 runner와 agent를 동시에 바꾸게 되어 속도가 크게 떨어진다.

## 4.4 webservice와 Slack의 review contract 통합

검토 요청 응답은 채널이 달라도 같은 contract를 쓰는 편이 맞다.

- webservice form response
- Slack action response
- 테스트 fixture response

세 경로 모두 같은 도메인 입력으로 변환되어야 한다.

# 5. 최종 의견

지금 생각한 개발 순서는 충분히 좋다. 다만 실제 성공 여부를 가르는 것은 agent 품질보다 먼저, state rule과 runner loop를 fixture 기반 테스트로 얼마나 단단하게 고정하느냐다.