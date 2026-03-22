# flow

Flow는 스펙 기반 개발 워크플로를 정의하고 실행하는 실험적 통합 시스템이다. 이 저장소의 중심은 개별 도구의 사용법이 아니라, 프로젝트 문맥과 실제 구현이 어떻게 같은 계약 위에서 움직여야 하는지에 있다.

아직 개발 중인 시스템이며, 현재 README는 개별 사용법보다 아래 질문에 답하는 데 집중한다.

- 왜 Flow가 필요한가
- 프로젝트, 에픽, 스펙, 변경 사항, 결정 사항은 어떻게 연결되는가
- Runner는 무엇을 자동화하고 무엇을 자동화하지 않는가
- 구현, 테스트, 리뷰, evidence는 어떤 철학으로 한 흐름에 묶이는가

## Flow가 풀려는 문제

일반적인 개발 흐름에서는 요구사항 문서, 작업 분해, 구현 상태, 테스트 결과, 리뷰 요청, 운영 근거가 서로 다른 화면과 도구에 흩어진다. 그러면 아래 문제가 반복된다.

- 상위 방향과 개별 구현 작업이 분리된다.
- 리뷰와 테스트가 개발 흐름의 일부가 아니라 사후 점검으로 밀린다.
- 자동화 agent가 무엇을 읽고 무엇을 제안할 수 있는지 계약이 흐려진다.
- 요구사항 변경의 원인과 그 결과로 바뀐 코드 사이의 추적이 약해진다.

Flow는 이 문제를 `프로젝트 -> 에픽 -> 스펙 -> 코드/테스트/evidence`라는 구조와, 그 바깥에서 들어오는 `변경 사항`, `결정 사항` 입력을 하나의 운영 모델로 묶어 줄이려 한다.

## 핵심 철학

### 1. 스펙은 실행 계약이다

Flow에서 스펙은 단순 문서가 아니라 실제 구현, 테스트, 리뷰, evidence 수집이 매달리는 실행 단위다. 작업을 dispatch하고 상태를 바꾸고 검증하는 기준도 스펙이다.

가능한 spec은 여기서 한 걸음 더 나아가, 문서 안에서 직접 입력을 넣고 결과를 확인할 수 있는 `살아있는 spec`이 될 수 있다. 예를 들어 더하기 기능 spec이라면 숫자를 입력해 실제 결과를 바로 확인하는 interactive run/playground를 가질 수 있다.

### 2. 모든 것을 스펙으로 만들지는 않는다

요구사항 수정, 하드웨어 변경, 운영 주체 변경, 정책 변경 같은 현실의 변화는 중요하지만, 그 자체가 항상 실행 단위는 아니다. 또한 구현 중에는 사람 선택이 필요한 미해결 결정 문제가 생긴다. Flow는 이런 변화와 결정을 스펙으로 정의하지 않고 별도의 `변경 사항`, `결정 사항` 입력으로 기록한 뒤, 어떤 프로젝트/에픽/스펙/코드에 영향을 주는지 추적하려 한다.

### 3. 계층마다 책임이 다르다

- Project: 왜 이 시스템이 존재하는지와 전체 방향을 설명한다.
- Epic: 여러 스펙을 묶는 계획 단위이자 내비게이션 단위다.
- Spec: 실제 구현과 검증이 일어나는 실행 단위다.
- Change Record: 무엇이 왜 바뀌었는지 기록하는 원인 단위다.
- Decision Record: 무엇을 선택해야 하는지 기록하는 미해결 결정 단위다.
- Code, Tests, Evidence: 스펙과 변경 사항이 실제로 반영되었는지 닫아 주는 검증 단위다.

### 4. 상태 전이는 UI가 아니라 core가 담당한다

Flow는 웹 화면, CLI, VS Code 확장, 외부 채널이 각자 상태를 직접 수정하는 구조를 지양한다. 상태 전이는 공용 core contract와 rule evaluator를 통해서만 일어나야 하며, 상위 표면은 event proposal 또는 command request를 만드는 얇은 계층이어야 한다.

