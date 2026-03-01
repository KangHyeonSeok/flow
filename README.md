# flow

스펙 기반 개발 도구

## 개요

사용자가 스펙을 정의 하면 스펙을 기반으로 AI가 구현
그 것을 위한 필요한 도구를 여기에 모두 포함한다.

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
