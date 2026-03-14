# Phase 5: 실제 Agent MVP — 결정 및 실행 기준

이 문서는 Phase 5 (실제 agent MVP + prompt tuning) 구현 전에 정리한 선택지를 검토한 뒤, 권장안을 수용하여 확정된 결정과 실행 기준으로 재구성한 문서다.

## 0. 검토 결과 요약

- Phase 5 구현 대상은 `flow-core`로 확정한다. `flow-cli`는 참조 구현으로만 활용한다.
- 첫 실제 agent는 `SpecValidator`로 확정한다.
- 백엔드는 `ICliBackend`로 추상화하고, 기본 매핑은 Claude CLI + Copilot ACP 멀티 백엔드로 유지한다.
- `flow-cli`의 현재 Copilot 통합은 ACP가 아니라 `copilot -p` 기반이므로, Phase 5의 ACP 백엔드는 신규 구현으로 간주해야 한다.
- Evidence 계약은 Phase 5a의 필수 선행조건이 아니다. SpecValidator MVP와 분리해 Phase 5b에서 계약을 추가한다.
- 멀티 runner, 병렬 agent dispatch, flow-cli 마이그레이션은 Phase 5의 비목표로 명시한다.

## 1. 현재 상태

### 1.1 flow-core (라이브러리) — Phase 1–4 완료

| 영역 | 상태 | 비고 |
|------|------|------|
| State Machine | ✅ 완료 | 27 이벤트, 9 상태, 7 처리상태 |
| RuleEvaluator | ✅ 완료 | 순수 함수, actor 권한 검증, retry 관리 |
| DependencyEvaluator | ✅ 완료 | cascade blocked/failed/resolved |
| FileFlowStore | ✅ 완료 | CAS 기반, spec/assignment/RR/activity 분리 저장 |
| FlowRunner | ✅ 완료 | dispatch → agent → evaluate → apply, timeout/retry |
| SideEffectExecutor | ✅ 완료 | 7종 side effect |
| ReviewResponseSubmitter | ✅ 완료 | 원자적 응답 제출 (Phase 4 수정 완료) |
| IAgentAdapter | ✅ 완료 | 공통 인터페이스 + ProposedReviewRequest |
| Dummy Agents | ✅ 완료 | 5개 (Planner, Architect, Developer, TestValidator, SpecValidator) |
| Archive/DerivedFrom | ✅ 완료 | Failed → Archived, Planner 재등록 |
| 테스트 | ✅ 225개 통과 | unit + integration + golden scenario + review loop + regression |

### 1.2 flow-cli (데몬/CLI) — 별도 구현

flow-cli는 flow-core를 **사용하지 않는** 독립 구현이다:

| 영역 | flow-core | flow-cli | 차이 |
|------|-----------|----------|------|
| State 모델 | `FlowState` enum | `SpecNode.status` (string) | 다른 상태 체계 |
| Runner | `FlowRunner.cs` | `RunnerService.cs` | 별도 orchestration |
| Storage | `FileFlowStore` (Spec/Assignment/RR) | `SpecStore` (SpecNode only) | 다른 저장 모델 |
| Agent | `IAgentAdapter` 인터페이스 | `CopilotService` (직접 CLI 호출) | agent 추상화 없음 |
| 규칙 엔진 | `RuleEvaluator` (27 이벤트) | `SpecValidator.cs` (산재) | 형식 상태 기계 없음 |

### 1.3 로드맵 위치

```
Phase 1: State Rule ✅
Phase 2: Core Storage ✅
Phase 3: Runner + Dummy Agent ✅
Phase 4: Review Loop ✅
Phase 5: 실제 Agent MVP + Prompt Tuning  ← 현재
Phase 6: Webservice
Phase 7: Docker
Phase 8: Slack
```

## 2. 멀티 백엔드 아키텍처

Phase 5에서는 **Claude CLI**와 **Copilot CLI (ACP)** 두 백엔드를 모두 사용한다. 통합 인터페이스(`ICliBackend`)를 두고 역할별로 백엔드를 교체하며 조율한다. 향후 OpenAI API, Gemini API 등도 같은 인터페이스로 추가 가능하다.

### 2.1 역할별 기본 백엔드 매핑

| 역할 | 기본 백엔드 | 근거 |
|------|-------------|------|
| **Planner** | Claude CLI | 복잡한 분석/판단, 긴 컨텍스트 처리에 강점 |
| **Architect** | Claude CLI | 설계 리뷰, 구조 분석 |
| **TestValidator** | Claude CLI | 테스트 적합성 판단, 텍스트 분석 |
| **SpecValidator** | Claude CLI | AC precheck, spec validation |
| **Developer** | Copilot CLI (ACP) | 코드 생성, git 통합, 도구 사용 (Code 모드) |

> 역할별 백엔드는 설정으로 교체 가능. 예: SpecValidator를 Copilot으로 바꿔서 비교 테스트.

참고: `AgentRole` enum에는 `SpecManager`가 있으나, 현재 FlowRunner에서 사용하지 않으며 Phase 5 범위에 포함하지 않는다. `BackendRegistry`에 매핑이 없어도 런타임 오류가 발생하지 않도록 조회 실패 시 `null` 반환 처리를 해둔다.

### 2.2 통합 인터페이스 설계

