---
name: flow-build
description: 멀티 플랫폼 빌드 시스템. flow build 커맨드를 사용하여 프로젝트를 빌드, 린트, 테스트, 실행한다. unity, python, node, dotnet, flutter 플랫폼을 자동 감지하여 지원한다. 빌드, 린트, 테스트가 필요할 때 사용.
---

# Flow Build

`flow build` 커맨드로 멀티 플랫폼 프로젝트를 빌드/린트/테스트/실행한다.

## 기본 사용법

```bash
# 자동 감지 + 빌드
./flow.ps1 build

# 특정 프로젝트 경로
./flow.ps1 build ./examples/flutter_calculator

# 전체 파이프라인 (lint + build + test)
./flow.ps1 build --all

# 개별 단계
./flow.ps1 build --lint
./flow.ps1 build --test
./flow.ps1 build --run

# 플랫폼 명시
./flow.ps1 build --platform flutter
./flow.ps1 build --platform unity
./flow.ps1 build --platform dotnet

# 타임아웃 설정 (초)
./flow.ps1 build --timeout 600
```

## 지원 플랫폼

| 플랫폼 | 감지 기준 | 빌드 모듈 위치 |
|--------|----------|--------------|
| flutter | `pubspec.yaml` | `.flow/build/flutter/` |
| unity | `ProjectSettings/` | `.flow/build/unity/` |
| dotnet | `*.csproj`, `*.sln` | `.flow/build/dotnet/` |
| python | `setup.py`, `pyproject.toml` | `.flow/build/python/` |
| node | `package.json` | `.flow/build/node/` |

## 출력 형식

JSON 출력. `--pretty` 옵션으로 가독성 높은 출력.

```json
{
  "success": true,
  "command": "build",
  "data": {
    "platform": "flutter",
    "project_path": "/path/to/project",
    "total_duration_ms": 12345,
    "steps": [
      { "action": "build", "success": true, "duration_ms": 10000 }
    ]
  }
}
```

## 빌드 모듈

각 플랫폼의 빌드 모듈은 `.flow/build/{platform}/manifest.json`에 정의된다.
`BuildModuleManager`가 모듈을 로드하고, `BuildOrchestrator`가 순서대로 실행한다.
