# CliArchitect, CliDeveloper, CliTestValidator 설계 문서

## 1. Context

현재 실제 LLM 기반 agent는 `CliSpecValidator`만 구현되어 있고,
Architect, Developer, TestValidator, Planner는 아직 더미 구현이다.

이 문서는 Planner를 제외한 나머지 3개 CLI agent의 실제 설계를 다룬다.
Planner는 별도 문서인 `cli-planner-design.md`를 따른다.

세 agent 모두 기본 구조는 동일하다.

```text
BackendRegistry -> ICliBackend -> PromptBuilder -> OutputParser -> AgentOutput
```

다만 Planner와 달리, 이 3개 agent는 현재 `AgentOutput` 계약만으로도 구현 가능하다.
즉 `proposedEvent`, `summary`, `evidenceRefs`만으로 충분하며,
Planner처럼 spec 본문 patch payload를 추가로 요구하지 않는다.

## 2. 현재 코드 기준 사실

문서를 구현 지침으로 쓰려면 먼저 현재 runtime의 한계를 정확히 적어야 한다.

### 2.1 이미 구현된 부분

- `DispatchTable`은 아래 상태에서 agent dispatch를 수행한다.
  - `ArchitectureReview/Pending -> Architect`
  - `Implementation/Pending -> Developer`
  - `TestValidation/Pending -> TestValidator`
- `FlowRunner.ProcessAgent()`는 위 세 상태에서 2-pass로 동작한다.
  - 먼저 `AssignmentStarted`
  - 다음 agent 호출
- `PromptBuilder`는 worktree가 있으면 경로와 브랜치를 프롬프트에 포함한다.
- `OutputParser`는 `evidenceRefs`를 파싱하고 상대 경로만 허용한다.
- `FlowRunner`는 CAS 커밋이 성공한 뒤 evidence manifest를 저장한다.

### 2.2 아직 없는 부분

- `CliArchitect`, `CliDeveloper`, `CliTestValidator` 구현이 없다.
- `flow-console`은 backend config를 읽지 않으며 `BackendRegistry`를 구성하지 않는다.
- worktree를 생성하거나 정리하는 runtime 컴포넌트가 없다.
- assignment 생성 시 worktree를 자동 할당하는 로직이 없다.
- Developer/TestValidator가 어떤 테스트 명령을 실행할지 지정하는 계약이 없다.

### 2.3 현재 runtime의 실제 제약

이 제약들은 문서에서 명시적으로 다뤄야 한다.

1. `StoreFactory`에서 Dummy agent를 그냥 CLI agent로 바꾸는 것만으로는 동작하지 않는다.
   backend config 로딩과 `BackendRegistry` 생성이 먼저 필요하다.

2. Developer를 worktree 없이 활성화하면 위험하다.
   현재 `CliBackendOptions.WorkingDirectory`는 `Assignment.Worktree?.Path`만 사용하므로,
   worktree가 없으면 격리되지 않은 작업 디렉토리에서 수정이 일어날 수 있다.

3. `FlowRunner.HandleAgentResult()`의 retry/terminal-failure 처리는 아직 agent 일반화가 덜 되어 있다.
   현재 로직은 실패 시 `SpecValidationFailed` 또는 `CancelRequested`에 기대는 경향이 있어,
   Architect/Developer/TestValidator 전용 실패 정책으로 보기 어렵다.

따라서 이 문서는 단순 agent 추가 문서가 아니라,
runtime 선행조건을 포함한 실제 도입 설계로 정리한다.

## 3. 설계 결정

### 3.1 Architect는 read-only 분석 agent로 둔다

- `AllowFileEdits = false`
- `AllowedTools = Read, Glob, Grep`
- `Bash`는 허용하지 않는다.

Architect는 코드와 구조를 읽어 판단만 하면 된다.
"읽기 전용 Bash"를 런타임에서 강제할 수 없기 때문에 v1에서는 제외하는 편이 안전하다.

### 3.2 Developer는 worktree 필수 agent로 둔다

- `AllowFileEdits = true`
- `AllowedTools = Read, Write, Edit, Glob, Grep, Bash`
- worktree가 없으면 실행하지 않고 실패 처리한다.

Developer를 main workspace에서 직접 실행하는 것은 현재 구조상 너무 위험하다.
따라서 `CliDeveloper`는 worktree provisioning이 준비되기 전에는 기본 활성화 대상이 아니다.

### 3.3 TestValidator는 동일 worktree에서 검증한다