### 5. 문서와 구현은 반복마다 다시 맞춘다

이 저장소는 문서를 사후 정리물로 두지 않는다. 구현 전에 문서를 먼저 맞추고, 구현 후에는 테스트 결과와 남은 리스크를 다시 문서에 반영하는 반복을 기본 작업 방식으로 둔다.

## 운영 모델

Flow가 지향하는 기본 구조는 아래와 같다.

```text
Project
	Epic
		Spec
			Acceptance Criteria
			Tests
			Evidence

Change Record
	-> impacts Project
	-> impacts Epic
	-> impacts Spec
	-> impacts Code / Tests / Evidence

Decision Record
	-> blocks or clarifies Spec
	-> guides Change / Spec updates
	-> resumes Runner after user choice
```

이 구조에서 중요한 점은 두 가지다.

- `Project -> Epic -> Spec`은 방향, 계획, 실행의 계층이다.
- `Change Record -> ...`와 `Decision Record -> ...`는 현실 변화와 사람 결정이 시스템 안으로 들어오는 입력 경로다.

즉 Flow는 `모든 것이 스펙이다`보다는 `중요한 변화와 결정은 모두 추적 가능한 입력이어야 한다`에 가깝다.

## 프로젝트, 에픽, 스펙

### 프로젝트

프로젝트 문서는 Flow 전체가 어떤 문제를 풀고 있는지, 어떤 제약과 원칙을 유지해야 하는지, 어떤 에픽 묶음으로 나뉘는지를 설명한다. 개별 구현 세부사항이 아니라 상위 방향을 고정하는 문서다.

### 에픽

에픽은 여러 스펙을 묶는 계획 단위다. 에픽은 실행 상태의 직접 단위가 아니라, 큰 사용자 가치와 milestone을 설명하는 상위 문맥이다. 스펙이 늘어날수록 에픽은 우선순위, 범위, 의존성을 설명하는 핵심 내비게이션 층이 된다.

### 스펙

스펙은 Flow의 authoritative execution contract다. acceptance criteria, 상태, assignment, review request, 테스트, evidence가 모두 이 단위에 걸린다. Runner가 실제로 집어 들어 처리하는 것도 스펙이다.

## 변경 사항을 별도 축으로 두는 이유

장기 운영에서는 `왜 바뀌었는가`가 `무엇을 구현할 것인가`만큼 중요하다. 요구사항 변경, 환경 변경, 운영 정책 변경을 스펙 본문에만 흡수해 버리면 아래 문제가 생긴다.

- 나중에 왜 특정 스펙이 생겼는지 설명하기 어렵다.
- 영향 분석이 끝났는지, 단지 문장만 바뀌었는지 구분하기 어렵다.
- 여러 에픽과 여러 스펙에 퍼지는 하나의 변화 원인을 잃는다.
- 코드, 테스트, evidence까지 반영되었는지 닫힘 조건을 강제하기 어렵다.

그래서 Flow는 변경 사항을 append-only 성격의 입력으로 두고, 그 변화가 어떤 프로젝트/에픽/스펙/코드/테스트/evidence에 전파되어야 하는지 추적하는 방향으로 가고 있다. 스펙은 실행 계약이고, 변경 사항은 실행 계약을 다시 써야 하는 이유다.

## 결정 사항을 별도 축으로 두는 이유

장기 운영에서는 `무엇을 선택해야 하는가`도 중요하다. 구현 중에는 아래처럼 사람의 선택이 필요한 갈림길이 반복해서 생긴다.

- acceptance criteria 해석이 둘 이상 가능하다.
- UX, 정책, 권한, 비용 같은 문제에서 agent가 임의로 결정하면 안 된다.
- review request로는 단순 승인/반려가 아니라 방향 선택 자체를 물어야 한다.
- 사용자는 runner 상태가 아니라 문제와 대안만 보고 판단하는 편이 낫다.

이 문제를 spec 본문에 TODO처럼 묻어 두면 아래 부작용이 생긴다.

