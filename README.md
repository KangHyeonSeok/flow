# flow

Flow는 스펙 기반 개발 워크플로를 정의하고 실행하는 통합 도구 모음이다. CLI, runner, webservice, VS Code 확장, 빌드/테스트 도구, RAG, 캡처 보조 도구가 같은 스펙 저장 계약과 상태 규칙 위에서 동작한다.

핵심 목표는 아래 세 가지다.

1. 스펙을 작성하고 상태와 의존성을 관리한다.
2. 구현, 테스트, 검토, evidence 수집을 같은 흐름 안에서 실행한다.
3. runner, webservice, Slack 같은 입력 채널이 모두 같은 core contract를 사용하게 한다.

## 설치

### Windows (PowerShell)

```powershell
irm https://raw.githubusercontent.com/KangHyeonSeok/flow/main/install.ps1 | iex
```

### macOS / Linux

```bash
curl -fsSL https://raw.githubusercontent.com/KangHyeonSeok/flow/main/install.sh | bash
```

## 업데이트

### Windows (PowerShell)

```powershell
irm https://raw.githubusercontent.com/KangHyeonSeok/flow/main/update.ps1 | iex
```

### macOS / Linux

```bash
curl -fsSL https://raw.githubusercontent.com/KangHyeonSeok/flow/main/update.sh | bash
```

## 제품 구성

- spec graph: 스펙 생성, 조회, 검증, 영향 분석, 상태 전파, 코드 참조 검증
- flow core: 상태 규칙, review request lifecycle, activity log, assignment, 저장소 contract
- runner: spec dispatch, agent orchestration, timeout recovery, review loop, 재시도 관리
- webservice: notebook-style 통합 workspace, kanban/review/failure projection, section 단위 저장
- build/test: unity, python, node, dotnet, flutter 프로젝트 감지와 실행
- e2e: 시나리오 기반 end-to-end 테스트 실행
- rag db: 작업 기록과 문서를 적재하고 검색
- capture/vlm: 화면 캡처와 시각 확인 자동화
- vscode extension: Flow 작업을 에디터 안에서 이어가는 보조 UI

스펙은 로컬 `docs/specs` 또는 연결된 스펙 저장소를 기준으로 읽고, runner는 `.flow/spec-cache`를 활용한다.

## 주요 명령

### 스펙 관리

```powershell
# 스펙 저장소 초기화
.\flow.ps1 spec-init

# 스펙 생성
.\flow.ps1 spec-create --title "새 기능" --parent F-001 --tags "core,cli"

# 스펙 목록 / 조회
.\flow.ps1 spec-list --pretty
.\flow.ps1 spec-get F-010

# review JSON 반영
.\flow.ps1 spec-append-review F-010 --input-file .\.flow\review\F-010-review.json --reviewer runner-01

# 검증 / 그래프 / 영향 분석
.\flow.ps1 spec-validate --strict
.\flow.ps1 spec-graph --tree
.\flow.ps1 spec-impact F-010
```

추가로 `spec-propagate`, `spec-check-refs`, `spec-order`, `spec-backup`, `spec-restore`, `spec-push`, `spec-index`를 지원한다.

### 빌드 실행

`build` 명령은 프로젝트를 감지해 `unity`, `python`, `node`, `dotnet`, `flutter` 흐름으로 연결한다.

```powershell
.\flow.ps1 build
.\flow.ps1 build . --all
.\flow.ps1 build . --platform dotnet --test
```

### E2E 테스트

E2E 러너는 `flutter`, `unity` 어댑터를 대상으로 시나리오 실행을 지원한다.

```powershell
.\flow.ps1 test e2e .\tools\e2e-test\scenarios\calculator --platform flutter
```

### Runner

Runner는 스펙 저장소와 설정을 읽어 작업 대상을 찾고, 상태와 로그를 관리한다.