- `AllowFileEdits = false`
- `AllowedTools = Read, Glob, Grep, Bash`
- Developer가 사용한 worktree를 그대로 사용한다.

TestValidator는 테스트 실행이 필요하므로 `Bash`가 필요하다.
다만 코드 수정은 금지한다.

### 3.4 Developer의 자동 커밋은 v1에서 하지 않는다

기존 문서의 "변경 사항을 커밋하세요"는 현재 runtime과 맞지 않는다.

이유:

- git commit lifecycle을 runner가 관리하지 않는다.
- commit 해시를 시스템 계약으로 검증하지 않는다.
- 실패/재시도/rollback 시 commit 정책이 아직 없다.

따라서 v1 Developer는 commit이 아니라 다음에 집중한다.

- 코드 수정
- 테스트 실행
- evidence 생성
- `ImplementationSubmitted` 제안

### 3.5 evidence 저장 위치는 spec evidence 디렉토리로 고정한다

evidence는 worktree 내부가 아니라 spec 디렉토리 내부 evidence 트리를 기준으로 보고한다.

```text
~/.flow/projects/<projectId>/specs/<specId>/evidence/<runId>/
```

`EvidenceRef.RelativePath`는 위 evidence 디렉토리 기준 상대 경로다.

### 3.6 테스트 범위는 기본적으로 targeted run으로 둔다

TestValidator가 항상 전체 repo regression을 돌리는 것은 v1에서 과하다.

기본 정책:

- spec에 연결된 테스트 또는 관련 테스트 우선 실행
- 프로젝트별 기본 검증 명령은 설정으로 주입
- 전체 regression은 `RiskLevel.High/Critical` 또는 명시적 설정일 때만 선택

### 3.7 worktree provisioning은 FlowRunner가 주관하되 별도 인터페이스로 분리한다

현재 flow-core에 worktree 로직이 없지만, 책임 위치는 runner가 가장 자연스럽다.

권장 인터페이스:

```csharp
public interface IWorktreeProvisioner
{
    Task<AssignmentWorktree> CreateAsync(Spec spec, CancellationToken ct = default);
    Task CleanupAsync(AssignmentWorktree worktree, CancellationToken ct = default);
}
```

FlowRunner 또는 assignment 생성 경로가 이를 호출해서 Developer/TestValidator assignment에 worktree를 부여한다.

## 4. 공통 아키텍처

세 agent의 기본 구조는 `CliSpecValidator`와 동일하다.

```csharp
public sealed class CliArchitect : IAgentAdapter
{
    private readonly BackendRegistry _registry;
    private readonly PromptBuilder _promptBuilder;
    private readonly OutputParser _outputParser;

    public AgentRole Role => AgentRole.Architect;

    public async Task<AgentOutput> ExecuteAsync(AgentInput input, CancellationToken ct = default)
    {
        var backend = _registry.GetBackend(AgentRole.Architect);
        if (backend == null)
        {
            return new AgentOutput
            {
                Result = AgentResult.TerminalFailure,
                BaseVersion = input.CurrentVersion,
                Message = "no backend configured for Architect"
            };
        }

        var definition = _registry.GetDefinition(AgentRole.Architect);
        var prompt = _promptBuilder.BuildPrompt(input, AgentRole.Architect);

        var options = new CliBackendOptions
        {
            WorkingDirectory = input.Assignment.Worktree?.Path,
            AllowFileEdits = false,
            AllowedTools = definition?.AllowedTools,
            IdleTimeout = TimeSpan.FromSeconds(definition?.IdleTimeoutSeconds ?? 300),
            HardTimeout = TimeSpan.FromSeconds(definition?.HardTimeoutSeconds ?? 600)
        };

        var response = await backend.RunPromptAsync(prompt, options, ct);
        return _outputParser.Parse(response, input) ?? new AgentOutput
        {
            Result = AgentResult.RetryableFailure,
            BaseVersion = input.CurrentVersion,
            Message = "failed to parse backend response"
        };
    }
}
```

차이점은 agent별 role, timeout, tool 권한, worktree 필수 여부뿐이다.

## 5. CliArchitect

### 5.1 역할

- acceptance criteria의 기술적 구현 가능성을 검토한다.
- 구조적 위험, 의존성, 범위 적절성을 검토한다.
- 상태를 직접 바꾸지 않고 `ArchitectReviewPassed` 또는 `ArchitectReviewRejected`만 제안한다.

### 5.2 호출 조건

