# Flow 살아있는 스펙 실행 문서

이 문서는 Flow에서 일부 spec을 단순 설명 문서가 아니라 `직접 실행하고 결과를 확인할 수 있는 살아있는 spec`으로 다루는 방향을 정리한다.

핵심 아이디어는 아래와 같다.

- 모든 spec이 실행 가능해야 하는 것은 아니다.
- 하지만 실행 가능한 spec은 문서 안에서 바로 입력을 넣고 결과를 확인할 수 있으면 좋다.
- 이렇게 되면 spec은 정적인 요구사항 설명을 넘어, 실제 동작을 보여 주는 살아있는 계약이 된다.

예를 들어 더하기 기능 spec이 있다면, 사용자가 숫자 두 개를 입력하고 결과가 예상대로 나오는지 문서 안에서 바로 확인할 수 있어야 한다.

## 1. 왜 필요한가

현재 spec은 문제, 목표, acceptance criteria, tests, evidence를 담는 계약이다. 하지만 아래 문제가 남는다.

- 문서를 읽는 것만으로는 실제 동작 감각이 부족할 수 있다.
- 사용자가 acceptance criteria와 실제 동작 사이를 직접 연결해 보기 어렵다.
- 간단한 기능은 테스트 결과보다 직접 입력/출력 확인이 더 빠른 경우가 있다.
- spec이 살아있는 artifact가 되려면 `읽기`와 `실행`이 더 가까워질 필요가 있다.

따라서 Flow는 가능한 spec에 한해 `interactive run`, `playground`, `live example` 같은 실행 섹션을 제공하는 편이 맞다.

## 2. 모든 spec에 강제하면 안 되는 이유

이 기능은 강력하지만, 모든 spec에 의무화하면 오히려 모델이 망가진다.

아래 spec은 interactive run이 잘 맞지 않을 수 있다.

- 순수 운영 정책 spec
- 장기 migration 계획 spec
- 사람 리뷰 절차 자체를 다루는 spec
- 외부 시스템/권한/인프라가 없으면 재현 불가능한 spec
- 대규모 멀티스텝 workflow spec

반대로 아래 spec은 interactive run이 잘 맞는다.

- 입력과 출력이 비교적 명확한 기능 spec
- 계산, 변환, 필터링, 포맷팅 같은 deterministic 기능 spec
- 간단한 API request/response 확인이 가능한 spec
- 작은 UI interaction이나 폼 동작을 샌드박스에서 재현할 수 있는 spec

즉 이 기능은 `모든 spec의 공통 필수`가 아니라 `일부 spec의 선택적 강화 기능`이어야 한다.

## 3. Live Spec의 정의

Flow에서 Live Spec은 아래 조건을 만족하는 spec을 뜻한다.

- 실행 가능한 예시 또는 시나리오가 있다.
- 사용자가 입력을 바꿔 결과를 볼 수 있다.
- 결과가 acceptance criteria 또는 expected output과 연결된다.
- 실행 결과를 evidence나 quick verification으로 남길 수 있다.

한 줄로 줄이면 아래와 같다.

`Live Spec은 가능한 범위에서 문서 안에서 직접 동작을 확인할 수 있는 spec이다.`

## 4. 핵심 원칙

### 4.1 spec을 대체하지 않는다

interactive run은 spec 본문을 대체하지 않는다. problem, goal, acceptance criteria, tests는 여전히 spec의 중심이다.

live execution은 그 계약을 더 빠르게 이해하고 확인하게 돕는 보조 실행 계층이다.

### 4.2 테스트와 경쟁하지 않는다

interactive run은 자동 테스트를 대체하지 않는다.

- 테스트는 반복 가능하고 기계적으로 검증 가능한 근거다.
- interactive run은 이해, 탐색, 수동 확인, 데모에 강하다.

즉 둘은 경쟁 관계가 아니라 역할이 다르다.

### 4.3 sandbox와 safety가 필요하다

문서 안에서 실행 가능한 기능은 안전한 범위에 있어야 한다.

- side effect가 큰 명령은 직접 실행시키면 안 된다.
- destructive action은 원칙적으로 제외해야 한다.
- 실행 환경, 입력 범위, timeout, resource limit가 필요하다.

