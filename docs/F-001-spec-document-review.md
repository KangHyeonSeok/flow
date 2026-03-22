# F-001 스펙 문서 검토

## 대상

- 스펙 ID: `F-001`
- 제목: `RunnerConfig에 TestGenerationTimeoutSeconds 설정 추가`
- 현재 타입: `task`
- 현재 상태: `testGeneration / inProgress`

검토 기준은 현재 저장된 스펙 원본과 flow의 스펙 모델이 요구하는 문서 필드다.

현재 원본 스펙에는 아래 정보만 존재한다.

- 제목
- 타입
- 상태
- 위험도
- assignment ID 목록
- 생성/수정 시각
- 버전

반면 스펙 모델은 문서성 있는 필드로 아래 항목들을 지원한다.

- `Problem`
- `Goal`
- `AcceptanceCriteria`
- `Tests`
- `Dependencies`
- `DerivedFrom`

즉, 현재 `F-001`은 실행 상태 추적용 메타데이터는 있으나, 사람과 에이전트가 참조할 수 있는 스펙 본문은 거의 없는 상태다.

## 부족한 점

### 1. 문제 정의가 없다

현재 스펙은 "무엇을 추가한다"는 제목만 있고, 왜 이 설정이 필요한지 설명이 없다.

빠진 내용 예시:

- 테스트 생성 단계에서 어떤 타임아웃 문제가 발생하는가
- 기존 `DefaultTimeoutSeconds`로는 왜 부족한가
- 어떤 상황에서 `testGeneration`만 별도 타임아웃이 필요한가

이 정보가 없으면 구현자가 필드만 추가하고 실제 운영 문제를 해결하지 못할 가능성이 있다.

### 2. 목표와 비목표가 없다

현재 제목만으로는 변경 범위가 불명확하다.

특히 아래가 분리되어 있지 않다.

- 목표: 테스트 생성 단계의 타임아웃을 독립 설정으로 분리한다
- 비목표: 다른 assignment 타입의 타임아웃 정책 변경은 이번 작업 범위에서 제외한다

이 구분이 없으면 구현 중 범위가 불어나기 쉽다.

### 3. 수락 기준이 스펙 본문에 없다

`specValidator` assignment 결과에는 다음 힌트가 남아 있다.

- `RunnerConfig`에 `int` 프로퍼티 추가
- 기본값 `600`
- `FlowRunner/SideEffectExecutor`에서 기존 타임아웃 패턴을 따라 적용

하지만 이 내용은 assignment 결과 요약에만 있고, 스펙 본문 `AcceptanceCriteria`에는 기록되어 있지 않다.

이 상태의 문제:

- 검증 기준이 실행 로그에만 존재한다
- 문서 export 시 핵심 요구사항이 빠질 수 있다
- 나중에 assignment가 정리되거나 요약이 바뀌면 요구사항이 유실된다

최소한 아래 수준의 AC가 필요하다.

- AC-1: `RunnerConfig`에 `TestGenerationTimeoutSeconds` 필드가 추가된다
- AC-2: 기본값은 `600`초다
- AC-3: test generation assignment 실행 시 해당 값을 사용한다
- AC-4: 다른 assignment 타입은 기존 타임아웃 동작을 유지한다
- AC-5: 설정값이 반영되는 테스트가 추가되거나 기존 테스트가 갱신된다

### 4. 영향 범위가 명시되어 있지 않다

현재 스펙만 봐서는 어떤 코드가 영향을 받는지 알 수 없다.

최소한 문서에 아래 정도는 있어야 한다.

- 설정 모델: `RunnerConfig`
- assignment dispatch / timeout 계산 지점
- test generation 실행 경로
- 관련 테스트 파일

영향 범위가 없으면 리뷰 시 누락된 수정 포인트를 찾기 어렵다.

### 5. 설정 의미와 우선순위가 정의되어 있지 않다

설정 추가 스펙이라면 값의 의미가 가장 중요하다. 하지만 현재 문서에는 아래가 없다.

- 단위가 초인지 밀리초인지
- `0` 또는 음수 허용 여부
- 미설정 시 기본값 사용 규칙
- `DefaultTimeoutSeconds`와 충돌할 때 어떤 값이 우선하는지

이 항목이 비어 있으면 구현자마다 다르게 해석할 수 있다.

### 6. 테스트 전략이 없다

스펙 모델에는 `Tests` 필드가 있는데 현재 비어 있다.

설정 추가 작업이라도 최소한 아래는 필요하다.

