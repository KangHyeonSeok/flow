# Flow 구현 플랫폼 권고

이 문서는 Flow의 핵심 가치와 현재 저장소 구조를 기준으로, 어떤 구현 환경을 기준 플랫폼으로 삼아야 하는지 정리한다.

핵심 결론은 단순하다.

- Flow는 단일 언어 통합보다 `권위 있는 core contract`를 중심으로 한 역할 분리가 더 중요하다.
- 현재 저장소와 요구사항에는 `.NET 중심 코어 + TypeScript/React 웹` 구성이 가장 적합하다.
- Python, PowerShell, 셸 스크립트는 보조 자동화와 플랫폼별 운영 도구로 제한하는 편이 낫다.

## 1. 왜 이 선택이 Flow에 맞는가

README가 정의하는 Flow의 핵심 가치는 아래에 가깝다.

- project -> epic -> spec -> code/tests/evidence를 하나의 실행 모델로 묶는다.
- 상태 전이는 UI나 개별 agent가 아니라 공용 core rule evaluator가 책임진다.
- runner는 long-running orchestration, retry, timeout, review loop를 다룬다.
- live execution은 일부 spec에 선택적으로 붙는 안전한 실행 capability다.

이 요구를 구현하려면 아래 특성이 중요하다.

- 강한 타입 기반 도메인 모델
- long-running background workflow를 다루기 쉬운 런타임
- 파일 기반 저장과 프로세스 실행, timeout, cancellation, concurrency 제어
- CLI, API, runner가 같은 계약을 재사용할 수 있는 구조

현재 저장소에서는 이 역할을 이미 C#/.NET이 가장 많이 담당하고 있다.

## 2. 권장 기준 플랫폼

### 2.1 권장안

Flow의 기준 플랫폼은 아래처럼 두 축으로 두는 편이 가장 적합하다.

1. Backend/Core standard: .NET 8+
2. Frontend/UI standard: TypeScript + React

즉 `모든 것을 한 언어로 통합`하기보다, 아래처럼 역할을 고정한다.

- `.NET`: core domain, state machine, runner, CLI, API, storage contract
- `TypeScript/React`: project/epic/spec workspace, live execution UI, evidence/review 화면
- `Python`: E2E, RAG, 실험적 보조 도구
- `PowerShell/bash`: 로컬 운영 스크립트와 빌드 보조

### 2.2 왜 Node.js 풀스택보다 .NET 중심이 나은가

Node.js는 web과 interactive UI에는 잘 맞지만, Flow의 본질은 UI보다 아래에 있다.

- deterministic state transition
- review loop orchestration
- retry, timeout, stale assignment recovery
- CLI와 API와 runner의 모델 공유
- 파일 시스템 기반 저장 계약

이 영역은 현재 구조상 .NET이 더 자연스럽고 이미 구현 자산도 많다. 지금 Node.js로 역통합하면 얻는 것보다 잃는 것이 크다.

### 2.3 왜 Python 중심도 아닌가

Python은 agent 실험, E2E, RAG에는 유용하지만, Flow의 authoritative runtime으로 삼기에는 아래 문제가 있다.

- core contract와 runtime state model이 분산되기 쉽다.
- CLI/API/runner 간 타입 일관성을 유지하기 어렵다.
- 장기적으로 문서 계약과 실행 계약의 일치성을 보장하기가 더 어렵다.

즉 Python은 유용하지만 중심 플랫폼은 아니다.

## 3. 무엇을 통합하고 무엇을 분리할 것인가

### 3.1 통합해야 하는 것

진짜로 통합해야 하는 것은 언어가 아니라 계약이다.

우선순위는 아래와 같다.

1. `flow-core` 모델을 authoritative source of truth로 둔다.
2. API schema를 OpenAPI 또는 JSON Schema로 외부화한다.
3. web 타입은 수동 복제보다 생성 기반으로 맞춘다.
4. state, event, decision, change, liveExecution 스키마를 공용 계약으로 정리한다.
5. runner, API, web이 같은 validation semantics를 공유하게 만든다.