### 4.4 expected result가 연결되어야 한다

단순히 실행 버튼만 있는 것은 약하다. 입력 예시와 기대 결과가 같이 있어야 살아있는 spec이 된다.

예:

- 입력: `2 + 3`
- 기대 결과: `5`
- 관련 AC: `ac-001`

### 4.5 실행 결과도 근거로 남길 수 있어야 한다

실행 후 아래 일부는 evidence나 verification으로 저장할 수 있어야 한다.

- 사용한 입력
- 실제 출력
- 기대 결과와 비교 결과
- 실행 시각
- 실행 환경 정보

## 5. 권장 정보 구조

실행 가능한 spec에는 아래 추가 구조를 둘 수 있다.

```text
Spec
  Problem / Goal
  Acceptance Criteria
  Tests
  Evidence
  Live Execution
    Preset Inputs
    Custom Inputs
    Expected Outputs
    Run Result
    Save As Evidence
```

즉 live execution은 spec 내부의 선택적 section으로 두는 편이 자연스럽다.

## 6. 대표 예시

### 6.1 더하기 기능 spec

spec이 아래와 같다고 하자.

- problem: 두 수를 입력받아 합을 반환해야 한다.
- acceptance criteria: `a`와 `b`를 입력하면 `a+b`가 반환된다.

이 경우 live execution section은 아래처럼 동작할 수 있다.

- 입력 필드: `a`, `b`
- preset: `(1, 2)`, `(0, 0)`, `(-1, 3)`
- 실행 버튼
- 결과 필드: `sum`
- 기대 결과 비교 표시
- `이 결과를 evidence로 저장` 버튼

이런 구조면 사용자는 문서만 읽는 것이 아니라 바로 기능을 만져 볼 수 있다.

### 6.2 API spec

간단한 조회 API spec이라면 아래처럼 둘 수 있다.

- request preset 선택
- request body 편집
- 실행 결과 status/body 확인
- expected schema와 비교

### 6.3 UI validation spec

작은 상호작용이라면 아래 구조가 가능하다.

- sample form 입력
- submit 실행
- visible output / validation message 확인
- screenshot evidence 저장

## 7. 권장 스키마 확장

모든 spec에 필수는 아니지만, 장기적으로 아래와 같은 선택 필드를 둘 수 있다.

```json
{
  "id": "spec-001",
  "title": "두 수의 합 계산",
  "liveExecution": {
    "enabled": true,
    "kind": "function",
    "title": "Add Number Playground",
    "description": "숫자 두 개를 입력해 합계를 확인한다.",
    "inputSchema": {
      "fields": [
        {
          "id": "a",
          "label": "First Number",
          "type": "number",
          "required": true
        },
        {
          "id": "b",
          "label": "Second Number",
          "type": "number",
          "required": true
        }
      ]
    },
    "presets": [
      {
        "id": "preset-1",
        "label": "1 + 2",
        "input": { "a": 1, "b": 2 },
        "expectedOutput": { "sum": 3 }
      }
    ],
    "runner": {
      "mode": "sandbox",
      "target": "local-function"
    },
    "relatedAcceptanceCriteria": ["ac-001"],
    "saveAsEvidence": true
  }
}
```

### 최소 의미

- `enabled`: live execution 지원 여부
- `kind`: `function | api | ui | script`
- `inputSchema`: 사용자 입력 폼 정의
- `presets`: 빠른 예시 실행값
- `expectedOutput`: 기대 결과
- `runner`: 어떤 샌드박스나 실행 target을 쓰는지
- `relatedAcceptanceCriteria`: 어떤 AC를 보여 주는지
- `saveAsEvidence`: 결과 저장 가능 여부

## 8. Web / UX 관점

Flow web의 spec detail에는 현재 notebook-style section 구조가 있다. live spec은 여기에 자연스럽게 들어갈 수 있다.

권장 section:

1. Summary
2. Goal & Context
3. Acceptance Criteria
4. Dependencies
5. Assignments
6. Review Requests
7. Tests & Evidence
8. Live Run / Playground
9. Activity Timeline
10. Actions