- 사용자가 무엇을 결정해야 하는지 한눈에 보기 어렵다.
- 왜 spec이 특정 방향으로 정의되었는지 근거가 약해진다.
- runner는 내부적으로 block되지만 사용자에게는 상태 이름만 보이게 된다.
- 실행 계약인 spec과 미해결 선택지가 같은 문서에 섞인다.

그래서 Flow는 Decision Record를 별도 입력으로 두고, 문제, 사실, 제약, 대안, 추천안, 최종 선택과 근거를 문서로 남기는 방향으로 가고 있다. 스펙은 실행 계약이고, 결정 사항은 그 실행 계약을 확정하거나 수정하기 위해 필요한 사람 선택의 근거다.

이때 중요한 운영 요구가 두 가지 더 있다.

- Relevant Context Retrieval: agent가 현재 spec 이행에 정말 필요한 Change/Decision Record만 점진적으로 읽어 context window를 통제해야 한다.
- Human-in-the-loop 최소화: Decision Record가 열릴 때 사람에게 빈 질문을 던지지 말고, agent가 먼저 상황 분석과 대안별 리스크/이득 보고서를 초안으로 채워야 한다.

## Runner 구현 철학

Runner는 자동화의 핵심이지만, 만능 판단 엔진이 되어서는 안 된다. 현재 Flow의 Runner 철학은 아래에 가깝다.

### 1. Runner는 orchestration coordinator다

Runner는 실행 가능한 스펙을 선택하고, 잠금을 잡고, 최신 상태를 다시 읽고, rule 또는 agent를 호출하고, 결과를 저장하고, 로그를 남기는 조정자다. 상태 전이 규칙 자체를 Runner가 임의로 해석하면 안 된다.

### 2. Agent는 state mutator가 아니라 event producer다

Developer, Architect, Test Validator, Spec Validator 같은 agent는 직접 상태를 바꾸지 않는다. agent는 단일 proposed event를 제안하고, 그 이벤트가 현재 상태와 역할 권한에 맞는지는 core 규칙이 판정한다.

### 3. Rule evaluator가 상태 전이를 책임진다

상태 전이는 Runner나 UI의 편의 로직이 아니라 공용 규칙 계층에서 계산되어야 한다. 그래야 CLI, webservice, extension, 외부 채널이 모두 같은 결과를 공유할 수 있다.

### 4. 리뷰는 사후 절차가 아니라 런타임의 일부다

Flow의 review loop는 구현이 끝난 뒤 따로 붙는 장식이 아니다. Spec Validator, review request lifecycle, 사용자 응답, 재평가, rework, failure handling까지 포함한 하나의 실행 경로다.

### 5. 실패와 재시도도 계약 안에 둔다

Runner는 timeout recovery, retry cooldown, review deadline, stale assignment 회수 같은 운영 문제를 예외 처리로 숨기지 않는다. 이런 상황도 스펙 상태, assignment 상태, activity log 안에서 재현 가능하게 다뤄야 한다.

### 6. 사용자 결정은 상태가 아니라 문제로 보여야 한다

Runner는 내부적으로 review, implementation, test-validation 어느 단계에서든 사용자 결정을 기다릴 수 있다. 하지만 사용자는 그 내부 상태에 접근해서 판단하면 안 된다. 사용자에게 보여야 하는 것은 `이 문제에 어떤 대안이 있고 무엇을 선택해야 하는가`이며, Decision Record는 그 경험을 위한 문서이자 runner 재개를 위한 근거다.

이 경험이 성립하려면 runner와 agent는 두 가지를 더 지켜야 한다.

- 필요한 문맥만 읽는 retrieval 전략을 사용한다.
- 사용자에게 올라가기 전에 decision package를 충분히 초안 작성한다.

## 구현이 닫히는 방식

Flow는 `문서 작성 -> 구현 -> 테스트 -> 리뷰 -> evidence`를 분리된 단계로 보기보다, 하나의 스펙이 닫히기 위해 서로 맞물려야 하는 요소로 본다.