```
┌──────────────────────────────────────────────────────────────┐
│  FlowRunner                                                  │
│  ┌────────────────────────────────────────────────────────┐  │
│  │  IAgentAdapter (역할별: Planner, Developer, ...)       │  │
│  │  ┌──────────────────┐  ┌───────────────────────────┐   │  │
│  │  │  PromptBuilder   │  │  OutputParser             │   │  │
│  │  │  (AgentInput →   │  │  (응답 텍스트 →           │   │  │
│  │  │   프롬프트 텍스트)│  │   AgentOutput)            │   │  │
│  │  └────────┬─────────┘  └───────────────┬───────────┘   │  │
│  │           │                             │               │  │
│  │           ▼                             ▲               │  │
│  │  ┌──────────────────────────────────────────────────┐   │  │
│  │  │  ICliBackend (통합 인터페이스)                    │   │  │
│  │  │                                                  │   │  │
│  │  │  Task<CliResponse> RunPromptAsync(               │   │  │
│  │  │    string prompt,                                │   │  │
│  │  │    CliBackendOptions options,                    │   │  │
│  │  │    CancellationToken ct)                         │   │  │
│  │  └──────────────────────────────────────────────────┘   │  │
│  │           │                                             │  │
│  │     ┌─────┴──────────────────────┐                      │  │
│  │     │                            │                      │  │
│  │     ▼                            ▼                      │  │
│  │  ┌──────────────┐  ┌───────────────────────────────┐    │  │
│  │  │ ClaudeCliBkd │  │ CopilotAcpBackend             │    │  │
│  │  │              │  │                               │    │  │
│  │  │ claude -p    │  │ copilot --acp --stdio         │    │  │
│  │  │ --output-fmt │  │ JSON-RPC 2.0 / NDJSON         │    │  │
│  │  │ stream-json  │  │ 세션 기반, 모드 전환           │    │  │
│  │  │ --dangerousl │  │                               │    │  │
│  │  │ y-skip-perms │  │                               │    │  │
│  │  └──────────────┘  └───────────────────────────────┘    │  │
│  │                                                         │  │
│  │  ┌──────────────┐  ┌──────────────┐                     │  │
│  │  │ (향후)       │  │ (향후)       │                     │  │
│  │  │ OpenAiApiBkd │  │ GeminiApiBkd │                     │  │
│  │  │ API key 기반 │  │ API key 기반 │                     │  │
│  │  └──────────────┘  └──────────────┘                     │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

### 2.3 ICliBackend 인터페이스

```csharp
/// <summary>CLI/API 백엔드 통합 인터페이스</summary>
public interface ICliBackend : IAsyncDisposable
{
    /// <summary>백엔드 식별자 (예: "claude-cli", "copilot-acp")</summary>
    string BackendId { get; }

    /// <summary>프롬프트 실행 후 응답 반환</summary>
    Task<CliResponse> RunPromptAsync(
        string prompt,
        CliBackendOptions options,
        CancellationToken ct = default);
}

public record CliBackendOptions
{
    /// <summary>작업 디렉터리 (worktree 경로 등)</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>파일 수정 허용 여부 (Developer=true, 나머지=false)</summary>
    public bool AllowFileEdits { get; init; } = false;

    /// <summary>허용 도구 목록 (Claude CLI: --allowedTools, ACP: request_permission 정책)</summary>
    public IReadOnlyList<string>? AllowedTools { get; init; }

    /// <summary>Idle 타임아웃 (출력 없는 시간 제한)</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(300);

    /// <summary>전체 실행 Hard 타임아웃</summary>
    public TimeSpan HardTimeout { get; init; } = TimeSpan.FromSeconds(1800);
}

public record CliResponse
{
    /// <summary>전체 응답 텍스트</summary>
    public required string ResponseText { get; init; }

    /// <summary>정상 종료 여부</summary>
    public bool Success { get; init; }

    /// <summary>오류 메시지 (실패 시)</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>종료 사유</summary>
    public CliStopReason StopReason { get; init; }
}

public enum CliStopReason
{
    Completed,   // 정상 완료
    Timeout,     // 타임아웃
    Cancelled,   // 취소됨
    Error        // 프로세스 오류
}
```

### 2.4 Claude CLI 백엔드

```bash
claude -p --dangerously-skip-permissions \
  --output-format stream-json \
  --allowedTools Read,Grep,Glob,Bash,Write,Edit \
  --max-turns 50
```

- **프로세스 수명**: 프롬프트당 1회 spawn (stateless)
- **프롬프트 전달**: stdin write → stdin close → stdout 수집
- **출력 파싱**: stream-json 라인별 파싱 → `CliResponse.ResponseText` 변환
  - `type: "result"` → `result` 필드를 `ResponseText`에 설정 (최종 텍스트)
  - `type: "tool_use"` → Activity 로그로 기록 (ResponseText에 포함하지 않음)
  - `type: "content_block_delta"` → `result`가 없는 경우 fallback 텍스트 누적
  - `result` 이벤트가 없으면 `Success = false`, `StopReason = Error`
- **도구 제어**: `--allowedTools`로 역할별 도구 제한
  - Planner/SpecValidator/TestValidator: `Read,Grep,Glob` (읽기 전용)
  - Architect: `Read,Grep,Glob,Bash` (빌드/분석 가능)
- **프로세스 종료 (크로스 플랫폼)**:
  - Graceful 순서: CancellationToken → stdin close → 5초 대기 → Force kill
  - **Windows**: `Process.Kill(entireProcessTree: true)` (.NET 8). POSIX 시그널 없음.
  - **macOS/Linux**: `SIGTERM` → 5초 대기 → `SIGKILL`. `Process.Kill()`은 내부적으로 SIGKILL 전송.
  - 구현: 플랫폼 분기 헬퍼 (`ProcessKiller.GracefulKillAsync`) — `RuntimeInformation.IsOSPlatform`으로 분기
  - 재시도: kill 후 최대 3회 백오프 (30s, 60s)
- **환경변수**: `CI=1`, `NO_COLOR=1`, `CLAUDECODE` 삭제

### 2.5 Copilot CLI ACP 백엔드

```bash
copilot --acp --stdio
```

- **프로세스 수명**: 장기 실행, Runner 시작 시 1회 spawn → 전체 라이프사이클 재사용
- **통신**: JSON-RPC 2.0 over NDJSON (stdin/stdout)
- **세션 수명**: `RunPromptAsync` 호출 단위로 세션을 생성·폐기한다 (프로세스는 장기, 세션은 호출 단위).
  - 진입: `session/new(cwd)` → `session/set_mode` → `session/prompt`
  - 완료: stopReason 수신 후 세션 폐기 (다음 호출에서 새 세션)
  - `ICliBackend` 계약은 stateless를 유지한다. sessionId는 내부 구현 상세.
- **프로세스 종료**: Claude CLI와 동일한 크로스 플랫폼 `ProcessKiller.GracefulKillAsync` 사용 → 프로세스 재시작

**ACP 프로토콜 라이프사이클**:
```
Client (flow)                   Agent (Copilot CLI)
  │                                │
  │── initialize ─────────────────>│  프로토콜 버전 + 기능 협상
  │<── initialize response ────────│
  │                                │
  │── session/new ────────────────>│  cwd + MCP 서버 설정
  │<── session response ───────────│  sessionId 반환
  │                                │
  │── session/set_mode ───────────>│  Code 모드 전환
  │                                │
  │── session/prompt ─────────────>│  프롬프트 전송
  │<── session/update (알림) ──────│  응답 스트리밍 (반복)
  │<── request_permission ─────────│  도구 실행 승인 요청
  │── allow_always ───────────────>│
  │<── session/update (알림) ──────│  도구 실행 결과
  │<── prompt response ────────────│  stopReason: end_turn
  │                                │
  │── session/prompt ─────────────>│  후속 프롬프트 (동일 세션)
  │   ...                          │