즉 `언어 하나로 통일`보다 `계약 하나로 통일`이 훨씬 중요하다.

### 3.2 분리해야 하는 것

아래는 의도적으로 분리하는 편이 좋다.

- UI projection과 core state transition
- live execution sandbox와 메인 API 프로세스
- 실험적 agent backend와 core rule evaluator
- Windows 전용 capture 도구와 cross-platform core/runtime

## 4. Flow 저장소에 대한 구체적 권고

### 4.1 .NET에 남겨야 하는 영역

- `tools/flow-core`
- `tools/flow`
- `tools/flow-api`
- runner host와 background orchestration
- project/epic/spec/change/decision/liveExecution 저장 계약

이유는 아래와 같다.

- 이미 core, CLI, API가 같은 런타임을 공유하고 있다.
- 상태 전이, timeout, cancellation, process execution 로직이 .NET 쪽에 모여 있다.
- 장기적으로 worker 분리나 durable workflow 도입 시에도 확장하기 쉽다.

### 4.2 TypeScript/React에 남겨야 하는 영역

- `tools/flow-web`
- notebook-style spec workspace
- project/epic overview
- activity timeline
- review response UI
- live execution playground
- evidence viewer

이유는 아래와 같다.

- 문서와 실행을 붙인 상호작용 화면을 만들기 쉽다.
- live spec처럼 preset input, expected output, result compare를 표현하기 좋다.
- API projection을 빠르게 시각화하고 검증하기 좋다.

### 4.3 Python과 스크립트의 적정 위치

Python과 스크립트는 아래 정도로 제한하는 편이 적절하다.

- E2E 시나리오
- RAG/임베딩 처리
- 실험적 데이터 처리
- 로컬 개발 편의 스크립트

반대로 아래는 Python/스크립트 중심으로 옮기지 않는 편이 좋다.

- state machine
- runner dispatch logic
- project/epic/spec 저장 계약
- API authoritative validation

## 5. 다른 더 좋은 플랫폼이 있는가

완전 대체 플랫폼보다는 보조 인프라 후보가 더 중요하다.

### 5.1 검토 가치가 있는 후보: Temporal

Flow의 runner 요구는 아래와 닮아 있다.

- long-running workflow
- retry/backoff
- timeout
- human approval wait
- resume after external input

이런 요구 때문에 장기적으로는 Temporal 같은 durable workflow 엔진이 잘 맞을 수 있다. 다만 현재는 먼저 Flow 자체의 event/state contract를 더 단단히 만드는 편이 우선이다.

권고 순서는 아래다.

1. 먼저 `flow-core` state/event contract를 고정한다.
2. 그 다음 runner host를 더 분리한다.
3. durable workflow가 정말 필요해지면 Temporal 같은 엔진을 붙인다.

### 5.2 꼭 필요한 보조 플랫폼: sandbox 실행 경계

live execution 때문에 중요한 것은 새 언어보다 안전한 실행 경계다.

권장 방향:

- 메인 API 안에서 임의 코드를 직접 실행하지 않는다.
- 별도 sandbox worker 또는 isolated execution target을 둔다.
- 허용 target만 실행한다.
- 결과는 evidence/verification 형태로만 반환한다.

즉 live execution의 핵심은 플랫폼 교체가 아니라 안전한 실행 격리다.

## 6. .NET은 macOS/Linux에서도 동작하는가

대체로 `예`다. 하지만 전부가 아니라 `코어는 대부분 가능, 일부 도구는 플랫폼 전용`이다.

### 6.1 macOS/Linux에서 동작 가능한 영역

아래 영역은 설계와 코드 구조상 macOS/Linux에서도 동작 가능성이 높다.

- `tools/flow-core`
- `tools/flow`
- `tools/flow-api`
- `tools/flow-console`
- `tools/flow-web`와 API 연동 자체

근거:

- 대부분 프로젝트가 `net8.0`을 사용한다.
- 저장 경로는 `Environment.SpecialFolder.UserProfile` + `.flow` 기준이라 OS별 홈 디렉터리에 맞춰 동작한다.
- 경로 조합은 `Path.Combine`을 사용한다.
- 프로세스 종료도 POSIX 분기를 가진다.
- CLI backend는 Windows 전용 launcher 처리를 별도 분기로 감싼다.

### 6.2 이미 크로스플랫폼을 고려한 코드

- `tools/flow-core/Backend/ProcessKiller.cs`: Windows와 POSIX를 분기해 종료 방식을 다르게 처리한다.
- `tools/flow-core/Backend/ClaudeCliBackend.cs`: Windows에서만 `.ps1`, `.bat`, Git Bash 탐지를 수행한다.
- `tools/flow-api/Program.cs`: 사용자 홈 아래 `.flow`를 기준 저장소로 사용하므로 경로 모델 자체는 OS 중립적이다.

즉 core/runtime 쪽은 `Windows 전용으로 고정된 설계`가 아니라 `Windows 특수 처리를 추가한 크로스플랫폼 설계`에 가깝다.

### 6.3 Windows 전용이거나 주의가 필요한 영역

아래는 그대로는 macOS/Linux에서 동작하지 않거나 기능 차등이 있다.

#### capture-cli

- `tools/capture-cli/capture-cli.csproj`는 `net8.0-windows10.0.19041.0`를 타깃으로 한다.
- Windows Graphics Capture와 Win32 API를 사용한다.

즉 `capture-cli`는 명확한 Windows 전용 도구다.

#### embed GPU 경로

- `tools/embed` 자체는 ONNX Runtime 기반이라 CPU 경로는 크로스플랫폼으로 가져갈 수 있다.
- 하지만 DirectML 가속 경로는 Windows 전용이다.

즉 `embed`는 `부분적 크로스플랫폼`으로 보는 편이 맞다.

#### 운영 스크립트

- `.ps1` 스크립트는 PowerShell이 있으면 macOS/Linux에서도 실행할 수 있지만, 일반적으로는 Windows 친화적이다.
- 다행히 저장소에는 `.sh` 스크립트도 같이 있는 편이라 운영 경로를 이원화할 수 있다.

### 6.4 실무적으로는 어떻게 보는 게 맞는가

실무 관점에서는 아래처럼 구분하는 것이 정확하다.

- `Flow core/runtime`: 크로스플랫폼 지향
- `capture tooling`: Windows 전용
- `embed acceleration`: Windows 최적화, CPU는 타 OS 가능
- `web`: 크로스플랫폼

즉 `.NET으로 구현되었으니 macOS/Linux에서 안 된다`는 판단은 틀리고, `현재 .NET 구현 중 일부 도구가 Windows 전용`이라고 보는 편이 맞다.

## 7. 권장 후속 작업

### Now

- `flow-core` 기준 계약을 authoritative source로 더 명확히 문서화한다.
- web 타입 생성 전략을 정해 수동 타입 중복을 줄인다.
- Windows 전용 도구와 cross-platform 런타임을 문서와 폴더 구조에서 더 분명히 구분한다.

### Next

- runner host를 별도 서비스로 분리할지 결정한다.
- live execution sandbox 경계를 별도 worker로 설계한다.
- macOS/Linux CI에서 `flow-core`, `flow-api`, `flow` smoke test를 추가한다.

## 8. 최종 권고

Flow에 가장 적합한 구현 환경은 아래다.

1. authoritative core/runtime: .NET 8+
2. integrated web workspace: React + TypeScript
3. supporting automation: Python + shell scripts
4. optional future workflow engine: Temporal
5. live execution safety boundary: isolated sandbox worker

한 줄로 정리하면 아래와 같다.

`언어를 하나로 줄이기보다, .NET을 실행 계약의 기준 플랫폼으로 고정하고 React를 문서/실행 UX 계층으로 유지하는 것이 Flow의 핵심 가치에 가장 잘 맞다.`