- 문서는 문제와 acceptance criteria를 제공한다.
- 구현은 스펙을 코드로 옮긴다.
- 테스트는 기준 충족 여부를 기계적으로 확인한다.
- 리뷰는 기준이 맞는지 다시 묻는다.
- evidence는 실제 반영 사실을 남긴다.

이 중 하나만 남고 나머지가 비어 있으면 Flow 관점에서는 아직 수렴하지 않은 상태다.

## 다른 표면들의 역할

Flow는 여러 도구를 만들고 있지만, 중요한 것은 도구 수가 아니라 책임 분리다.

- CLI: 문서와 저장 계약을 직접 다루는 운영 진입점
- Runner: 스펙 dispatch, 재시도, timeout, review loop를 실행하는 자동화 계층
- Web: 프로젝트/에픽/스펙과 운영 이력을 한 workspace 안에서 보여 주는 통합 뷰
- Extension / 외부 채널: 별도 규칙 엔진이 아니라 같은 core contract로 연결되는 입력 표면
- Build, E2E, Capture, RAG: 스펙과 변경 사항을 검증하고 설명하는 보조 계층

또한 가능한 spec은 web workspace 안에서 직접 실행 가능한 live playground를 통해 문서와 동작 확인을 더 가깝게 붙일 수 있다.

즉 Flow의 목적은 기능이 많은 툴박스를 만드는 것이 아니라, 서로 다른 표면이 같은 실행 모델을 공유하게 만드는 것이다.

## 현재 개발 초점

Flow는 아직 완성된 제품보다 설계와 구현을 강하게 맞춰 가는 단계에 있다. 현재 우선순위는 아래에 가깝다.

### Now

- 프로젝트 문서, 에픽 문서, 스펙 문서를 하나의 계층으로 정리한다.
- 변경 사항과 결정 사항을 독립 입력으로 다루는 모델을 문서와 구현 계약에 반영한다.
- Runner의 dispatch, review loop, retry, timeout 같은 실행 규칙을 deterministic하게 고정한다.

### Next

- Change Record의 저장 스키마와 impact/close 흐름을 core contract로 끌어내린다.
- Decision Record의 저장 스키마와 apply/resume 흐름을 core contract로 끌어내린다.
- Relevant Context Retrieval 규칙과 summary cache를 runtime 계약으로 끌어내린다.
- Decision Record 초안용 상황 분석 및 대안별 리스크/이득 보고서 생성 규칙을 agent contract로 끌어내린다.
- 가능한 spec에 대한 `liveExecution` 스키마와 playground/evidence 흐름을 설계한다.
- web에서 project overview, epic overview, spec stream을 하나의 workspace로 연결한다.
- spec과 code/test/evidence 사이의 닫힘 조건을 더 엄격하게 검증한다.

### Later

- 외부 채널과 운영 자동화를 같은 계약 위에서 확장한다.
- 장기 보관, 아카이브, 회고, 검색 같은 운영 기능을 강화한다.
- 더 실제적인 agent 품질과 장시간 실행 환경을 안정화한다.

## 관련 문서

- `docs/flow-project-document.md`: 상위 프로젝트 문서와 정보 구조
- `docs/flow-epic-plan.md`: 에픽 단위 운영 계획과 단계별 도입 방향
- `docs/flow-change-driven-spec-graph.md`: 변경 사항을 별도 입력으로 다루는 운영 모델
- `docs/flow-decision-record.md`: 사용자 결정 문제를 별도 입력으로 다루는 운영 모델
- `docs/flow-runner-agent-contract.md`: Runner와 agent 사이의 최소 런타임 계약
- `docs/flow-live-spec-execution.md`: 직접 실행하고 결과를 확인할 수 있는 살아있는 spec 방향
- `docs/flow-phase3-runner-decisions.md`: Runner orchestration의 기본 결정 사항
- `docs/flow-phase4-review-loop-decisions.md`: review request loop와 사용자 응답 처리 방향
- `docs/cli-hourly-iteration-guide.md`: 문서-구현-검증을 시간 단위로 다시 맞추는 운영 지침