```

**ACP 핵심 메서드**:

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `initialize` | Client→Agent | 프로토콜 버전, capabilities 협상 |
| `session/new` | Client→Agent | 세션 생성 (cwd, MCP 서버) |
| `session/prompt` | Client→Agent | 프롬프트 전송, stopReason 반환 |
| `session/cancel` | Client→Agent | 작업 취소 |
| `session/set_mode` | Client→Agent | 모드 전환 (Ask/Architect/Code) |
| `session/update` | Agent→Client | 응답 스트리밍, 도구 상태 알림 |
| `request_permission` | Agent→Client | 도구 실행 승인 요청 |

**ACP 세션 모드**:

| 모드 | 설명 | 용도 |
|------|------|------|
| **Ask** | 변경 전 권한 요청 | 읽기 전용 분석 |
| **Architect** | 설계/계획, 구현 없음 | 설계 리뷰 |
| **Code** | 전체 도구 접근, 코드 수정 | Developer 기본 모드 |

**ACP 권한 정책**: Developer = `allow_always` (자율 실행)

**ACP StopReason**: `end_turn` (완료), `max_tokens`, `cancelled`, `refusal`

### 2.6 두 백엔드 비교

| 영역 | Claude CLI | Copilot CLI (ACP) |
|------|-----------|-------------------|
| **프로세스 수명** | 프롬프트당 1회 spawn | 장기 실행, 다중 프롬프트 |
| **통신** | stdin/stdout stream-json | JSON-RPC 2.0 NDJSON |
| **도구 제어** | `--allowedTools` (화이트리스트) | `request_permission` (인터랙티브) |
| **세션** | 없음 (stateless) | 세션 기반, 복원 가능 |
| **모드** | 없음 | Ask/Architect/Code |
| **취소** | stdin close → SIGTERM/Kill | `session/cancel` (graceful) |
| **인증** | 없음 (로컬 CLI) | GitHub Copilot 구독 |
| **강점** | 분석/판단, 긴 컨텍스트 | 코드 생성, IDE 통합, git |
| **API 키** | 불필요 (로컬) | 불필요 (구독) |

### 2.7 역할별 백엔드 설정 구조

```jsonc
// RunnerConfig 내부 (또는 .flow/config.json)
{
  "agentBackends": {
    "planner":       { "backend": "claude-cli" },
    "architect":     { "backend": "claude-cli" },
    "testValidator": { "backend": "claude-cli" },
    "specValidator": { "backend": "claude-cli" },
    "developer":     { "backend": "copilot-acp" }
  },
  "backends": {
    "claude-cli": {
      "command": "claude",
      "idleTimeoutSeconds": 300,
      "hardTimeoutSeconds": 1800,
      "maxRetries": 3
    },
    "copilot-acp": {
      "command": "copilot",
      "idleTimeoutSeconds": 900,
      "hardTimeoutSeconds": 3600,
      "defaultMode": "code"
    }
    // 향후 추가 가능:
    // "openai-api": { "apiKeyEnv": "OPENAI_API_KEY", "model": "gpt-4o", ... }
    // "gemini-api": { "apiKeyEnv": "GEMINI_API_KEY", "model": "gemini-2.5-pro", ... }
  }
}
```

역할별 백엔드를 바꿔서 비교 테스트:
- `"specValidator": { "backend": "copilot-acp" }` → Copilot으로 SpecValidator 실행
- `"developer": { "backend": "claude-cli" }` → Claude로 Developer 실행

### 2.8 향후 확장: API 백엔드

동일한 `ICliBackend` 인터페이스로 HTTP API 백엔드도 구현 가능:

| 백엔드 | 구현 방식 | 인증 |
|--------|-----------|------|
| `OpenAiApiBackend` | OpenAI Chat Completions API | `OPENAI_API_KEY` 환경변수 |
| `GeminiApiBackend` | Google Gemini API | `GEMINI_API_KEY` 환경변수 |
| `AnthropicApiBackend` | Claude API (직접 호출) | `ANTHROPIC_API_KEY` 환경변수 |

API 백엔드는 Phase 5 범위 밖. 인터페이스만 확장 가능하도록 설계.

## 3. 참조 프로젝트 분석 (Claude CLI 기반)

Phase 5 설계에 앞서 두 참조 프로젝트의 Claude CLI 통합 방식을 분석했다.

참고:
- 현재 `flow-cli`는 Copilot ACP가 아니라 `copilot -p` 기반 구현을 사용한다.
- 따라서 Phase 5의 `CopilotAcpBackend`는 기존 `flow-cli` 코드를 직접 이식하는 작업이 아니라, 실행 경로 해석, Windows `.ps1` 처리, timeout/retry 정책만 참조하는 신규 구현이다.

### 3.1 Uni (Slack AI 동료 봇)

TypeScript/Node.js 기반 Slack 봇으로 Claude CLI를 추론 엔진으로 사용한다.

**호출 방식**:
- 단순 호출: `claude -p` (stdin 프롬프트, 도구 없음, 120s timeout)
- Agent 모드: `claude -p --dangerously-skip-permissions --allowedTools Read,Write,Edit,Glob,Grep,Bash --output-format stream-json` (300s timeout)
- 프로세스 spawn → stdin에 프롬프트 write → stdout에서 결과 수집

**프롬프트 조립 파이프라인**:
```
시스템 프롬프트 (prompts/system.md)
+ 팀 컨텍스트 (knowledge/context.md)
+ 채널 히스토리 (data/history/<channelId>.jsonl, 최근 20건)
+ 현재 메시지 + 첨부 파일
+ 사용 가능한 스킬 문서 (skills/)
```

**에러/재시도 전략**:
- Signal kill (SIGTERM/SIGKILL) → 최대 3회 재시도 (30s, 60s 백오프)
- Timeout → 재시도 없이 사용자에게 "요청을 분할하세요" 안내
- Exit code 오류 → 재시도 없이 에러 메시지 반환
- `ClaudeCliError` 타입 분류: signal / timeout / error

**stream-json 파싱**:
```typescript
// 라인별 JSON 파싱
if (event.type === 'tool_use') → 진행 상태 UI 업데이트
if (event.type === 'result') → 최종 텍스트 추출
if (event.type === 'content_block_delta') → 텍스트 누적 (fallback)
```

**타임아웃 (용도별 차등)**:
| 용도 | 타임아웃 |
|------|---------|
| 단순 프롬프트 | 120s |
| DM/멘션 에이전트 | 300s |
| Idle 루프 | 180s |
| 자기 개선 | 600s |
| 동적 스케줄 | 3600s |

**핵심 설계 결정**:
- CLI 기반 호출 (API SDK 아닌 프로세스 spawn) — 도구 사용, 파일 접근, bash 실행 가능
- 스킬은 `skills/` 디렉터리의 tsx 스크립트 — Claude가 Bash로 실행
- 채널별 JSONL 히스토리 — 무제한 성장, 최근 20건 컨텍스트
- 자율 루프 (idle-loop 30분, self-improve 자정) + 제안→승인 패턴

### 3.2 claw-empire (AI 에이전트 회사 시뮬레이터)

TypeScript/Node.js 기반 멀티 프로바이더 에이전트 오케스트레이터. Claude, Codex, Gemini, OpenCode, Kimi CLI + Copilot, Antigravity HTTP 에이전트를 지원한다.

**호출 방식**:
```typescript
const args = [
  "claude",
  "--dangerously-skip-permissions",
  "--print",                        // -p의 long form
  "--verbose",
  "--output-format=stream-json",
  "--include-partial-messages",
  "--max-turns", "200",
];
// stdin에 프롬프트 write
child.stdin?.write(prompt);
child.stdin?.end();
```

**프롬프트 조립 파이프라인** (buildTaskExecutionPrompt):
```
학습된 스킬 블록 (SQLite에서 로드)
+ 태스크 세션 메타데이터 (sessionId, owner, provider)
+ 프로젝트 구조 (파일 트리, 최대 4000자)
+ 최근 변경 (git diff)
+ 태스크 제목 + 설명
+ 워크플로 팩 규칙 (태스크 유형별)
+ Continuation 컨텍스트 (재실행 시 이전 미해결 체크리스트)
+ 대화 히스토리
+ 부서 프롬프트 (공유 규칙)
+ MVP 코드 리뷰 정책
+ 에이전트 메타데이터 (이름, 역할, 부서)
+ Worktree 격리 안내
```

**에러/타임아웃 전략**:
- **Idle 타임아웃**: 출력 없이 15분 경과 시 kill (환경변수로 설정 가능)
- **Hard 타임아웃**: 전체 실행 시간 제한 (기본 비활성, 설정 가능)
- **Graceful kill**: SIGTERM → 1.2s 대기 → SIGKILL (Windows: taskkill /T /F)
- **Stale 프로세스 감지**: PID alive 체크 → 죽은 프로세스 정리 후 태스크 재실행

**stream-json 파싱 (서브태스크 감지)**:
```typescript
// Claude Code의 Task 도구 사용 감지
if (j.type === "tool_use" && j.tool === "Task") → 서브태스크 생성
if (j.type === "tool_result" && j.tool === "Task") → 서브태스크 완료
// Codex의 멀티 에이전트 감지
if (j.type === "item.started" && item.tool === "spawn_agent") → 서브태스크 생성
// Gemini의 계획 감지
if (content.match(/\{"subtasks"\s*:\s*\[.*?\]\}/s)) → 계획에서 서브태스크 추출
```

**핵심 설계 결정**:
- 태스크별 git worktree 격리 (`climpire/{taskId}` 브랜치)
- SQLite로 상태 관리 (에이전트, 태스크, 서브태스크, 스킬, 로그)
- ANSI 이스케이프 + 스피너 + CLI 노이즈 제거 레이어
- 출력 중복 제거 (슬라이딩 윈도우 기반)
- Continuation 컨텍스트로 재실행 시 불필요한 탐색 방지
- `CLAUDECODE`/`CLAUDE_CODE` 환경변수 삭제 → 중첩 세션 감지 방지
- `CI=1`, `NO_COLOR=1`, `TERM=dumb` 설정 → 깨끗한 출력

### 3.3 두 프로젝트 비교

| 영역 | Uni | claw-empire |
|------|-----|-------------|
| **호출 플래그** | `-p` (short) | `--print` (long) — 동일 옵션 |
| **도구 제한** | `--allowedTools` 명시 | 제한 없음 (전체 허용) |
| **max-turns** | 미지정 (기본값) | 200 |
| **프로바이더** | Claude 단일 | Claude + Codex + Gemini + OpenCode + Kimi + HTTP |
| **격리** | 없음 (프로젝트 루트) | 태스크별 git worktree |
| **재시도** | Signal kill 시 3회 (백오프) | 재시도 없음 (timeout kill) |
| **상태 저장** | JSONL 파일 | SQLite |
| **스킬 시스템** | tsx 스크립트 (정적) | 에이전트가 학습 (동적) |
| **서브태스크** | 없음 | stream-json에서 tool_use 감지 → 자동 생성 |

### 3.4 flow Phase 5에 적용할 패턴

| 패턴 | 출처 | flow 적용 방안 |
|------|------|---------------|
| **CLI spawn + stream-json** | 둘 다 | Claude CLI 백엔드: `claude -p --output-format stream-json` spawn |
| **stdin 프롬프트 전달** | 둘 다 | Claude CLI: stdin write → close. Copilot ACP: `session/prompt` |
| **Graceful kill + 재시도** | Uni | Claude CLI: stdin close → SIGTERM/Kill → 최대 3회 백오프. Copilot: `session/cancel` + kill |
| **Idle + Hard 이중 타임아웃** | claw-empire | `CliBackendOptions`에 IdleTimeout + HardTimeout (두 백엔드 공통) |
| **프롬프트 조립 파이프라인** | 둘 다 | PromptBuilder (백엔드 독립): Spec JSON + Activity + ReviewRequests → 프롬프트 텍스트 |
| **Continuation 컨텍스트** | claw-empire | 재시도 시 이전 실행 Activity를 AgentInput에 포함하여 중복 탐색 방지 |
| **ANSI/노이즈 제거** | claw-empire | Claude CLI: stream-json 파싱으로 회피. Copilot ACP: NDJSON 구조화 |
| **환경변수 정리** | claw-empire | `CLAUDECODE` 삭제, `CI=1`, `NO_COLOR=1` 설정 (두 백엔드 공통) |
| **Worktree 격리** | claw-empire | Developer: spec별 git worktree, cwd로 전달 (두 백엔드 공통) |
| **도구 권한 제어** | ACP/Claude | Copilot: `request_permission`. Claude: `--allowedTools` 화이트리스트 |
| **역할별 백엔드 교체** | flow 고유 | ICliBackend로 통합, 설정 변경만으로 역할별 백엔드 교체 + 비교 |

## 4. Phase 5 범위

로드맵 §2.5 기준:

> 더미 agent를 실제 prompt 기반 agent로 점진 교체한다.
> 복잡한 spec이 아니라 짧고 수렴이 빠른 fixture부터 시작한다.

**Phase 5의 핵심 산출물**:
1. **ICliBackend 인터페이스 + 두 백엔드** — Claude CLI + Copilot ACP, 역할별 교체 가능
2. **실제 IAgentAdapter 구현** (최소 1개 역할 — SpecValidator, Claude CLI 기반)
3. **PromptBuilder + OutputParser** — 백엔드 독립적 프롬프트 변환/응답 파싱
4. 동일 fixture 반복 실행 시 수렴 경향 확인 (백엔드별 비교)
5. Agent 출력 schema validation
6. Agent 오류 시 runner recovery 검증 (재시도 + 타임아웃)

## 5. 확정 결정

### 5.1 아키텍처 통합 전략

**결정**: Phase 5는 `flow-core`에 구현한다. `flow-cli`는 당분간 독립 구현으로 유지한다.

적용 원칙:
- 실제 agent, backend, prompt parser, recovery 테스트는 모두 `flow-core` 기준으로 설계한다.
- `flow-cli`의 `CopilotService`에서는 실행 경로 해석, Windows `.ps1` 실행, timeout 및 에러 분류 방식만 참조한다.
- `flow-cli`를 `IAgentAdapter`로 브릿지하는 어댑터는 만들지 않는다.
- `flow-cli` 전체 마이그레이션은 Phase 5 완료 후 별도 단계로 분리한다.

이유:
1. `flow-core`는 이미 `IAgentAdapter`, `RuleEvaluator`, review loop, archive 흐름까지 검증된 계약을 갖고 있다.
2. Phase 5의 핵심 위험은 런타임 통합보다 LLM 출력 안정화이므로, 상태 기계가 정리된 계층에서 시작하는 편이 안전하다.
3. `flow-cli`를 먼저 통합하면 범위가 급격히 커지고, 실제 agent MVP 착수가 지연된다.

### 5.2 LLM 백엔드 선택 — ✅ 결정됨: 멀티 백엔드 (Claude CLI + Copilot ACP)

**결정**: `ICliBackend` 인터페이스로 두 백엔드를 통합하고, 역할별로 교체 가능하게 한다.

- **Claude CLI** (`claude -p --output-format stream-json`): Planner, Architect, TestValidator, SpecValidator
- **Copilot CLI ACP** (`copilot --acp --stdio`): Developer
- 역할별 백엔드는 설정으로 교체 가능 — 비교 테스트하며 조율
- API 키 불필요 (둘 다 로컬 CLI 기반)
- 향후 OpenAI API, Gemini API 등 동일 인터페이스로 확장 가능

상세 아키텍처는 §2 참조.

추가 제약:
- Claude CLI는 stateless backend로 구현한다.
- Copilot backend는 ACP를 목표로 하되, 기존 `flow-cli`가 ACP 구현체를 제공하지 않는다는 점을 전제로 한다.
- Windows 환경에서는 `copilot` 명령이 `.ps1`로 해석될 수 있으므로, 백엔드 설정은 실행 파일명과 절대 경로를 모두 지원해야 한다.

### 5.3 첫 번째 실제 Agent 역할

**결정**: 첫 실제 agent는 `SpecValidator`로 한다.

이유:
1. Draft → Queued 진입 게이트와 Review → Active 최종 게이트를 동시에 검증할 수 있다.
2. `ProposedReviewRequest`를 포함한 Phase 4의 review loop 계약을 바로 실전 검증할 수 있다.
3. 코드 수정, git worktree, merge 충돌 처리 없이도 실제 LLM 연동 가치를 볼 수 있다.
4. 실패 시에도 기존 timeout/retry/review loop 회복 경로를 그대로 활용할 수 있다.

비결정 사항:
- Developer agent는 Phase 5a 범위에 넣지 않는다.
- Architect와 TestValidator는 SpecValidator 이후 동일 backend abstraction 위에 순차 확장한다.

### 5.4 Agent 프롬프트 구조

**결정**: PromptBuilder와 OutputParser는 backend 독립 컴포넌트로 두고, Phase 5a에서는 다음과 같이 고정한다.

- 시스템 프롬프트는 코드 내 상수로 시작한다.
- 입력은 `AgentInput`의 전체 `Spec` JSON, 최근 Activity 20건, 전체 `ReviewRequests`를 포함한다.
- 출력은 자유 텍스트가 아니라 JSON 블록 1개를 반드시 포함하도록 요구한다.
- OutputParser는 `CliResponse.ResponseText`에서 JSON 블록을 추출해 `AgentOutput`으로 변환한다.

보강 규칙:
- JSON 파싱 실패는 agent 성공으로 간주하지 않는다. `RetryableFailure` 또는 backend error로 환원한다.
- PromptBuilder는 역할별 섹션만 다르게 하고 envelope 형식은 공통으로 유지한다.
- 프롬프트 파일 외부화는 prompt tuning이 안정화된 뒤에만 도입한다.
- **`AgentOutput.BaseVersion` 자동 설정**: OutputParser가 `AgentInput.CurrentVersion`을 `AgentOutput.BaseVersion`에 복사한다. LLM에게 버전을 요구하지 않는다. 이유: FlowRunner는 `BaseVersion != spec.Version`이면 이벤트를 거부하므로, 이 값은 런타임이 보장해야 한다.

### 5.5 설정 관리

두 백엔드 모두 API 키 불필요 (로컬 CLI 기반). 향후 API 백엔드 추가 시 환경변수로 키 관리.

**설정 구조**: §2.7의 JSON 설정 참조.

**핵심 설정 항목**:
- `agentBackends`: 역할별 백엔드 매핑 (교체 가능)
- `backends.claude-cli`: command, idleTimeout, hardTimeout, maxRetries, allowedTools
- `backends.copilot-acp`: command, idleTimeout, hardTimeout, defaultMode
- 향후: `backends.openai-api`, `backends.gemini-api` (apiKeyEnv, model 등)

**저장 위치 및 주입 방식**:
- `BackendConfig`는 `RunnerConfig`에 넣지 않는다. `RunnerConfig`는 runner 루프 설정만 담당한다.
- `BackendRegistry`를 별도 클래스로 만들고, `IAgentAdapter` 구현체(CliSpecValidator 등)에 주입한다.
- FlowRunner는 기존대로 `IEnumerable<IAgentAdapter>`만 받는다. 백엔드 내부 구조를 모른다.
- 환경변수 오버라이드 지원.

```
FlowRunner ← IAgentAdapter[] ← CliSpecValidator ← BackendRegistry ← ICliBackend[]
                                                                       ├─ ClaudeCliBackend
                                                                       └─ CopilotAcpBackend