```powershell
.\flow.ps1 config
.\flow.ps1 config --spec-repo https://github.com/user/flow-spec.git

.\flow.ps1 runner-start --once
.\flow.ps1 runner-status
.\flow.ps1 runner-logs --tail 50
.\flow.ps1 runner-stop
```

macOS/Linux에서는 PowerShell 없이 루트 Bash 래퍼를 사용할 수 있다.

```bash
./flow config
./flow runner-start --daemon
./flow runner-status
./flow runner-logs --tail 50
```

### RAG DB

작업 기록이나 문서를 적재하고 검색할 수 있다.

```powershell
.\flow.ps1 db-add --content "스펙 그래프 구현" --feature "spec-graph" --tags "spec,graph"
.\flow.ps1 db-query --query "spec graph" --top 5 --pretty
```

### Capture / VLM

루트 스크립트는 별도 바이너리와 Python 스크립트를 감싸서 캡처와 VLM 확인을 실행한다.

```powershell
.\flow.ps1 capture list-windows
.\flow.ps1 capture monitor --index 0 --output .\.flow\tmp\monitor.png

.\flow.ps1 vlm --image .\image1.png --expected "이미지가 에디터 화면인지 확인"
```

VLM 사용 시 `GEMINI_API_KEY`와 Python 환경이 필요하다. 설치/업데이트 스크립트는 `.venv`와 관련 패키지 설치를 함께 처리한다.

### Webservice

webservice는 spec 문서 뷰어, 칸반, 검토 요청함을 분리된 화면으로 두지 않고 하나의 통합 workspace로 제공한다.

- 기본 화면은 collapsed spec stream이다.
- spec을 펼치면 summary, goal/context, acceptance criteria, dependencies, assignments, review requests, tests/evidence, activity timeline, actions 섹션이 열린다.
- kanban, review-only, failure-hold view는 별도 데이터 구조가 아니라 같은 spec stream의 projection mode다.
- 상태 변경 버튼은 직접 mutation이 아니라 event proposal 또는 command request를 생성한다.
- section 저장은 baseVersion을 사용한 optimistic concurrency를 전제로 한다.

### Slack / 외부 채널

Slack 같은 외부 채널은 별도 상태 전이 규칙을 갖지 않는다. review response, spec 생성 요청, 운영 action은 모두 같은 core API contract로 변환되어 처리된다.

## 저장소 구성

- `docs`: 운영 정책, 상태 규칙, 아키텍처 문서
- `tools/flow-cli`: 주 CLI 구현
- `tools/flow-core`: 공용 도메인, 상태 규칙, 저장/runner contract
- `tools/flow-cli.Tests`: CLI 및 runner 테스트
- `tools/e2e-test`: Python 기반 E2E 러너
- `tools/capture-cli`: 캡처 도구
- `tools/embed`: 임베딩 보조 도구
- `tools/flow-ext`: VS Code 확장
- `tools/flow-kanban`: webservice/kanban 관련 구현 자산

## 운영 원칙

- 상태 전이는 rule evaluator와 spec manager를 통해서만 일어난다.
- UI와 외부 채널은 상태를 직접 수정하지 않고 event proposal 또는 command request만 생성한다.
- review request, assignment, activity log는 append-only 운영 이력을 우선한다.
- webservice는 thin layer로 유지하고 저장/판정 로직은 core에 둔다.
- runner, webservice, Slack은 같은 review contract와 spec storage contract를 공유한다.

## 관련 스크립트

- `build-flow.ps1`: Flow CLI 빌드
- `build-flow.sh`: Flow CLI 빌드 (macOS/Linux)
- `build-capture.ps1`: 캡처 도구 빌드
- `build-capture.sh`: 캡처 도구 빌드 (win-x64 대상)
- `build-embed.ps1`: 임베딩 도구 빌드
- `build-embed.sh`: 임베딩 도구 빌드 (기본 `win-x64`)
- `install.ps1`, `install.sh`: 설치
- `update.ps1`, `update.sh`: 업데이트
