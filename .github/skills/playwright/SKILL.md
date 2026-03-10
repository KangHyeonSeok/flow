---
name: playwright
description: Playwright CLI로 브라우저 스크린샷을 캡처한다. 로컬 HTML 파일이나 URL을 headless 브라우저에서 열고 PNG로 저장한다. UI 렌더링 검증, 시각적 회귀 테스트에 사용.
---

# Playwright Screenshot

`npx playwright screenshot` 으로 브라우저 스크린샷을 캡처한다.

## 기본 사용법

```bash
# URL 스크린샷
npx playwright screenshot https://example.com output.png

# 로컬 HTML 파일 (file:// 프로토콜 사용)
npx playwright screenshot "file:///D:/Projects/flow/.flow/test.html" output.png

# 풀 페이지 스크린샷
npx playwright screenshot --full-page "file:///D:/Projects/flow/test.html" output.png
```

## 주요 옵션

```bash
# 브라우저 선택 (기본: chromium)
npx playwright screenshot -b firefox <url> output.png
npx playwright screenshot -b webkit <url> output.png

# 뷰포트 크기 지정
npx playwright screenshot --viewport-size "1280,720" <url> output.png

# 특정 요소가 나타날 때까지 대기 후 캡처
npx playwright screenshot --wait-for-selector ".activity-section" <url> output.png

# 밀리초 대기 후 캡처
npx playwright screenshot --wait-for-timeout 2000 <url> output.png

# 다크 모드
npx playwright screenshot --color-scheme dark <url> output.png

# 디바이스 에뮬레이션
npx playwright screenshot --device "iPhone 15" <url> output.png
```

## UI 검증 패턴

HTML 렌더링 결과를 스크린샷으로 찍고 시각적으로 검증하는 흐름:

```bash
# 1. 테스트용 HTML 생성 (구현 코드의 렌더 함수 호출 결과)
# 2. 스크린샷 캡처
npx playwright screenshot --full-page "file:///absolute/path/to/test.html" evidence/screenshot.png

# 3. 캡처된 이미지를 확인하여 검증
```

## 로컬 파일 경로 규칙

- Windows: `file:///D:/path/to/file.html`
- Linux/macOS: `file:///home/user/path/to/file.html`
- 상대 경로 불가, 반드시 절대 경로 사용

## 설치

별도 설치 불필요. `npx playwright` 로 자동 다운로드된다.
브라우저가 없으면 `npx playwright install chromium` 으로 설치.