```

추가 결정:
- backend command는 단순 명령어와 절대 경로를 모두 허용한다.
- Windows에서는 `.ps1`와 `.bat` 실행 파일을 직접 실행하지 않고 적절한 호스트(`pwsh`, `cmd.exe`)로 감싸서 실행한다.
- `copilot-acp` 설정에는 추후 `commandPath` 또는 `launcher` 필드가 들어갈 수 있으며, 이는 기존 `flow-cli`의 Windows launcher 처리 경험을 반영한다.

### 5.6 Developer Agent 구현 방식

**결정**: Developer agent의 본격 구현은 Phase 5b로 미룬다. 다만 설계 제약은 지금 고정한다.

고정 제약:
1. Developer는 worktree 기반 격리를 전제로 한다.
2. 기본 backend는 Copilot ACP Code 모드다.
3. Claude CLI는 비교용 대체 backend로만 유지한다.
4. 빌드/테스트 실행 결과는 응답 텍스트에 요약하고, 상세 산출물은 evidence 경로에 저장하는 방향으로 확장한다.

Phase 5a에서 하지 않는 것:
- 구현 agent의 코드 수정 자동화
- merge conflict 자동 해결
- git commit/push 정책 정의

### 5.7 PartialEditApprove 처리 (Phase 4에서 미뤄진 항목)

Phase 4 결정 문서 §3.4에서 미뤄진 사항: 사용자가 검토 요청의 일부를 직접 수정 후 승인하는 경우.

**현재 상태**: `ReviewResponseType.PartialEditApprove`는 enum에 존재하지만, `editedPayload`를 SpecValidator가 읽고 후속 처리하는 로직은 없음.

**결정**: MVP에서는 `PartialEditApprove`를 `ApproveOption`과 동일하게 처리한다.

이유:
- 현재 UI나 CLI 입력 경로상 `editedPayload`를 안정적으로 생성할 사용처가 없다.
- Phase 5a의 목표는 review loop의 실제 LLM 판단 검증이지 사용자 편집 병합이 아니다.
- `editedPayload` 반영은 webservice가 생기는 Phase 6과 함께 다루는 편이 자연스럽다.

### 5.8 Crash Recovery (Daemon 재시작 시)

flow-core의 FlowRunner는 현재 crash recovery가 없다. Daemon이 죽으면 Running 상태의 assignment가 고아가 된다.

**현재 상태**: flow-cli의 `RecoverFromCrash`는 local state file (`.flow/spec-state.json`)을 사용. flow-core에는 해당 기능 없음.

**결정**: 별도 crash recovery 계층은 Phase 5에 추가하지 않는다.

근거:
- `FlowRunner.ProcessTimeouts()`가 stale assignment를 감지하고 `AssignmentTimedOut` → `AssignmentResumed` 경로를 이미 수행한다.
- 따라서 daemon 재시작 복구의 1차 책임은 assignment timeout 설정에 둔다.

운영 원칙:
- 실제 LLM agent timeout은 더미 agent보다 길게 재설정해야 한다.
- 장시간 세션을 쓰는 backend는 cancellation 후 kill fallback을 가져야 한다.

## 6. 추가 질문에 대한 답변

Phase 5 범위를 정리하면서 함께 확정한 보조 결정들이다.

### 6.1 멀티 Runner 동시성

**답변**: Phase 5는 단일 runner 전제로 충분하다. 멀티 runner는 지원 목표에 넣지 않는다.

이유:
- 현재 CAS는 저장 충돌은 막지만, dispatch 경쟁 자체를 조정하지는 않는다.
- 실제 agent 도입 초기에는 출력 안정화와 recovery 검증이 우선이며, 멀티 runner까지 동시에 열면 원인 분리가 어려워진다.

후속 원칙:
- 멀티 runner가 필요해지면 agent/backend 계층이 아니라 assignment 획득 단계에 spec-level lease를 추가한다.
- lease는 store 또는 별도 coordinator 계층에서 해결하고, `IAgentAdapter` 계약에는 넣지 않는다.

### 6.2 Agent 실행 시간

**답변**: Phase 5는 순차 실행을 유지한다. 병렬 agent dispatch는 도입하지 않는다.

이유:
- 현재 runner는 한 cycle 안에서 상태 변경, side effect, timeout 처리를 단순한 직렬 모델로 가정한다.
- 병렬 호출을 넣으면 assignment 저장, activity 순서, review request supersede 타이밍을 다시 설계해야 한다.

운영 기준:
- `MaxSpecsPerCycle`과 timeout 값을 보수적으로 설정한다.
- 병목이 확인되면 Phase 5b 이후 “spec 단위 병렬, spec 내부 직렬” 모델을 후보로 검토한다.

### 6.3 Evidence 저장 계약

**답변**: Evidence는 spec 디렉토리 내부에 저장한다. 스펙 파일과 동일 경로 체계를 사용한다.

결정:
- 저장 경로는 spec 디렉토리 내부 `~/.flow/projects/<projectId>/specs/<specId>/evidence/<runId>/`로 둔다.
- spec, assignment, review request, evidence 모두 같은 spec 디렉토리 트리에 공존한다.
- AgentOutput에 `EvidenceRefs` 목록을 추가하고, runner가 manifest를 저장한다.

이유:
- evidence를 workspace(`docs/evidence`)에 두면 스펙 파일 경로와 분리되어 관리가 복잡해진다.
- spec 디렉토리 내부에 두면 archive/delete 시 evidence도 함께 이동·삭제되어 라이프사이클이 일치한다.
- `FileFlowStore`의 기존 경로 체계(`SpecDir/<specId>/`)를 그대로 확장한다.

저장 구조:
```
~/.flow/projects/<projectId>/specs/<specId>/
  ├── spec.json
  ├── assignments/
  ├── review-requests/
  ├── activity/
  └── evidence/
       └── <runId>/
            ├── manifest.json        ← EvidenceRef 목록
            └── (산출물 파일들)
