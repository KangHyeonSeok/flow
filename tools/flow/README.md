# flow CLI

flow-core 기능을 호출하는 간단한 CLI 도구. 스펙 관리와 러너 제어를 제공한다.

## 빌드

```bash
dotnet build tools/flow/flow.csproj
```

## 명령어

### spec create — 스펙 생성

```bash
flow spec create --project <id> --title <title> [--id <id>] [--type feature|task] [--problem <text>] [--goal <text>]
```

- `--project` (필수): 프로젝트 ID
- `--title` (필수): 스펙 제목
- `--id`: 스펙 ID. 생략 시 `F-NNN` 자동 채번
- `--type`: `feature` (기본값) 또는 `task`
- `--problem`, `--goal`: 문제 정의, 목표

생성된 스펙은 `Draft` 상태로 저장된다.

```
$ flow spec create --project myproj --title "사용자 인증" --type feature
Created: F-001 "사용자 인증" (Feature, Draft)
```

### spec list — 스펙 목록

```bash
flow spec list --project <id> [--status <status>]
```

- `--status`: 상태 필터 (draft, queued, implementation, review, active, failed, completed 등)

```
$ flow spec list --project myproj
  F-001        Draft              사용자 인증
  F-002        Queued             데이터 모델 설계
```

### spec get — 스펙 상세

```bash
flow spec get --project <id> <spec-id>
```

스펙 JSON 전체를 출력한다.

```
$ flow spec get --project myproj F-001
{
  "id": "F-001",
  "projectId": "myproj",
  "title": "사용자 인증",
  "type": "feature",
  "state": "draft",
  ...
}
```

### runner start — 러너 시작

```bash
flow runner start --project <id> [--once]
```

- `--once`: 한 사이클만 실행하고 종료
- `--once` 생략 시 데몬 모드 (30초 간격 폴링, Ctrl+C로 중지)

```
$ flow runner start --project myproj --once
Running single cycle for project 'myproj'...
Processed 1 spec(s).

$ flow runner start --project myproj
Runner started (PID 12345) for project 'myproj'
Poll interval: 30s | Press Ctrl+C to stop
```

### runner stop — 러너 중지

```bash
flow runner stop
```

PID 파일(`~/.flow/runner.pid`)을 읽어 프로세스를 종료한다.

### runner status — 러너 상태

```bash
flow runner status
```

## 에이전트 설정

`~/.flow/backend-config.json` 파일 유무에 따라 에이전트가 결정된다.

| 설정 파일 | 에이전트 |
|-----------|----------|
| 없음 | 더미 에이전트 (테스트용, 즉시 통과) |
| 있음 | CLI 에이전트 (Claude/Copilot 백엔드) |

## 저장 경로

```
~/.flow/
  projects/{projectId}/specs/{specId}/
    spec.json                    # 스펙 본문
    assignments/{asgId}.json     # 할당 기록
    review-requests/{rrId}.json  # 검토 요청
    activity/                    # 활동 로그
  worktrees/{specId}/            # git worktree (구현 시)
  runner.pid                     # 데몬 PID
  backend-config.json            # 에이전트 백엔드 설정
```

`FLOW_HOME` 환경변수로 기본 경로(`~/.flow`)를 변경할 수 있다.
