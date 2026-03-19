# 세션 요약: Runner 파이프라인 수정 및 flow CLI 생성 (2026-03-16)

## 수행한 작업

### 1. 프로젝트 정리

- `tools/flow-cli`, `tools/flow-cli.Tests`, `tools/flow-ext`, `tools/flow-kanban` 제거
- `flow.sln`에서 참조 제거
- `build-flow.ps1`, `build-flow.sh`, `flow.ps1` 래퍼 스크립트 삭제
- `.github/workflows/release.yml`에서 flow-cli/flow-ext 빌드 단계 제거
- `.github/agents/kanban-spec.agent.md`, `.github/skills/flow-kanban-spec/` 삭제
- `README.md`, skills, instructions 내 flow-cli 참조 업데이트

### 2. flow CLI 생성 (`tools/flow/`)

flow-core 기능을 호출하는 간단한 CLI 도구 생성:

- `spec create` — 스펙 생성 (F-NNN 자동 채번)
- `spec list` — 스펙 목록 (상태 필터)
- `spec get` — 스펙 상세 JSON 출력
- `runner start` — 데몬/단일 실행
- `runner stop` — PID 기반 프로세스 종료
- `runner status` — 실행 상태 확인

### 3. Runner 파이프라인 버그 수정

| 문제 | 원인 | 수정 |
|------|------|------|
| Draft에서 agent 실패 시 무한 루프 | `EvalExecutionFailed()`가 Draft 상태 불허 | Draft/Queued도 허용하도록 변경 |
| backend 에러 시 즉시 Failed 전환 | OutputParser가 exit code 에러를 TerminalFailure로 처리 | RetryableFailure로 변경 |
| claude CLI 실행 실패 (git bash) | `CLAUDE_CODE_GIT_BASH_PATH` 미설정 | Windows에서 자동 탐지 로직 추가 |
| `--output-format stream-json` 에러 | `-p`와 함께 `--verbose` 필요 | `--verbose` 플래그 추가 |
| Developer `acp` 명령 미설치 | backend-config에 copilot-acp 설정 | claude-cli로 변경 |

### 4. 데몬 테스트 결과

실제 Claude CLI 백엔드로 전체 파이프라인 동작 확인:
```
Draft → Queued → ArchitectureReview → Implementation → TestValidation → Review
```

Review에서 SpecValidator가 rework을 반복 요청하는 루프 발생 (프롬프트 튜닝 필요).

## 다음에 할 일

### 우선순위 높음

1. **SpecValidator 프롬프트 튜닝** — Review 단계에서 rework 루프를 방지하도록 SpecValidator 프롬프트 개선. 더미 agent로 구현된 코드 변경이 실제로 반영되었는지 판단하는 기준을 명확히 해야 함.

2. **`flow init` 명령 구현** — 프로젝트 초기화 시 `backend-config.template.json`을 `~/.flow/backend-config.json`으로 복사하는 명령.

3. **retry 전략 개선** — 현재 RetryableFailure 시 backoff 없이 다음 사이클에서 바로 재시도. backoff 로직(`RetryNotBefore`)이 있지만 기본값이 0초이므로 실질적 대기 없음.

### 우선순위 중간

4. **worktree 내 변경사항 커밋/PR 생성** — Developer가 worktree에서 코드를 변경하지만, 이를 커밋하거나 PR을 생성하는 로직이 없음. Implementation 완료 후 자동 커밋 + PR 생성 흐름 필요.

5. **로그 가시성 향상** — 현재 활동 로그가 JSONL 파일에만 기록됨. 데몬 실행 중 콘솔에 진행 상황을 출력하면 디버깅이 쉬워짐.

6. **`spec edit` 명령** — JSON 직접 편집 없이 AC, dependencies 등을 CLI로 수정할 수 있는 명령.

### 우선순위 낮음

7. **copilot-acp 백엔드 테스트** — acp 설치 후 실제 동작 확인.

8. **로드맵 6단계 (Webservice)** — 코어 파이프라인이 안정화되면 착수.

## 변경된 파일

```
수정:
  tools/flow-core/Rules/RuleEvaluator.cs        — ExecutionFailed 허용 상태 확장
  tools/flow-core/Agents/Cli/OutputParser.cs     — 에러 시 RetryableFailure
  tools/flow-core/Backend/ClaudeCliBackend.cs    — --verbose, git bash 자동탐지
  tools/flow-core.tests/OutputParserTests.cs     — 테스트 기대값 업데이트
  tools/flow-core.tests/CliSpecValidatorTests.cs — 테스트 기대값 업데이트
  tools/flow/README.md                           — backend-config 설정 안내 추가

신규:
  tools/flow/backend-config.template.json        — 백엔드 설정 템플릿

삭제 (이전 커밋):
  tools/flow-cli/                                — 전체
  tools/flow-cli.Tests/                          — 전체
  tools/flow-ext/                                — 전체
  tools/flow-kanban/                             — 전체
```
