---
description: "flow spec-graph 커맨드 레퍼런스. .github/agents/ 내 에이전트 파일 작업 시 자동 첨부. spec-create, spec-list, spec-impact, spec-validate, spec-graph 커맨드 사용이 필요할 때."
applyTo: ".github/agents/**"
---

# Flow Spec Graph — 에이전트용 레퍼런스

`./flow.ps1` 커맨드로 스펙을 관리한다. 모든 명령은 프로젝트 루트에서 실행.

## 핵심 커맨드

```bash
# 스펙 생성 (ID 자동 채번)
./flow.ps1 spec-create --title "<제목>" --parent <상위ID> --tags "<태그>"

# 스펙 조회
./flow.ps1 spec-get <ID>
./flow.ps1 spec-list --status working
./flow.ps1 spec-list --tag <태그>

# 그래프 / 트리 출력
./flow.ps1 spec-graph
./flow.ps1 spec-graph --tree

# 영향 분석
./flow.ps1 spec-impact <ID>

# 상태 전파 (dry-run → 적용)
./flow.ps1 spec-propagate <ID> --status needs-review
./flow.ps1 spec-propagate <ID> --status needs-review --apply

# 검증
./flow.ps1 spec-validate
./flow.ps1 spec-validate --strict
./flow.ps1 spec-check-refs
```

## 스펙 JSON 핵심 필드

| 필드 | 필수 | 값 예시 |
|------|------|---------|
| `id` | ✓ | `F-010` |
| `nodeType` | ✓ | `feature` \| `task` |
| `title` | ✓ | `"로그인 기능"` |
| `status` | ✓ | `draft` → `queued` → `working` → `needs-review` → `verified` |
| `parent` | 선택 | `"F-001"` |
| `dependencies` | 선택 | `["F-005", "F-008"]` |
| `conditions` | feature 필수 | Given-When-Then 형식 수락 조건 배열 |
| `codeRefs` | 선택 | `["tools/flow-core/Services/Foo.cs#L10-L20"]` |

## nodeType 구분

- **feature**: 지속적으로 유지되는 기능. `conditions` 필수. 최종 상태: `verified`
- **task**: 일회성 작업 (보안 검토, 데이터 이전 등). 최종 상태: `done`

## RAG 연동 패턴

```bash
# 작업 전: 과거 이력 검색
./flow.ps1 db-query --query "<키워드>" --top 5

# 작업 후: 결과 기록
./flow.ps1 db-add --content "<요약>" --feature "<기능명>" --tags "spec,planning"
```