| 상태 | 처리 상태 | 비고 |
| --- | --- | --- |
| `ArchitectureReview` | `Pending` | runner가 2-pass로 `AssignmentStarted` 후 호출 |

실제 흐름:

1. `Queued/Pending`
2. `AssignmentStarted`
3. `ArchitectureReview/Pending`
4. Architect dispatch

### 5.3 프롬프트 방향

```text
# 역할: Architect — 아키텍처 리뷰

당신은 소프트웨어 아키텍트입니다.
이 스펙이 현재 코드베이스에서 무리 없이 구현 가능한지 검토하세요.

# Spec 정보
{spec JSON}

# 최근 활동 이력
{activity log}

# 검토 기준

1. acceptance criteria가 기술적으로 실현 가능한가?
2. 구조 변경 범위가 과도하지 않은가?
3. 의존성이 정확한가?
4. 위험도를 더 높게 잡아야 하는가?
5. 범위가 AI 단일 구현 사이클에 적합한가?

# 지시사항

- 관련 코드와 파일 구조를 읽고 판단하세요.
- 문제 없으면 `architectReviewPassed`를 제안하세요.
- 문제가 있으면 `architectReviewRejected`를 제안하고 summary에 구체적 사유를 적으세요.
```

### 5.4 도구 및 권한

| 설정 | 값 |
| --- | --- |
| `AllowFileEdits` | `false` |
| `AllowedTools` | `Read`, `Glob`, `Grep` |
| `HardTimeout` | 600초 |
| `IdleTimeout` | 300초 |
| `WorkingDirectory` | worktree가 있으면 해당 경로, 없으면 null |

Architect는 worktree가 없어도 실행 가능하다.
읽기 전용 분석만 수행하기 때문이다.

### 5.5 반려 후 흐름

`ArchitectReviewRejected` 이후 흐름은 Planner 문서와 동일하다.

```text
ArchitectReviewRejected
-> RuleEvaluator가 Planner assignment 생성
-> Planner가 DraftUpdated 제안
-> Architect 재검토
```

단, 이 루프가 실제로 동작하려면 Planner dispatch 문제를 먼저 해결해야 한다.

## 6. CliDeveloper

### 6.1 역할

- acceptance criteria를 만족하는 코드를 구현한다.
- 필요한 테스트를 추가하거나 수정한다.
- 구현 결과를 evidence와 summary로 보고하고 `ImplementationSubmitted`를 제안한다.

### 6.2 호출 조건

| 상태 | 처리 상태 | 비고 |
| --- | --- | --- |
| `Implementation` | `Pending` | worktree가 할당된 경우에만 실행 |

실제 흐름:

1. `Queued/Pending` 또는 `ArchitectureReview/Pending`
2. `AssignmentStarted`
3. `Implementation/Pending`
4. Developer dispatch

### 6.3 worktree 전제

기존 문서의 "FlowRunner가 worktree를 할당한다"는 현재 코드 기준 사실이 아니다.
지금은 `Assignment.Worktree` 모델만 있고 이를 채우는 런타임이 없다.

따라서 v1 설계는 아래처럼 고정한다.

- `CliDeveloper`는 worktree가 없으면 실행하지 않는다.
- worktree provisioning은 `IWorktreeProvisioner`가 책임진다.
- `FlowRunner`가 Developer assignment 생성 시 provisioner를 통해 worktree를 붙인다.

### 6.4 프롬프트 방향

```text
# 역할: Developer — 구현

당신은 소프트웨어 개발자입니다.
스펙의 acceptance criteria를 만족하도록 코드를 구현하세요.

# Spec 정보
{spec JSON}

# 최근 활동 이력
{activity log}

# 작업 디렉토리
경로: {worktree.Path}
브랜치: {worktree.Branch}

# 지시사항

1. 관련 코드를 읽고 구조를 파악하세요.
2. acceptance criteria를 만족하도록 구현하세요.
3. 필요한 테스트를 추가하거나 수정하세요.
4. 가능한 범위에서 관련 테스트를 실행하세요.
5. 변경 내용과 테스트 결과를 evidence로 보고하세요.

# 구현 원칙

- 최소 변경 우선
- 기존 스타일 유지
- 불필요한 리팩터링 금지
- 커밋은 하지 않음
```

### 6.5 evidence 보고

Developer는 commit hash 대신 다음 evidence를 우선 보고한다.

- `test-result`: 실행한 테스트 결과
- `diff`: 변경 파일 목록 또는 요약 파일
- `build-log`: 빌드 로그가 있으면 포함

