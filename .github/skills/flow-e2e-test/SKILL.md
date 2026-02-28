---
name: flow-e2e-test
description: E2E 테스트 실행. flow test e2e 커맨드로 시나리오 기반 E2E 테스트를 실행한다. Flutter, Unity 등 플랫폼의 자동화된 E2E 테스트가 필요할 때 사용.
---

# Flow E2E Test

Python 기반 시나리오 E2E 테스트 실행기.

## 사용법

```bash
# 기본 실행
./flow.ps1 test e2e scenarios/basic.yaml

# 타임아웃 설정
./flow.ps1 test e2e scenarios/basic.yaml --timeout 600

# 재시도 횟수
./flow.ps1 test e2e scenarios/basic.yaml --retry 5

# 플랫폼 지정
./flow.ps1 test e2e scenarios/flutter_test.yaml --platform flutter

# 보고서 저장
./flow.ps1 test e2e scenarios/basic.yaml --save-report
```

## 시나리오 파일

YAML 형식. `tools/e2e-test/scenarios/` 디렉토리에 위치.

## 요구사항

- Python 3.12+
- `tools/e2e-test/` 디렉토리의 e2e_test 모듈
- `.venv` 가상환경 자동 사용 (있는 경우)

## 출력 형식

JSON 출력. Python 테스트 도구가 flow-format JSON을 생성하면 그대로 전달.
