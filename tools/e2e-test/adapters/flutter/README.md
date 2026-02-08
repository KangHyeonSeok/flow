# Flutter E2E 테스트 어댑터 가이드

## 개요

Flow E2E 테스트 도구와 Flutter 앱을 연동하기 위한 가이드입니다.  
Flutter 앱에 **HTTP 서버**와 **UDP 비콘**을 내장하여, 외부 테스트 도구가 시나리오를 주입하고 결과를 수집합니다.

## 아키텍처

```
┌──────────────────────────────────────────────────────┐
│  flow test e2e scenario.yaml                         │
│  ┌─────────────┐    ┌────────────┐    ┌───────────┐  │
│  │ UDP Listener │◄───│ E2EBeacon  │    │ Gemini    │  │
│  │ (port 51320) │    │ (Flutter)  │    │ VLM 검증  │  │
│  └──────┬──────┘    └────────────┘    └─────▲─────┘  │
│         │                                   │        │
│  ┌──────▼──────┐    ┌────────────┐    ┌─────┴─────┐  │
│  │ HTTP Client │───►│ E2EServer  │───►│ Screenshot│  │
│  │             │◄───│ (Flutter)  │    │ + Result  │  │
│  └─────────────┘    └────────────┘    └───────────┘  │
└──────────────────────────────────────────────────────┘
```

## 빠른 시작

### 1. 의존성 추가

`pubspec.yaml`에 다음 패키지를 추가합니다:

```yaml
dependencies:
  flutter:
    sdk: flutter
  shelf: ^1.4.2
```

### 2. E2E 코드 통합

프로젝트에 `lib/e2e/` 디렉토리를 만들고 다음 파일을 추가합니다:

| 파일 | 역할 |
|------|------|
| `e2e_beacon.dart` | UDP 브로드캐스트 (포트 51320) |
| `e2e_server.dart` | HTTP 서버 (포트 51321) |
| `scenario_executor.dart` | 시나리오 스텝 실행 엔진 |
| `e2e_wrapper.dart` | RepaintBoundary 래퍼 + 서비스 시작 |

`main.dart`에 조건부 컴파일을 적용합니다:

```dart
import 'e2e/e2e_wrapper.dart';

const kE2ETests = bool.fromEnvironment('E2E_TESTS');

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  
  Widget app = const MyApp();

  if (kE2ETests) {
    app = E2EWrapper(child: app);
  }

  runApp(app);
}
```

### 3. 테스트 실행

```bash
# Flutter 앱을 E2E 모드로 실행
flutter run -d windows --dart-define=E2E_TESTS=true

# 테스트 실행
flow test e2e scenarios/calculator/basic_addition.yaml --pretty
```

---

## 컴포넌트 상세

### E2EBeacon (UDP 브로드캐스트)

| 항목 | 값 |
|------|-----|
| 파일 | `lib/e2e/e2e_beacon.dart` |
| 프로토콜 | UDP 브로드캐스트 |
| 포트 | 51320 (고정) |
| 간격 | 1초 |
| 조건 | `--dart-define=E2E_TESTS=true` 시에만 동작 |

**브로드캐스트 메시지 형식:**

```json
{
  "app": "flutter-calculator",
  "platform": "flutter",
  "port": 51321,
  "version": "1.0.0"
}
```

### E2EServer (HTTP 서버)

| 항목 | 값 |
|------|-----|
| 파일 | `lib/e2e/e2e_server.dart` |
| 프로토콜 | HTTP (dart:io HttpServer + shelf) |
| 포트 | 51321 (기본) |
| 조건 | `E2EWrapper`에 의해 자동 시작 |

### ScenarioExecutor (시나리오 실행 엔진)

| 항목 | 값 |
|------|-----|
| 파일 | `lib/e2e/scenario_executor.dart` |
| Widget 탐색 | `ValueKey<String>` 기반 |
| 스크린샷 | `RepaintBoundary.toImage()` → PNG → base64 |

### E2EWrapper (래퍼 위젯)

| 항목 | 값 |
|------|-----|
| 파일 | `lib/e2e/e2e_wrapper.dart` |
| 역할 | RepaintBoundary 래핑, E2EServer + E2EBeacon 시작 |
| StatefulWidget | initState()에서 서비스 시작, dispose()에서 정리 |

---

## HTTP API 상세

### POST /e2e/run

시나리오를 제출하여 테스트를 시작합니다.

**요청:**
```json
{
  "scenario": {
    "steps": [
      {"type": "click", "target": "btn_7"},
      {"type": "click", "target": "btn_eq"},
      {"type": "wait", "ms": 500},
      {"type": "screenshot", "target": "result"}
    ]
  }
}
```

