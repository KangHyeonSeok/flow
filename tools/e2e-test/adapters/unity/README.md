# Unity E2E 테스트 어댑터 가이드

## 개요

Flow E2E 테스트 도구와 Unity 앱을 연동하기 위한 가이드입니다.  
Unity 앱에 **HTTP 서버**와 **UDP 비콘**을 내장하여, 외부 테스트 도구가 시나리오를 주입하고 결과를 수집합니다.

## 아키텍처

```
┌──────────────────────────────────────────────────────┐
│  flow test e2e scenario.yaml                         │
│  ┌─────────────┐    ┌────────────┐    ┌───────────┐  │
│  │ UDP Listener │◄───│ E2EBeacon  │    │ Gemini    │  │
│  │ (port 51320) │    │ (Unity)    │    │ VLM 검증  │  │
│  └──────┬──────┘    └────────────┘    └─────▲─────┘  │
│         │                                   │        │
│  ┌──────▼──────┐    ┌────────────┐    ┌─────┴─────┐  │
│  │ HTTP Client │───►│ E2EServer  │───►│ Screenshot│  │
│  │             │◄───│ (Unity)    │    │ + Result  │  │
│  └─────────────┘    └────────────┘    └───────────┘  │
└──────────────────────────────────────────────────────┘
```

## 빠른 시작

### 1. 빌드 심볼 추가

Unity Editor:  
**Project Settings → Player → Other Settings → Scripting Define Symbols**

```
E2E_TESTS
```

> ⚠️ 릴리즈 빌드에서는 반드시 제거하세요.

### 2. 패키지 설치 (UPM)

Unity Package Manager에서 Git URL을 추가합니다:

```
https://github.com/KangHyeonSeok/flow.git?path=tools/e2e-test/adapters/unity
```

### 3. 씬 설정

빈 GameObject를 만들고 이름을 `E2ETestRunner`로 설정한 뒤:
- `E2EServer` 컴포넌트 추가
- `E2EBeacon` 컴포넌트 추가

```csharp
// 또는 코드에서 자동 생성
#if E2E_TESTS
var runner = new GameObject("E2ETestRunner");
runner.AddComponent<E2EServer>();
runner.AddComponent<E2EBeacon>();
DontDestroyOnLoad(runner);
#endif
```

### 4. 테스트 실행

```bash
# Unity 앱을 E2E 모드로 빌드 & 실행
# (Scripting Define Symbols에 E2E_TESTS 포함)

# 테스트 실행
flow test e2e scenarios/examples/unity_basic.yaml
```

---

## 컴포넌트 상세

### E2EBeacon (UDP 브로드캐스트)

| 항목 | 값 |
|------|-----|
| 파일 | `E2EBeacon.cs` |
| 프로토콜 | UDP 브로드캐스트 |
| 포트 | 51320 (고정) |
| 간격 | 1초 |
| 조건 | `E2E_TESTS` 심볼 정의 시에만 동작 |

**브로드캐스트 메시지 형식:**

```json
{
  "app": "my-unity-app",
  "platform": "unity",
  "port": 51321,
  "version": "1.0.0"
}
```

- `app`: 앱 이름 (Inspector에서 설정)
- `platform`: 항상 `"unity"`
- `port`: HTTP 서버 포트 (기본 51321)
- `version`: `Application.version`에서 자동 추출

### E2EServer (HTTP 서버)

| 항목 | 값 |
|------|-----|
| 파일 | `E2EServer.cs` |
| 프로토콜 | HTTP |
| 포트 | 51321 (기본, 변경 가능) |
| 조건 | `E2E_TESTS` 심볼 정의 시에만 동작 |

**엔드포인트:**

| 메서드 | 경로 | 설명 |
|--------|------|------|
| POST | `/e2e/run` | 테스트 시나리오 제출 |
| GET | `/e2e/status` | 실행 상태 조회 |
| GET | `/e2e/result` | 결과 + 스크린샷 수집 |
| GET | `/e2e/health` | 헬스체크 |

---

## HTTP API 상세

### POST /e2e/run

시나리오를 제출하고 테스트 실행을 시작합니다.

**요청:**
```json
{
  "meta": {
    "app": "flow-editor",
    "platform": "unity",
    "resolution": "1920x1080",
    "timeout": 300
  },
  "steps": [
    { "type": "input", "target": "#projectName", "text": "Test" },
    { "type": "click", "target": "#createButton" },
    { "type": "wait", "ms": 1000 },
    { "type": "screenshot", "target": "result" }
  ],
  "assertions": [
    { "type": "screenshot", "name": "result", "expected": "프로젝트가 생성된 화면" }
  ]
}
```

**응답 (200):**
```json
{
  "session_id": "e2e-abc123",
  "status": "running"
}
```

### GET /e2e/status

현재 테스트 실행 상태를 조회합니다.

**응답 (200):**
```json
{
  "status": "running",
  "progress": 0.6,
  "current_step": 3,
  "total_steps": 5
}
```

상태 값: `idle`, `running`, `completed`, `failed`

### GET /e2e/result

테스트 완료 후 결과를 수집합니다.