```

Evidence 계약:
- `EvidenceRef { Kind, RelativePath, Summary }` 목록을 `AgentOutput.EvidenceRefs`에 추가
- `RelativePath`는 evidence 디렉토리 기준 상대 경로
- runner는 manifest 저장 + existence check만 수행하고, 내용 해석은 하지 않음
- `IEvidenceStore` 인터페이스로 저장/조회를 추상화한다

### 6.4 flow-cli 마이그레이션 시점

**답변**: flow-cli 마이그레이션은 Phase 5 종료 조건에 넣지 않는다. 다만 Phase 6 전에 방향은 확정한다.

권장 방향:
- Phase 5에서는 `flow-core`에서 backend/agent abstraction을 검증한다.
- Phase 5 종료 후, `flow-cli`를 점진 이관할지 새 CLI를 얹을지 비교 결정한다.
- Webservice는 가능하면 `flow-core` API를 직접 사용하고, `flow-cli` 의존은 줄인다.

현재 판단:
- 기존 `flow-cli`의 Copilot 구현은 운영상 가치가 있지만 상태 모델이 달라 직접 재사용 비용이 크다.
- 따라서 “flow-cli를 유지한 채 일부 공유”보다 “flow-core를 기준 런타임으로 승격”하는 쪽이 더 일관적이다.

## 7. 구현 계획

Phase 5를 3단계로 나눔:

### 7.0 Phase 5 인프라: ICliBackend + 두 백엔드

1. **`ICliBackend` 인터페이스** 정의 (§2.3)
   - `RunPromptAsync(prompt, options, ct)` → `CliResponse`
   - `CliBackendOptions`: WorkingDirectory, AllowFileEdits, IdleTimeout, HardTimeout
   - `CliResponse`: ResponseText, Success, ErrorMessage, StopReason

2. **`ClaudeCliBackend : ICliBackend`** 구현
   - `claude -p --output-format stream-json --dangerously-skip-permissions` spawn
   - stdin에 프롬프트 write → stdout stream-json 수집
   - `--allowedTools` = `CliBackendOptions.AllowedTools` 매핑
   - stream-json `type: "result"` → `CliResponse.ResponseText` 변환
   - 크로스 플랫폼 kill: `ProcessKiller.GracefulKillAsync` (Windows: Kill tree, macOS/Linux: SIGTERM→SIGKILL)
   - 재시도 최대 3회 (30s/60s 백오프)
   - 환경변수 정리: `CI=1`, `NO_COLOR=1`, `CLAUDECODE` 삭제

3. **`CopilotAcpBackend : ICliBackend`** 구현
   - `copilot --acp --stdio` 장기 실행 프로세스 관리
   - ACP JSON-RPC 클라이언트: initialize, session/new, session/prompt
   - session/update → 응답 텍스트 수집
   - request_permission 핸들러 (AllowFileEdits 기반 자동 응답)
   - session/cancel + kill fallback 타임아웃
   - Windows 실행 파일 해석: 절대 경로, `.ps1`, `.bat` launcher 지원

4. **`BackendRegistry`** — 역할별 백엔드 매핑 (설정 기반, §2.7)

5. **공통 컴포넌트**:
   - `PromptBuilder`: AgentInput → 프롬프트 텍스트 (백엔드 독립)
   - `OutputParser`: CliResponse.ResponseText → AgentOutput (백엔드 독립)
     - `AgentOutput.BaseVersion` = `AgentInput.CurrentVersion` 자동 복사 (LLM이 버전을 모름)
   - `BackendConfig` 모델 (실행 경로, 타임아웃, 역할별 매핑, 역할별 AllowedTools)

### 7.1 Phase 5a: SpecValidator MVP

6. **`CliSpecValidator : IAgentAdapter`** 구현
   - BackendRegistry에서 specValidator용 ICliBackend 획득
   - PromptBuilder로 AgentInput → 프롬프트 변환
   - ICliBackend.RunPromptAsync → CliResponse
   - OutputParser로 AgentOutput 파싱 (ProposedEvent + ProposedReviewRequest)
7. AC Precheck 프롬프트 설계 (Draft 상태)
8. Spec Validation 프롬프트 설계 (Review 상태)
9. Fixture 기반 수렴 테스트 (Claude CLI로 실행, Copilot으로 교체하여 비교)
10. 오류/timeout recovery 테스트

명시적 비범위:
- evidence 저장
- Developer 구현 자동화
- flow-cli 마이그레이션

### 7.2 Phase 5b: 추가 Agent 확장

11. **`CliArchitect : IAgentAdapter`** 구현 (기본: Claude CLI)
12. **`CliTestValidator : IAgentAdapter`** 구현 (기본: Claude CLI)
13. **`CliDeveloper : IAgentAdapter`** 구현 (기본: Copilot ACP Code 모드, §5.6)
14. **`CliPlanner : IAgentAdapter`** 구현 (기본: Claude CLI)
15. 역할별 백엔드 교체 비교 테스트 (Claude ↔ Copilot 성능/품질 비교)
16. Evidence manifest 계약 추가 + 저장 경로 검증
17. 전체 파이프라인 end-to-end fixture 실행
18. 프롬프트 튜닝 (수렴 경향 + 백엔드별 최적화)

## 8. 테스트 전략

로드맵 §2.5 기준 테스트를 Phase 5a와 5b로 나눠 적용한다.

| # | 테스트 | 설명 |
|---|--------|------|
| 1 | 동일 fixture 반복 수렴 | 같은 spec을 3회 실행 시 같은 최종 상태 도달 |
| 2 | Agent 출력 schema validation | LLM 반환값이 AgentOutput 계약 준수 |
| 3 | Agent 오류 시 runner recovery | LLM 타임아웃/오류 시 RetryableFailure 반환 → runner 복구 |
| 4 | 프롬프트 변경 비파괴 | 프롬프트 수정이 상태 규칙과 schema를 깨지 않는 것 확인 |
| 5 | 백엔드 전환 비교 | 동일 fixture를 Claude/Copilot backend로 교체 실행해 결과 안정성 비교 |
| 6 | 비용/토큰 모니터링 | CLI/API별 호출량, 실행 시간, 재시도 횟수 추적 |

Phase 5b 추가 테스트:

| # | 테스트 | 설명 |
|---|--------|------|
| 7 | Evidence 첨부 검증 | Agent가 생성한 evidence manifest가 저장 경로와 일치하는지 확인 |
| 8 | Developer worktree 격리 검증 | spec별 worktree와 backend cwd가 일치하는지 확인 |

## 9. 위험 요소

| 위험 | 완화 |
|------|------|
| LLM 출력이 AgentOutput 계약을 깨뜨림 | OutputParser에서 JSON 블록 파싱 + 파싱 실패 시 RetryableFailure |
| 프롬프트 변경이 상태 기계를 우회 | RuleEvaluator가 최종 게이트, agent는 event만 제안 |
| ACP 프로토콜 비호환 (버전 불일치) | `initialize`에서 protocolVersion 검증, 실패 시 fallback 또는 오류 |
| Copilot CLI ACP 기능 미완성 | ACP는 초기 단계 — Claude CLI로 fallback 가능 (백엔드 교체) |
| Claude CLI와 Copilot 간 출력 형식 차이 | OutputParser를 백엔드 독립적으로 설계, 공통 CliResponse 계약으로 통일 |
| 수렴 불안정 (같은 입력에 다른 결과) | 결정적 프롬프트, 수렴 테스트 자동화, 백엔드별 비교 |
| 세션 메모리 누수 (Copilot ACP 장기 실행) | 세션별 프롬프트 제한, 주기적 세션 재생성 |
| 백엔드 추가 시 ICliBackend 계약 부족 | MVP에서 두 백엔드로 검증 후, 향후 API 백엔드 추가 시 인터페이스 확장 |
| flow-cli와의 코드 중복 | Phase 5는 flow-core만 대상, 마이그레이션은 별도 단계 |
| Windows에서 Copilot 실행 경로 해석 실패 | command/path 분리 설정 + `.ps1`/`.bat` launcher 처리 + 절대 경로 지원 |
| Evidence 범위가 MVP를 지연시킴 | Phase 5a에서 제외하고 Phase 5b 최소 계약으로 분리 |