예시:

```json
{
  "proposedEvent": "implementationSubmitted",
  "summary": "핵심 AC를 구현하고 관련 테스트를 추가했습니다.",
  "evidenceRefs": [
    { "kind": "test-result", "relativePath": "test-output.txt", "summary": "12 tests passed" },
    { "kind": "diff", "relativePath": "changed-files.txt", "summary": "3 files changed" }
  ]
}
```

### 6.6 도구 및 권한

| 설정 | 값 |
| --- | --- |
| `AllowFileEdits` | `true` |
| `AllowedTools` | `Read`, `Write`, `Edit`, `Glob`, `Grep`, `Bash` |
| `WorkingDirectory` | worktree 경로 필수 |
| `HardTimeout` | 1800초 |
| `IdleTimeout` | 600초 |

### 6.7 재작업 흐름

`SpecValidationReworkRequested` 또는 `TestValidationRejected`가 발생하면
기존 규칙대로 다시 `Implementation`으로 돌아간다.

다만 retry/terminal-failure의 공통 처리는 먼저 일반화해야 한다.
지금 로직은 SpecValidator 중심으로 되어 있어 Developer 전용 실패 정책으로 보기 어렵다.

## 7. CliTestValidator

### 7.1 역할

- Developer가 제출한 구현이 테스트 측면에서 적합한지 검증한다.
- 테스트 존재 여부, 적합성, 통과 여부를 확인한다.
- `TestValidationPassed` 또는 `TestValidationRejected`만 제안한다.

### 7.2 호출 조건

| 상태 | 처리 상태 | 비고 |
| --- | --- | --- |
| `TestValidation` | `Pending` | Developer가 사용한 worktree를 그대로 사용 |

실제 흐름:

1. Developer가 `ImplementationSubmitted` 제안
2. `RuleEvaluator`가 `TestValidation`으로 전이
3. `AssignmentStarted`
4. `TestValidation/Pending`
5. TestValidator dispatch

### 7.3 프롬프트 방향

```text
# 역할: Test Validator — 테스트 검증

당신은 테스트 검증 전문가입니다.
Developer가 제출한 구현이 acceptance criteria를 충분히 검증하는지 판단하세요.

# Spec 정보
{spec JSON}

# 최근 활동 이력
{activity log}

# 작업 디렉토리
경로: {worktree.Path}
브랜치: {worktree.Branch}

# 검증 기준

1. 각 AC를 검증하는 테스트가 있는가?
2. 테스트가 AC 의도를 정확히 검증하는가?
3. 실행 결과가 통과하는가?
4. 필요한 최소 regression이 포함되었는가?

# 지시사항

- 관련 테스트를 먼저 찾으세요.
- 기본적으로 targeted test를 우선 실행하세요.
- 필요할 때만 범위를 넓히세요.
- 부족하거나 실패하면 `testValidationRejected`와 구체적 사유를 반환하세요.
```

### 7.4 evidence 보고

TestValidator evidence는 아래가 적절하다.

- `test-result`
- `coverage`가 있으면 선택적으로 포함
- `build-log` 또는 `validation-log`가 필요하면 포함

### 7.5 도구 및 권한

| 설정 | 값 |
| --- | --- |
| `AllowFileEdits` | `false` |
| `AllowedTools` | `Read`, `Glob`, `Grep`, `Bash` |
| `WorkingDirectory` | worktree 경로 필수 |
| `HardTimeout` | 900초 |
| `IdleTimeout` | 300초 |

### 7.6 반려 후 흐름

`TestValidationRejected`가 수용되면 규칙상 다시 `Implementation`으로 돌아가고,
Developer가 같은 worktree 문맥에서 재작업을 이어가는 방향이 가장 자연스럽다.

이 역시 worktree lifecycle 계약이 먼저 고정되어야 안정적으로 동작한다.

## 8. runtime 선행 작업

이 문서 기준으로 실제 구현을 시작하기 전에 다음 선행 작업이 필요하다.

### 8.1 필수

1. `flow-console` 또는 다른 진입점에서 backend config를 읽고 `BackendRegistry`를 구성한다.
2. `HandleAgentResult()`의 retry/terminal-failure 처리를 agent 일반화한다.
3. `IWorktreeProvisioner`와 Developer/TestValidator용 worktree 할당 경로를 추가한다.

### 8.2 권장

4. 프로젝트별 테스트 실행 명령을 설정 파일로 분리한다.
5. Developer/TestValidator evidence 파일 생성 규칙을 샘플과 함께 고정한다.

