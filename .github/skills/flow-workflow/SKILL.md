---
name: flow-workflow
description: Flow 프로젝트 개발 워크플로우 가이드. 기능 설계, 작업 분해, 구현, 검증의 전체 흐름을 안내한다. 새 기능 개발, 설계 진행, 작업 계획 수립이 필요할 때 사용.
---

# Flow Workflow

Flow 프로젝트에서 기능을 설계하고 구현하는 워크플로우.

## 전체 흐름

1. **컨텍스트 수집** → 프로젝트 구조 및 기존 패턴 탐색
2. **과거 사례 조회** → `flow db-query`로 유사 작업 참조
3. **설계** → 요구사항 분석 및 설계 문서 작성
4. **사용자 리뷰** → `flow human-input`으로 승인 획득
5. **작업 분해** → 독립적 태스크로 분리
6. **구현** → 태스크별 구현
7. **검증** → 빌드/테스트/스펙 검증
8. **기록** → `flow db-add`로 결과 저장

## 1. 컨텍스트 수집

프로젝트 구조와 기존 코드 패턴을 탐색한다.

```
- 관련 디렉토리 구조 확인
- 기존 코드 스타일/패턴 파악
- 영향받는 모듈 식별
```

## 2. 과거 사례 조회

```bash
./flow.ps1 db-query --query "관련 키워드" --tags "관련태그"
./flow.ps1 db-query --query "유사 기능" --plan --result
```

과거 작업의 plan/result를 참조하여 패턴과 교훈을 반영한다.

## 3. 설계

요구사항을 분석하고 설계 문서를 작성한다.

핵심 내용:
- 목표 및 범위
- 기술 설계 (아키텍처, 데이터 모델, API)
- 영향 분석 (`flow spec-impact` 활용)
- 테스트 전략

## 4. 사용자 리뷰

```bash
# 확인 요청
./flow.ps1 human-input --type confirm --prompt "설계를 승인하시겠습니까?"

# 선택지 제시
./flow.ps1 human-input --type select --prompt "접근 방식 선택" --options "A안" --options "B안"

# 자유 입력
./flow.ps1 human-input --type text --prompt "추가 요구사항이 있으신가요?"
```

요구사항이 있으면 설계에 반영 후 다시 리뷰 요청.

## 5. 작업 분해

설계를 독립적 태스크로 나눈다. 각 태스크별로:
- `flow db-query`로 유사 과거 청크 참조
- plan.md 작성

## 6. 구현

태스크별로 순차 구현. 각 태스크 완료 후 빌드/테스트 확인.

## 7. 검증

```bash
# 빌드 확인
./flow.ps1 build --all

# 스펙 검증
./flow.ps1 spec-validate --strict
./flow.ps1 spec-check-refs --strict

# E2E 테스트 (해당 시)
./flow.ps1 test e2e scenarios/target.yaml
```

## 8. 기록

```bash
./flow.ps1 db-add --content "작업 요약" --tags "관련,태그" --commit-id "$(git rev-parse HEAD)"
```

## 스펙 관리

기능 변경 시 스펙 업데이트:

```bash
# 영향 분석
./flow.ps1 spec-impact F-010

# 스펙 상태 전파
./flow.ps1 spec-propagate F-010 --status needs-review --apply

# 검증
./flow.ps1 spec-validate --strict
./flow.ps1 spec-check-refs --strict
```