이 섹션에서 사용자는 아래를 할 수 있어야 한다.

- preset을 선택한다.
- custom input을 넣는다.
- 실행 결과를 본다.
- expected output과 비교한다.
- 결과를 evidence 또는 verification note로 저장한다.

## 9. Runner와의 관계

Live execution은 runner와 직접 같은 역할을 하지는 않는다.

- runner는 spec lifecycle을 orchestration한다.
- live execution은 spec을 이해하고 빠르게 검증하는 interactive path다.

하지만 둘은 아래 지점에서 연결될 수 있다.

- live execution 결과를 evidence로 남긴다.
- manual verification이나 quick smoke check로 기록한다.
- runner가 `liveExecution` 가능 여부를 읽고 적절한 검증 보조를 제안한다.

즉 live execution은 runner를 대체하지 않지만, runner가 만든 결과를 더 쉽게 확인하게 만들 수 있다.

## 10. 운영 규칙

- 모든 spec에 live execution을 강제하지 않는다.
- deterministic하고 안전한 spec부터 우선 적용한다.
- destructive side effect가 있는 실행은 기본적으로 제외한다.
- live execution 결과는 테스트 통과와 별도로 기록한다.
- 가능하면 preset과 expected output을 함께 둔다.
- acceptance criteria와 연결되지 않는 live playground는 장식이 되기 쉬우므로 피한다.

## 11. 구현 우선순위

### Now

- [x] 실행 가능한 spec을 위한 `살아있는 spec` 개념을 문서화한다.
  왜 필요한가: 문서를 읽는 것과 실제 동작 확인 사이의 간극을 줄이기 위해 필요하다.
  변경 대상: `docs/flow-live-spec-execution.md`
  완료 기준: 범위, 예시, 스키마 방향, web section 구성이 문서에 정리되어 있다.

- [x] 상위 문서와 관련 문서에 live execution 개념을 연결한다.
  왜 필요한가: 살아있는 spec이 프로젝트 철학과 workspace 설계 문맥에서 발견되어야 한다.
  변경 대상: `README.md`, `docs/flow-schema.md`, `docs/flow-webservice-integrated-workspace.md`
  완료 기준: 관련 문서에 live execution 또는 runnable spec 방향이 반영되어 있다.

### Next

- [ ] `liveExecution` 스키마와 evidence 저장 형식을 core 계약으로 구체화한다.
  왜 필요한가: 문서 개념을 실제 저장 모델로 내리기 위해 필요하다.
  변경 대상: `docs/flow-schema.md` 또는 별도 schema 문서
  완료 기준: 입력 필드, preset, expected output, 실행 결과, evidence 저장 규칙이 합의된다.

- [ ] flow-web에 `Live Run / Playground` section을 추가한다.
  왜 필요한가: 가능한 spec은 문서 안에서 바로 입력과 결과를 확인할 수 있어야 한다.
  변경 대상: web read model과 UI 문서
  완료 기준: spec detail에서 preset 선택, custom input, run result, evidence 저장이 가능하다.

### Later

- [ ] runner와 build/test 도구가 live execution 가능한 spec을 자동으로 감지하고 quick verification 경로를 제안한다.
  왜 필요한가: 사용자가 어떤 spec이 실행 가능한지 찾는 비용을 줄이기 위해 필요하다.
  변경 대상: runner/build integration 설계 문서
  완료 기준: relevant spec에서 live run availability와 quick check entry가 노출된다.

## 12. 결론

Flow에서 spec은 이미 실행 계약이다. 여기에 가능한 경우 직접 입력과 결과 확인까지 붙으면 spec은 더 강한 의미의 살아있는 artifact가 된다.

따라서 Flow는 아래 방향으로 가는 것이 적절하다.

- 모든 spec이 아니라 가능한 spec만 live execution을 가진다.
- live execution은 spec 본문을 대체하지 않고 보강한다.
- 테스트와 evidence를 대체하지 않고 연결한다.
- web workspace 안에서 문서와 실행이 가까워지게 한다.

이 방향은 `읽는 spec`을 `직접 확인할 수 있는 spec`으로 확장한다는 점에서 Flow의 철학과 잘 맞는다.