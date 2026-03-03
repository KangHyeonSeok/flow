---
name: flow-spec-create
description: 자연어 입력을 분석하여 스펙 JSON을 생성하는 스킬. 사용자가 자연어로 설명한 기능 요구사항을 파싱하여 spec-create 커맨드와 JSON 편집으로 완성된 스펙을 추가한다. 스펙 생성, 요구사항 구조화가 필요할 때 사용.
---

# Flow Spec Create

사용자가 자연어로 입력한 기능 요구사항을 분석하여 스펙 그래프용 JSON 스펙으로 변환·등록한다.

## 워크플로우

### 1. 기존 스펙 파악

```bash
# 전체 스펙 목록 확인 (부모·의존성 후보 식별)
./flow.ps1 spec-list --pretty

# 트리 구조로 파악
./flow.ps1 spec-graph --tree
```

### 2. 자연어 분석 → 스펙 필드 추출

사용자 입력에서 다음 필드를 추출한다.

| 필드 | 추출 기준 |
|------|----------|
| `title` | 기능의 핵심 동작을 한 문장으로 요약 |
| `description` | 목적·배경·범위를 포함한 상세 설명 |
| `parent` | "~의 하위 기능", "~에 속하는" 등의 표현 → 기존 스펙 ID 매핑 |
| `dependencies` | "~가 필요", "~에 의존", "~를 전제" 등의 표현 → 기존 스펙 ID 목록 |
| `tags` | 기술 키워드, 도메인, 레이어 (예: `cli,spec,core`) |
| `status` | 명시 없으면 `draft` |
| `conditions` | "~해야 한다", "~인 경우", "Given/When/Then" 패턴 → 수락 조건 목록 |

#### 추출 예시

> 사용자 입력: "스펙 목록을 태그로 필터링할 수 있어야 해. F-010 기능의 하위 기능이고, spec-list 커맨드에서 --tag 옵션으로 동작해야 함."

```
title       : "스펙 태그 필터링"
description : "spec-list 커맨드에서 --tag 옵션으로 스펙을 태그 기준으로 필터링한다."
parent      : "F-010"
dependencies: []
tags        : ["cli", "spec", "filter"]
status      : "draft"
conditions  :
  - "주어진 태그가 존재할 때 --tag 옵션을 사용하면 해당 태그를 가진 스펙만 반환된다."
  - "--tag 옵션을 생략하면 전체 스펙이 반환된다."
```

### 3. 스펙 생성

```bash
./flow.ps1 spec-create \
  --title "스펙 태그 필터링" \
  --description "spec-list 커맨드에서 --tag 옵션으로 스펙을 태그 기준으로 필터링한다." \
  --parent F-010 \
  --dependencies "F-005,F-008" \
  --tags "cli,spec,filter" \
  --status draft \
  --pretty
```

`--dependencies`는 의존 스펙이 없으면 생략한다.

출력에서 생성된 스펙 ID를 확인한다 (예: `F-042`).

### 4. 수락 조건(conditions) 추가

추출한 수락 조건이 있는 경우, 생성된 스펙 JSON 파일을 직접 편집하여 `conditions` 배열을 추가한다.

파일 위치: `docs/specs/{id}.json`

```json
{
  "schemaVersion": 2,
  "id": "F-042",
  "nodeType": "feature",
  "title": "스펙 태그 필터링",
  "description": "spec-list 커맨드에서 --tag 옵션으로 스펙을 태그 기준으로 필터링한다.",
  "status": "draft",
  "parent": "F-010",
  "dependencies": [],
  "conditions": [
    {
      "id": "F-042-C1",
      "nodeType": "condition",
      "description": "주어진 태그가 존재할 때 --tag 옵션을 사용하면 해당 태그를 가진 스펙만 반환된다.",
      "status": "draft",
      "codeRefs": [],
      "evidence": []
    },
    {
      "id": "F-042-C2",
      "nodeType": "condition",
      "description": "--tag 옵션을 생략하면 전체 스펙이 반환된다.",
      "status": "draft",
      "codeRefs": [],
      "evidence": []
    }
  ],
  "codeRefs": [],
  "evidence": [],
  "tags": ["cli", "spec", "filter"]
}
```

**Condition ID 규칙**: `{스펙ID}-C{번호}` (예: `F-042-C1`, `F-042-C2`)

### 5. 검증

```bash
# 생성한 스펙 확인
./flow.ps1 spec-get F-042 --pretty

# 유효성 검사
./flow.ps1 spec-validate --id F-042 --pretty

# 그래프에서 위치 확인
./flow.ps1 spec-graph --tree
```

## 자연어 패턴 → 필드 매핑 가이드

### parent 식별

| 자연어 표현 | 처리 방법 |
|-----------|----------|
| "F-010의 하위 기능" | `--parent F-010` |
| "빌드 시스템에 속하는 기능" | spec-list에서 "빌드 시스템" 관련 스펙 ID 검색 후 parent 설정 |
| "독립적인 기능" | parent 생략 |

### dependencies 식별

| 자연어 표현 | 처리 방법 |
|-----------|----------|
| "F-010 기능이 있어야 동작" | `--dependencies "F-010"` |
| "스펙 초기화(spec-init) 후에 동작" | spec-init 관련 스펙 ID 검색 후 `--dependencies "{id}"` |

### conditions 추출

- "~해야 한다" → 수락 조건
- "~인 경우 ~가 된다" → Given/When/Then 형식으로 변환
- "~를 하면 ~가 출력된다" → 수락 조건
- 명시적 조건이 없으면 기능 설명에서 핵심 동작을 조건으로 도출

## 전체 예시

### 입력
> "CLI에서 스펙을 상태(status)로 필터링하는 기능. spec-list에 --status 옵션 추가. draft 상태인 스펙만 조회하거나 전체 조회가 가능해야 해."

### 실행

```bash
# 1. 기존 스펙 확인
./flow.ps1 spec-list --pretty

# 2. 스펙 생성
./flow.ps1 spec-create \
  --title "스펙 상태 필터링" \
  --description "spec-list 커맨드에 --status 옵션을 추가하여 특정 상태의 스펙만 조회할 수 있도록 한다." \
  --tags "cli,spec,filter" \
  --status draft \
  --pretty
```

### docs/specs/F-043.json (조건 추가 후)

```json
{
  "schemaVersion": 2,
  "id": "F-043",
  "nodeType": "feature",
  "title": "스펙 상태 필터링",
  "description": "spec-list 커맨드에 --status 옵션을 추가하여 특정 상태의 스펙만 조회할 수 있도록 한다.",
  "status": "draft",
  "parent": null,
  "dependencies": [],
  "conditions": [
    {
      "id": "F-043-C1",
      "nodeType": "condition",
      "description": "--status draft를 지정하면 draft 상태인 스펙만 반환된다.",
      "status": "draft",
      "codeRefs": [],
      "evidence": []
    },
    {
      "id": "F-043-C2",
      "nodeType": "condition",
      "description": "--status 옵션을 생략하면 모든 상태의 스펙이 반환된다.",
      "status": "draft",
      "codeRefs": [],
      "evidence": []
    }
  ],
  "codeRefs": [],
  "evidence": [],
  "tags": ["cli", "spec", "filter"]
}
```

```bash
# 3. 검증
./flow.ps1 spec-validate --id F-043 --pretty
```
