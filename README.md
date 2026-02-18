# flow

작업 흐름을 agent로 정의

## 개요

- `flow.design`: 사용자의 요구사항을 분석해 백로그를 만드는 agent
- `flow`: 백로그를 가져와서 백로그가 모두 해결 될때 까지 구현하는 agent

## 사용 원칙

- 모델에 따라 잘 동작할 때도 있고 그렇지 않을 때도 있습니다.
- 한 채팅 세션에서 한 번이라도 사용했다면 `flow`를 따르려는 경향이 있으므로,
	premium request를 소비하지 말고 **새 채팅 세션을 열어** 진행하세요.
- 백로그를 충분히 쌓고, 계획을 천천히 리뷰한 다음 **Opus로 최대한 많은 백로그를 처리**하는 것이
	premium request를 아끼는 방법이라고 생각했습니다.

## 프로세스

설계 → 설계 리뷰 → 구현 → 검증 → 완료 보고

- 설계 리뷰는 **반드시 사람이 수행**합니다.

## 설치

### Windows (PowerShell)

```powershell
irm https://raw.githubusercontent.com/KangHyeonSeok/flow/main/install.ps1 | iex
```

### macOS / Linux

```bash
curl -fsSL https://raw.githubusercontent.com/KangHyeonSeok/flow/main/install.sh | bash
```

### 설치 완료 메시지

```
═══════════════════════════════════════
  ✅ Flow Prompt v0.1.0 installed
═══════════════════════════════════════
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

### 이미 최신 버전인 경우

```
═══════════════════════════════════════
  ✅ Already up to date (v0.1.0)
═══════════════════════════════════════
```

## Capture 사용법

`flow.ps1 capture`는 화면/윈도우 캡처를 수행합니다.

```powershell
# 열려있는 윈도우 목록 확인
.\flow.ps1 capture list-windows

# 1번 모니터 캡처
.\flow.ps1 capture monitor --index 0 --output .\.flow\tmp\monitor.png

# 프로세스 이름으로 윈도우 캡처 (제목에 공백/특수문자가 있을 때 권장)
.\flow.ps1 capture window --process Notepad --output .\.flow\tmp\notepad.png
```

## VLM 사용법

`flow.ps1 vlm`은 `.flow/bin/gemini_vlm.py`를 실행해 Gemini VLM 검증을 수행합니다.
이미지는 최대 3개까지 전달할 수 있습니다.

사전 준비:

- `GEMINI_API_KEY` 환경변수 설정 또는 `~/.flow/env`에 `GEMINI_API_KEY=...` 저장
- Python 3.12+ 설치
- `install.ps1`/`update.ps1` 또는 `install.sh`/`update.sh` 실행 시 `.venv`와 `google-genai`, `pillow` 자동 설치

```powershell
# 단일 이미지 검증
.\flow.ps1 vlm --image .\image1.png --expected "이미지가 어두운 테마의 에디터 화면인지 확인"

# 다중 이미지(최대 3개) 비교/질의
.\flow.ps1 vlm `
	--image .\.flow\tmp\monitor.png `
	--image .\.flow\tmp\notepad.png `
	--expected "두 이미지의 차이를 설명해줘."
```
