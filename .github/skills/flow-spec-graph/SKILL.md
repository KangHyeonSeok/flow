---
name: flow-spec-graph
description: 스펙 그래프 관리 시스템. flow spec-* 커맨드로 기능 스펙을 JSON으로 관리하고 의존성 그래프를 생성·분석한다. 스펙 생성, 검증, 영향 분석, 상태 전파, 코드 참조 검증이 필요할 때 사용. 스펙 관련 작업 전반에 사용.
---

# Flow Spec Graph

JSON 기반 기능 스펙 관리 및 의존성 그래프 분석 시스템.

## 스펙 CRUD

```bash
# 초기화
./flow.ps1 spec-init

# 생성 (ID 자동 채번)
./flow.ps1 spec-create --title "새 기능" --parent F-001 --tags "core,cli"

# ID 지정 생성
./flow.ps1 spec-create --id F-040 --title "새 기능" --status draft

# 조회
./flow.ps1 spec-get F-010

# 목록 (필터)
./flow.ps1 spec-list --status working --tag core

# 삭제
./flow.ps1 spec-delete F-040

# review JSON 반영
./flow.ps1 spec-append-review F-040 --input-file ./.flow/review/F-040-review.json --reviewer runner-01
```

## 검증

```bash
# 전체 검증
./flow.ps1 spec-validate

# 엄격 모드 (에러 시 exit 1)
./flow.ps1 spec-validate --strict

# 특정 스펙만
./flow.ps1 spec-validate --id F-010

# 코드 참조 검증
./flow.ps1 spec-check-refs
./flow.ps1 spec-check-refs --strict
```

## 그래프 분석

```bash
# 그래프 정보 출력
./flow.ps1 spec-graph

# 트리 텍스트 출력
./flow.ps1 spec-graph --tree

# JSON 파일로 저장
./flow.ps1 spec-graph --output docs/specs/.spec-cache/graph.json

# 영향 분석
./flow.ps1 spec-impact F-010

# 상태 전파 (dry-run)
./flow.ps1 spec-propagate F-010 --status needs-review

# 상태 전파 (적용)
./flow.ps1 spec-propagate F-010 --status needs-review --apply
```

## 백업/복구

```bash
./flow.ps1 spec-backup
./flow.ps1 spec-restore 20260228-073219
```

## 스펙 JSON 구조

```json
{
  "schemaVersion": 2,
  "id": "F-010",
  "nodeType": "feature",        // feature | task
  "title": "스펙 그래프 관리",
  "description": "설명",
  "status": "queued",          // draft|queued|working|needs-review|verified|deprecated|done
  "parent": "F-001",           // 상위 스펙 ID (트리 구조)
  "dependencies": ["F-010"],   // 의존 스펙 (DAG 구조)
  "conditions": [
    {
      "id": "F-010-C1",
      "nodeType": "condition",
      "description": "Given-When-Then 형식 수락 조건",
      "status": "verified",
      "codeRefs": ["tools/flow-cli/Services/SpecStore.cs#L63-L75"],
      "evidence": []
    }
  ],
  "codeRefs": ["tools/flow-cli/Commands/SpecCommand.cs"],
  "tags": ["spec", "graph"]
}
```

## nodeType 구분

| nodeType | 설명 | 최종 상태 |
|----------|------|----------|
| `feature` | 지속적으로 유지되는 기능 스펙. 수락 조건(conditions) 필요. | `verified` |
| `task` | 일회성 처리 스펙. 보안 검토, 데이터 이전 등 한 번 수행하고 끝나는 작업. 수락 조건 불필요. | `done` |
| `condition` | feature/task의 하위 수락 조건 노드. | `verified` |

### task 타입 예시

```bash
# 보안 검토 task 생성
./flow.ps1 spec-create --id F-099 --title "현시스템 OWASP Top10 보안 취약점 검토" --status draft
# F-099.json의 nodeType을 "task"로 수동 수정
# 검토 완료 후 새 스펙 F-100 생성, F-099는 done으로 종료
./flow.ps1 spec-update F-099 --status done
```

## 스펙 파일 위치

- 스펙 디렉토리: `docs/specs/`
- 스펙 파일 패턴: `docs/specs/{id}.json`
- 백업: `docs/specs/.backup/{timestamp}/`
- 그래프 캐시: `docs/specs/.spec-cache/graph.json`

## VS Code 확장

`flow-ext` 패키지가 VS Code에서 스펙 트리뷰 + Cytoscape.js 그래프 시각화를 제공한다.
