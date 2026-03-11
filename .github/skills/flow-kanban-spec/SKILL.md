---
name: flow-kanban-spec
description: 칸반 서버를 통해 스펙을 생성·조회하는 스킬. 칸반 API(http://localhost:3000)로 프로젝트 목록 조회, 스펙 목록 조회, 스펙 상세 조회, 새 스펙 생성, 상태 변경을 수행한다. 스펙 생성, 스펙 추가, 칸반에 등록, 기능 정의가 필요할 때 사용.
---

# Flow Kanban Spec

칸반 서버 API를 통해 스펙을 관리한다. 서버: `http://localhost:3000`

## API 엔드포인트

### 조회

```bash
# 프로젝트 목록
curl http://localhost:3000/api/projects

# 스펙 목록 (전체)
curl http://localhost:3000/api/specs

# 스펙 상세
curl http://localhost:3000/api/specs/{specId}

# 다음 스펙 ID 조회
curl "http://localhost:3000/api/specs/next-id?project={project}&prefix=F"
```

### 스펙 생성

```bash
curl -X POST http://localhost:3000/api/specs \
  -H "Content-Type: application/json" \
  -d '{
    "project": "flow",
    "title": "사용자 인증 시스템",
    "type": "기능",
    "description": "이메일/비밀번호 기반 로그인 기능을 구현한다.",
    "status": "초안",
    "conditions": [
      {
        "id": "F-010-C1",
        "description": "이메일과 비밀번호로 로그인할 수 있다.",
        "status": "초안"
      }
    ],
    "relatedFiles": ["src/auth/login.ts"]
  }'
```

**필수 필드**: `project`, `title`
**선택 필드**: `id` (미지정 시 자동 채번), `type` (기능|태스크, 기본: 기능), `description`, `status` (기본: 초안), `conditions`, `tests`, `relatedFiles`, `specMd`

### 상태 변경

```bash
curl -X PATCH http://localhost:3000/api/specs/{specId}/status \
  -H "Content-Type: application/json" \
  -d '{"status": "대기"}'
```

유효 상태: `초안`, `대기`, `작업`, `테스트 검증`, `리뷰`, `검토`, `활성`, `완료`

### 프로젝트 추가

```bash
curl -X POST http://localhost:3000/api/projects \
  -H "Content-Type: application/json" \
  -d '{"path": "D:\\Projects\\my-app", "name": "my-app", "defaultBranch": "main"}'
```

## 스펙 스키마 (meta.json)

```json
{
  "title": "스펙 제목",
  "type": "기능",
  "status": "초안",
  "attemptCount": 0,
  "updatedAt": "2026-03-12T...",
  "conditions": [
    {
      "id": "F-010-C1",
      "description": "Given ... When ... Then ...",
      "status": "초안"
    }
  ],
  "tests": [
    {
      "id": "T-001",
      "type": "단위 테스트",
      "conditionId": "F-010-C1",
      "description": "테스트 설명",
      "lastResult": null
    }
  ],
  "relatedFiles": ["src/file.ts"]
}
```

## Condition ID 규칙

`{specId}-C{번호}` (예: `F-010-C1`, `F-010-C2`)

## 워크플로우

1. `GET /api/projects` — 대상 프로젝트 확인
2. `GET /api/specs` — 기존 스펙과 중복 확인
3. `GET /api/specs/next-id?project={project}` — ID 자동 채번
4. `POST /api/specs` — 스펙 생성
5. 필요 시 `PATCH /api/specs/{id}/status` — 상태를 `대기`로 전환