## 9. 구현 파일 구조

```text
tools/flow-core/
  Agents/Cli/
    CliArchitect.cs
    CliDeveloper.cs
    CliTestValidator.cs
    PromptBuilder.cs
  Runner/
    IWorktreeProvisioner.cs

tools/flow-console/
  Services/
    StoreFactory.cs             # BackendRegistry + CLI agent wiring 추가

tools/flow-core.tests/
  CliArchitectTests.cs
  CliDeveloperTests.cs
  CliTestValidatorTests.cs
  FlowRunnerTests.cs            # retry/terminal-failure 일반화 검증
```

## 10. 구현 순서

가장 적절한 순서는 아래다.

1. runtime 선행 작업
   - backend wiring
   - generic failure handling
   - worktree provisioning 인터페이스
2. `CliArchitect`
   - 가장 단순하고 read-only라 위험이 낮다.
3. `CliDeveloper`
   - worktree 필수 조건을 만족한 뒤 구현한다.
4. `CliTestValidator`
   - Developer가 만든 worktree와 evidence 흐름을 재사용한다.
5. 통합 테스트
   - `Queued -> ArchitectureReview -> Implementation -> TestValidation -> Review` 파이프라인 검증

## 11. 최종 정리

기존 문서에서 바로잡아야 했던 핵심 오류는 네 가지다.

1. worktree는 현재 모델만 있을 뿐 runtime이 자동으로 할당하지 않는다.
2. `StoreFactory`에서 Dummy agent만 교체해도 동작한다는 가정은 틀리다.
3. Developer의 자동 커밋은 현재 계약 밖이다.
4. retry/terminal-failure 처리도 아직 세 agent에 맞게 일반화되어 있지 않다.

이 문서의 최종 방향은 다음과 같다.

- Architect는 read-only 분석 agent
- Developer는 worktree 필수 구현 agent
- TestValidator는 같은 worktree에서 targeted test를 실행하는 검증 agent

이 전제를 먼저 고정해야 세 agent를 실제 runtime에 안전하게 붙일 수 있다.

## 12. 보완 사항

### 12.1 Architect worktree 동작 명확화

Architect는 worktree 없이도 실행 가능하지만, worktree가 있으면 해당 경로를 `WorkingDirectory`로 사용한다.
이는 Developer가 이미 작업 중인 worktree에서 Architect가 코드를 읽을 수 있게 하기 위함이다.
단, ArchitectureReview는 Implementation 이전에 발생하므로 v1에서는 worktree 없이 실행되는 것이 일반적이다.

### 12.2 worktree lifecycle 범위

Developer와 TestValidator가 같은 worktree를 공유하므로 lifecycle은 아래처럼 고정한다:

- 생성 시점: Developer assignment 생성 시 `IWorktreeProvisioner.CreateAsync()` 호출
- 공유: TestValidator assignment에 같은 `AssignmentWorktree`를 복사
- 정리 시점: spec이 `Review`, `Completed`, `Failed`, `Archived` 중 하나로 전이될 때 cleanup
- 비정상 종료 시: runner 재시작 시 orphan worktree를 감지하고 정리 (crash recovery 확장)

### 12.3 retry 횟수와 terminal failure 정책

현재 `HandleAgentResult()`의 retry 로직을 agent별로 일반화할 때의 기본 정책:

| Agent | MaxRetries | Terminal failure 시 전이 |
| --- | --- | --- |
| Architect | 2 | `ArchitectReviewRejected` + Planner 연계 |
| Developer | 2 | `ImplementationFailed` → Failed |
| TestValidator | 2 | `TestValidationRejected` → Implementation 재작업 |

`RetryableFailure`가 MaxRetries를 초과하면 `TerminalFailure`로 격상한다.
이 정책은 `RunnerConfig` 또는 agent definition에서 설정 가능하게 둔다.

### 12.4 TestValidator의 테스트 명령 발견

v1에서 TestValidator가 어떤 테스트를 실행할지 결정하는 방법:

1. spec의 `AcceptanceCriteria[].RelatedTestIds`가 있으면 해당 테스트 우선
2. Developer evidence의 `test-result` kind에서 실행된 테스트 경로 참조
3. 위 정보가 없으면 프롬프트에서 LLM이 관련 테스트를 탐색하도록 유도
4. 프로젝트별 기본 테스트 명령은 `RunnerConfig.DefaultTestCommand`로 설정