**응답 (200):**
```json
{
  "status": "completed",
  "screenshots": [
    {
      "name": "result",
      "data": "<base64 PNG>"
    }
  ],
  "logs": [
    { "timestamp": "2026-01-01T00:00:00Z", "level": "info", "message": "Clicked createButton" }
  ]
}
```

### GET /e2e/health

서버 상태를 확인합니다.

**응답 (200):**
```json
{
  "status": "ok",
  "app": "my-unity-app",
  "platform": "unity"
}
```

---

## 시나리오 스텝 구현 가이드

### input (텍스트 입력)

```csharp
case "input":
    var inputField = FindUI<TMP_InputField>(step.target);
    inputField.text = step.text;
    inputField.onValueChanged.Invoke(step.text);
    break;
```

### click (버튼 클릭)

```csharp
case "click":
    var button = FindUI<Button>(step.target);
    button.onClick.Invoke();
    break;
```

### select (드롭다운 선택)

```csharp
case "select":
    var dropdown = FindUI<TMP_Dropdown>(step.target);
    var idx = dropdown.options.FindIndex(o => o.text == step.value);
    dropdown.value = idx;
    break;
```

### wait (대기)

```csharp
case "wait":
    yield return new WaitForSeconds(step.ms / 1000f);
    break;
```

### screenshot (스크린샷)

```csharp
case "screenshot":
    yield return new WaitForEndOfFrame();
    var tex = ScreenCapture.CaptureScreenshotAsTexture();
    var png = tex.EncodeToPNG();
    _screenshots[step.target] = Convert.ToBase64String(png);
    Destroy(tex);
    break;
```

---

## UI 요소 탐색 규칙

시나리오의 `target` 필드는 다음 규칙으로 UI 요소를 찾습니다:

| 패턴 | 의미 | 예시 |
|------|------|------|
| `#name` | GameObject 이름으로 찾기 | `#createButton` |
| `.tag` | Tag로 찾기 | `.MainPanel` |
| `/path/to/obj` | 경로로 찾기 | `/Canvas/Panel/Button` |

**구현 예:**

```csharp
private T FindUI<T>(string target) where T : Component
{
    GameObject go = null;

    if (target.StartsWith("#"))
    {
        // 이름으로 찾기
        go = GameObject.Find(target[1..]) 
          ?? FindInactive(target[1..]);
    }
    else if (target.StartsWith("."))
    {
        // 태그로 찾기
        go = GameObject.FindWithTag(target[1..]);
    }
    else if (target.StartsWith("/"))
    {
        // 경로로 찾기
        go = GameObject.Find(target);
    }

    if (go == null)
        throw new System.Exception($"UI element not found: {target}");

    return go.GetComponent<T>()
        ?? throw new System.Exception($"Component {typeof(T).Name} not found on {target}");
}
```

---

## 커스텀 스텝 확장

기본 제공 스텝 외에 앱 고유의 스텝을 추가할 수 있습니다:

```csharp
// E2EServer.cs의 ExecuteStep에 커스텀 핸들러 등록
protected virtual IEnumerator ExecuteCustomStep(StepData step)
{
    switch (step.type)
    {
        case "drag":
            // 드래그 구현
            yield return DragUI(step.target, step.text);
            break;
        case "scroll":
            // 스크롤 구현
            yield return ScrollUI(step.target, float.Parse(step.value));
            break;
        default:
            AddLog("warn", $"Unknown step type: {step.type}");
            break;
    }
}
```

---

## 빌드 설정

### Development Build (권장)

```
Unity Editor → Build Settings
  ✅ Development Build
  ✅ Script Debugging

Player Settings → Scripting Define Symbols
  E2E_TESTS
```

### 커맨드라인 빌드

```bash
Unity.exe -batchmode -nographics \
  -projectPath ./MyProject \
  -buildTarget Win64 \
  -executeMethod BuildScript.BuildE2E \
  -quit
```

```csharp
// BuildScript.cs
public static void BuildE2E()
{
    PlayerSettings.SetScriptingDefineSymbols(
        NamedBuildTarget.Standalone,
        "E2E_TESTS"
    );

    BuildPipeline.BuildPlayer(new BuildPlayerOptions
    {
        scenes = new[] { "Assets/Scenes/Main.unity" },
        locationPathName = "Build/E2E/App.exe",
        target = BuildTarget.StandaloneWindows64,
        options = BuildOptions.Development
    });
}
```

---

## 트러블슈팅

| 증상 | 원인 | 해결 |
|------|------|------|
| `No target app found` | UDP 비콘 미작동 | `E2E_TESTS` 심볼 확인, 방화벽 UDP 51320 허용 |
| `Connection refused` | HTTP 서버 미시작 | E2EServer 컴포넌트가 씬에 있는지 확인 |
| `Timeout polling status` | 시나리오 실행 지연 | `--timeout` 옵션 증가 |
| `Screenshot empty` | WaitForEndOfFrame 미대기 | 코루틴에서 `yield return new WaitForEndOfFrame()` 확인 |
| `UI element not found` | target 이름 불일치 | Hierarchy에서 정확한 이름 확인, `#` 접두사 사용 |

---

## 참고

- [전체 E2E 테스트 설계](../../../docs/e2e.md)
- [시나리오 YAML 스키마](../../scenarios/schema.yaml)
- [예제 시나리오](../../scenarios/examples/)