**응답:**
```json
{
  "session_id": "e2e-1707321600000",
  "status": "running"
}
```

### GET /e2e/status/{session_id}

테스트 실행 상태를 조회합니다.

**응답:**
```json
{
  "status": "running",
  "progress": 0.5,
  "current_step": 3,
  "total_steps": 6
}
```

| status | 설명 |
|--------|------|
| `idle` | 테스트 대기 중 |
| `running` | 테스트 실행 중 |
| `completed` | 테스트 완료 |
| `failed` | 테스트 실패 |

### GET /e2e/result/{session_id}

테스트 결과를 수집합니다.

**응답:**
```json
{
  "status": "completed",
  "screenshots": [
    {
      "name": "result",
      "data": "iVBORw0KGgo..."
    }
  ],
  "logs": [
    {
      "timestamp": "2026-02-08T08:01:46.650Z",
      "level": "info",
      "message": "Clicked btn_7"
    }
  ]
}
```

### GET /e2e/health

서버 상태 확인.

**응답:**
```json
{
  "status": "ok",
  "app": "flutter-calculator",
  "platform": "flutter"
}
```

---

## 시나리오 스텝 구현 가이드

### click

`ValueKey<String>`로 Widget을 탐색하여 `onPressed` / `onTap`을 호출합니다.

```yaml
- type: click
  target: "btn_7"
```

**탐색 순서:** Widget 트리를 순회하여 `ValueKey<String>(target)`을 가진 Element를 찾은 뒤, 하위 트리에서 `ElevatedButton`, `GestureDetector`, `InkWell` 등 탭 가능한 위젯의 콜백을 호출합니다.

### wait

지정된 시간(밀리초) 동안 대기합니다.

```yaml
- type: wait
  ms: 500
```

### screenshot

현재 화면을 캡처하여 base64 PNG로 저장합니다.

```yaml
- type: screenshot
  target: "result"
```

> `target`은 스크린샷 이름입니다. YAML assert 섹션의 `name`과 일치해야 합니다.

### input

텍스트 입력 필드에 값을 설정합니다.

```yaml
- type: input
  target: "input_field"
  text: "Hello"
```

---

## Widget 탐색 규칙 (ValueKey)

E2E 어댑터는 `ValueKey<String>`을 사용하여 Widget을 식별합니다.

### 규칙

1. 테스트 대상 위젯에 `ValueKey<String>`을 설정합니다
2. YAML 시나리오의 `target` 값과 동일한 문자열을 사용합니다
3. 네이밍 규칙: `{위젯유형}_{식별자}` (예: `btn_7`, `display`, `input_name`)

### 예시

```dart
// 버튼
ElevatedButton(
  key: const ValueKey<String>('btn_7'),
  onPressed: () => logic.onNumberPressed('7'),
  child: const Text('7'),
)

// 디스플레이
Text(
  value,
  key: const ValueKey<String>('display'),
)
```

---

## 빌드 설정

### Development Build (E2E 활성화)

```bash
flutter run -d windows --dart-define=E2E_TESTS=true
```

### Release Build (E2E 비활성화)

```bash
flutter build windows
```

> ⚠️ `--dart-define=E2E_TESTS=true` 없이 빌드하면 E2E 코드가 자동으로 비활성화됩니다.
> `bool.fromEnvironment('E2E_TESTS')`는 기본값 `false`이므로 릴리즈 빌드에 E2E 코드가 포함되지 않습니다.

---

## 트러블슈팅

| 증상 | 원인 | 해결 |
|------|------|------|
| No target app found | UDP 브로드캐스트 미동작 | `--dart-define=E2E_TESTS=true` 확인 |
| Connection refused | HTTP 서버 미시작 | 포트 51321 사용 중 여부 확인, 방화벽 확인 |
| Widget not found | ValueKey 불일치 | `target` 값과 코드의 `ValueKey` 문자열 일치 확인 |
| Screenshot empty | RepaintBoundary 없음 | `E2EWrapper`로 앱을 감싸고 있는지 확인 |
| VLM validation failed | API 키 없음 | `GEMINI_API_KEY` 환경변수 설정 |
| Pillow not installed | Python 패키지 누락 | `pip install pillow` 실행 |
| google.generativeai 없음 | Python 패키지 누락 | `pip install google-generativeai` 실행 |

---

## 참고

- **Unity 어댑터 가이드**: `tools/e2e-test/adapters/unity/README.md`
- **스키마 정의**: `tools/e2e-test/scenarios/schema.yaml`
- **설계 문서**: `docs/flow/implements/designs/Flutter_계산기_E2E_검증.md`
