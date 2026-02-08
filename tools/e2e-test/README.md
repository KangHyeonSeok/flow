# E2E Test Tool

Flow 프로젝트를 위한 End-to-End 테스트 자동화 툴입니다.

## 요구사항

- Python 3.12.x

## 설치

```bash
# 자동 환경 설정
python -m e2e_test.installer.venv_manager --setup
```

## 사용법

```bash
flow test e2e scenario.yaml
```

## 구조

```
e2e_test/
├── installer/       # Python 환경 관리
├── config/          # 설정
├── scenario/        # YAML 시나리오 파싱
├── discovery/       # UDP 디스커버리
├── transport/       # HTTP 통신
├── runner/          # 테스트 실행기
├── validators/      # VLM 검증
├── reporting/       # 리포트 생성
└── utils/           # 유틸리티
```