- 단위 테스트: 기본값과 override 값 검증
- 통합 테스트: test generation assignment가 전용 타임아웃을 사용함을 검증
- 회귀 테스트: 다른 assignment가 기존 timeout 경로를 유지하는지 검증

테스트 계획이 없으면 완료 판정 기준이 약해진다.

### 7. 실패 시 기대 동작이 없다

timeout 설정은 정상 경로보다 실패 경로 정의가 중요하다. 하지만 현재 스펙에는 아래가 없다.

- timeout 초과 시 assignment 상태가 어떻게 바뀌는가
- retry 정책과 어떤 관계가 있는가
- activity log / evidence에 무엇이 남아야 하는가
- 사용자에게 어떤 피드백이 보여야 하는가

이 정보가 없으면 동작은 구현돼도 운영 관점에서 불완전할 수 있다.

### 8. 문맥 정보가 부족하다

`DerivedFrom`, 관련 설계 문서, 선행 의사결정 링크가 없다.

현재 제목만으로는 이 스펙이 아래 중 어디에서 나온 요구사항인지 추적이 어렵다.

- runner 관련 설계 결정
- 테스트 생성 병목 이슈
- 기존 timeout 정책의 한계

문맥 링크가 없으면 나중에 왜 이런 설정이 들어갔는지 설명하기 어려워진다.

### 9. task 타입이라도 문서 최소 본문은 필요하다

현재 `F-001`은 `task` 타입이라 feature보다 가볍게 다뤄지고 있지만, 그렇더라도 실행 메타데이터만으로는 충분한 스펙 문서가 아니다.

task 타입 최소 본문은 있어야 한다.

- 변경 배경
- 기대 결과
- 완료 조건
- 영향 범위
- 검증 방법

현재는 이 최소선도 충족하지 못한다.

## 우선순위별 보완 제안

### 필수

1. `Problem`과 `Goal` 작성
2. `AcceptanceCriteria`를 스펙 본문에 명시
3. timeout 의미, 기본값, 적용 대상, 제외 대상을 명시
4. 테스트 계획을 `Tests` 또는 본문 섹션으로 추가

### 권장

1. 영향 받는 코드 경로를 정리
2. 관련 설계 문서 또는 이슈 링크 추가
3. 실패/재시도/로그 정책을 보강

### 선택

1. 예시 설정 JSON 추가
2. before/after 동작 비교 추가

## 권장 스펙 초안 구조

아래 정도만 채워도 현재보다 훨씬 문서성이 높아진다.

### Problem

테스트 생성 assignment는 기존 공용 timeout 설정을 사용하고 있어, 실제 소요 시간 특성을 반영한 개별 제어가 어렵다. 그 결과 test generation 단계에서 timeout 기준을 보수적으로 조정하기 어렵고, 다른 assignment와 동일 정책에 묶인다.

### Goal

`RunnerConfig`에 `TestGenerationTimeoutSeconds`를 추가해 test generation assignment에 한해 별도 timeout을 적용한다.

### Non-goals

- implementation, review 등 다른 assignment의 timeout 정책 변경
- timeout 초과 후 retry 정책 전체 재설계

### Acceptance Criteria

- AC-1: `RunnerConfig`에 `TestGenerationTimeoutSeconds` 정수 설정이 추가된다.
- AC-2: 기본값은 `600`초다.
- AC-3: test generation assignment는 공용 timeout 대신 이 값을 사용한다.
- AC-4: 다른 assignment는 기존 `DefaultTimeoutSeconds` 동작을 유지한다.
- AC-5: 관련 자동 테스트가 추가 또는 갱신된다.

### Test Plan

- 기본 설정값 적용 테스트
- 사용자 설정 override 테스트
- test generation에만 전용 timeout이 적용되는지 검증
- 다른 assignment 회귀 테스트

## 결론

현재 `F-001`은 실행 추적 관점에서는 유효하지만, 스펙 문서 관점에서는 본문이 매우 부족하다.

핵심 부족 사항은 다음 네 가지다.

- 왜 필요한지에 대한 문제 정의 부재
- 무엇을 만족해야 하는지에 대한 수락 기준 부재
- 어디까지 바뀌는지에 대한 범위 정의 부재
- 어떻게 검증할지에 대한 테스트 계획 부재

즉, 지금 상태의 `F-001`은 "작업 항목"으로는 볼 수 있어도, 독립적인 "스펙 문서"로 보기에는 정보가 모자란다.