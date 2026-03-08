# flow

스펙 기반 개발 워크플로를 다루는 CLI와 보조 도구 모음입니다.

현재 저장소에서 확인되는 구현 축은 다음과 같습니다.

- 스펙 생성, 조회, 검증, 그래프 분석
- 프로젝트 빌드/린트/테스트 실행
- 시나리오 기반 E2E 테스트 실행
- Runner를 통한 스펙 처리 자동화
- 문서 적재/검색용 RAG DB
- 화면 캡처와 VLM 확인 보조 도구
- VS Code 확장 개발 코드

README는 구현된 명령과 스크립트 기준으로만 정리합니다.

운영 정책과 거버넌스 문서는 [docs/hidden-spec-policy.md](docs/hidden-spec-policy.md)에서 관리합니다.

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

## 핵심 흐름

Flow는 대체로 세 가지 흐름으로 사용됩니다.

1. 스펙을 만들고 상태를 관리한다.
2. 빌드, 테스트, 캡처 같은 실행 도구를 붙인다.
3. Runner나 VS Code 확장에서 스펙 기반 작업을 이어간다.

스펙은 로컬 `docs/specs` 또는 연결된 스펙 저장소를 기준으로 읽고, Runner는 `.flow/spec-cache`를 활용합니다.

<img width="1667" height="1098" alt="image" src="https://github.com/user-attachments/assets/dae05560-7f32-46d7-93ff-287660cb24ee" />


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

추가로 `spec-propagate`, `spec-check-refs`, `spec-order`, `spec-backup`, `spec-restore`, `spec-push`, `spec-index`가 구현되어 있습니다.

### 빌드 실행

`build` 명령은 프로젝트를 감지해 `unity`, `python`, `node`, `dotnet`, `flutter` 흐름으로 연결합니다.

```powershell
.\flow.ps1 build
.\flow.ps1 build . --all
.\flow.ps1 build . --platform dotnet --test
```

### E2E 테스트

현재 E2E 러너는 `flutter`, `unity` 어댑터를 대상으로 시나리오 실행을 지원합니다.

```powershell
.\flow.ps1 test e2e .\tools\e2e-test\scenarios\calculator --platform flutter
```

### Runner

Runner는 스펙 저장소와 설정을 읽어 작업 대상을 찾고, 상태와 로그를 관리합니다.

```powershell
.\flow.ps1 config
.\flow.ps1 config --spec-repo https://github.com/user/flow-spec.git

.\flow.ps1 runner-start --once
.\flow.ps1 runner-status
.\flow.ps1 runner-logs --tail 50
.\flow.ps1 runner-stop
```

macOS/Linux에서는 PowerShell 없이 루트 Bash 래퍼를 사용할 수 있습니다.

```bash
./flow config
./flow runner-start --daemon
./flow runner-status
./flow runner-logs --tail 50
```

### RAG DB

작업 기록이나 문서를 적재하고 검색할 수 있습니다.

```powershell
.\flow.ps1 db-add --content "스펙 그래프 구현" --feature "spec-graph" --tags "spec,graph"
.\flow.ps1 db-query --query "spec graph" --top 5 --pretty
```

### Capture / VLM

루트 스크립트는 별도 바이너리와 Python 스크립트를 감싸서 캡처와 VLM 확인을 실행합니다.

```powershell
.\flow.ps1 capture list-windows
.\flow.ps1 capture monitor --index 0 --output .\.flow\tmp\monitor.png

.\flow.ps1 vlm --image .\image1.png --expected "이미지가 에디터 화면인지 확인"
```

VLM 사용 시 `GEMINI_API_KEY`와 Python 환경이 필요합니다. 설치/업데이트 스크립트는 `.venv`와 관련 패키지 설치를 함께 처리합니다.

## 저장소 구성

- `docs`: 운영 정책 및 거버넌스 문서
- `tools/flow-cli`: 주 CLI 구현
- `tools/flow-cli.Tests`: CLI 테스트
- `tools/e2e-test`: Python 기반 E2E 러너
- `tools/capture-cli`: 캡처 도구
- `tools/embed`: 임베딩 보조 도구
- `tools/flow-ext`: VS Code 확장
- `scripts/hooks`: 세션 시작/종료 훅

## 개발 메모

- `flow.ps1`는 플랫폼에 맞는 실행 파일을 선택해 `.flow/bin` 아래 바이너리를 호출합니다.
- `build-flow.sh`는 현재 셸 환경에 맞는 RID를 기본 선택해 Flow CLI를 `.flow/bin`에 빌드합니다.
- `tools/flow-cli` 수정 후 실제 런타임 반영이 필요하면 `build-flow.ps1` 또는 해당 프로젝트 빌드를 다시 해야 합니다.
- VS Code 확장은 `tools/flow-ext`에서 별도로 빌드합니다.

## 관련 스크립트

- `build-flow.ps1`: Flow CLI 빌드
- `build-flow.sh`: Flow CLI 빌드 (macOS/Linux)
- `build-flow-ext.ps1`: VS Code 확장 빌드
- `build-flow-ext.sh`: VS Code 확장 빌드 및 VSIX 설치 시도 (macOS/Linux)
- `build-capture.ps1`: 캡처 도구 빌드
- `build-capture.sh`: 캡처 도구 빌드 (win-x64 대상)
- `build-embed.ps1`: 임베딩 도구 빌드
- `build-embed.sh`: 임베딩 도구 빌드 (기본 `win-x64`)
- `install.ps1`, `install.sh`: 설치
- `update.ps1`, `update.sh`: 업데이트
