# Runner E2E Test Strategy

## 목적

Runner의 전체 handoff 흐름을 테스트한다.

- 테스트용 spec 생성
- Runner가 `queued -> working -> needs-review` 로 전이
- 테스트용 Copilot이 더미 구현 수행
- 자동 테스트 결과 동기화
- review loop 최종 판정
- 최종 상태가 `verified`, `done`, `needs-review` 로 분기

이 문서는 현재 구현을 크게 뜯지 않고도 만들 수 있는 테스트 구조와,
장기적으로 더 싸고 빠르게 유지할 수 있는 구조를 같이 정리한다.

## 권장 테스트 계층

전체 흐름을 한 테스트에 다 밀어 넣지 말고 4계층으로 나눈다.

### 1. 규칙 단위 테스트

대상:

- `SpecReviewEvaluator.ResolveDecision`
- `RunnerService.ApplyReviewAnalysis`
- `SpecRecordConditionReview`

목적:

- 상태 판정 규칙의 회귀를 가장 빠르게 검출
- 실패 사유와 metadata disposition 확인

이 계층은 이미 일부 존재한다.

### 2. 커맨드 통합 테스트

대상:

- `spec-append-review`
- `spec-record-condition-review`

목적:

- spec JSON 파일이 실제로 어떻게 바뀌는지 확인
- review metadata와 evidence 저장 확인

이 계층도 이미 있다.

### 3. Runner 시나리오 통합 테스트

대상:

- `RunnerService.RunOnceAsync()`

목적:

- 임시 git repo에서 worktree 생성
- 더미 Copilot 구현
- 자동 테스트 동기화
- `needs-review` handoff 확인

이 계층이 사용자가 말한 “테스트용 러너 + 테스트용 Copilot”의 핵심이다.

### 4. Review loop 시나리오 통합 테스트

대상:

- `RunOnceAsync()` 이후 review candidate 상태
- fake review action 반영 후 다음 cycle에서 최종 판정

목적:

- review loop가 유일한 최종 확정 지점인지 확인
- `verified`, `done`, `needs-review` 분기 확인

## 핵심 아이디어

현재 `RunnerService`는 Copilot, git, 자동 테스트를 실제 프로세스로 호출한다.
그래도 테스트는 가능하다.

방법은 다음 조합이다.

- 임시 디렉터리에 최소 git repo 생성
- 최소 `.sln` 또는 `.csproj` 생성
- `docs/specs/*.json` 테스트 스펙 생성
- `RunnerConfig.CopilotCliPath` 를 테스트용 `.ps1` 로 지정
- fake Copilot 스크립트가 worktree 파일 수정 또는 spec 파일 metadata 갱신 수행
- 필요하면 TRX 파일을 직접 만들어 test sync 경로까지 태움

즉 “실제 Copilot” 대신 “Copilot CLI와 같은 인자 규약을 받는 테스트 스크립트”를 쓴다.

## 가장 작은 현실적 구조

### Test Fixture

`RunnerEndToEndFixture` 같은 헬퍼를 만든다.

역할:

- temp repo 생성
- git init + 최초 commit
- `docs/specs`, `docs/evidence`, `.flow` 생성
- fake copilot script 생성
- 테스트용 `.sln/.csproj` 생성
- `RunnerConfig` 기본값 구성

예시 필드:

```csharp
internal sealed class RunnerEndToEndFixture : IDisposable
{
    public string Root { get; }
    public string SpecsDir { get; }
    public string FakeCopilotScriptPath { get; }
    public RunnerConfig Config { get; }

    public SpecStore CreateStore() => new(Root);
    public RunnerService CreateRunner() => new(Root, Config);
}
```

### Fake Copilot 스크립트

CopilotService는 결국 아래 형태로 호출한다.

```powershell
pwsh -NonInteractive -File fake-copilot.ps1 -p "...prompt..." --model gpt-5-mini --yolo --autopilot
```

따라서 fake script는 아래만 맞추면 된다.

- `-p` 인자 수신
- 현재 작업 디렉터리 확인
- prompt 안의 spec id 또는 별도 scenario file 읽기
- 구현 단계면 worktree 파일 수정
- review 단계면 spec JSON 또는 review JSON 반영
- stdout에 적당한 성공 메시지 출력
- exit code 0 반환

가장 단순한 방식은 prompt를 정교하게 파싱하지 않고 `scenario.json` 을 같이 두는 것이다.

예시:

```json
{
  "specId": "F-900",
  "implement": {
    "writeFiles": [
      { "path": "src/App.cs", "content": "// implemented" }
    ],
    "reviewNote": "implemented"
  },
  "review": {
    "mode": "verified"
  }
}
```

script는 현재 worktree 또는 repo root 근처의 `scenario.json` 을 읽고 행동한다.

## 자동 테스트를 태우는 두 가지 방법

### 방법 A. 실제 최소 dotnet test 실행

임시 repo에 아주 작은 test project를 만든다.

- 장점: production 경로와 가장 비슷함
- 단점: 느림

구성:

- `App.sln`
- `App/App.csproj`
- `App.Tests/App.Tests.csproj`
- 테스트 메서드 이름이나 trait에 `[spec:F-900-C1]` 규약 반영

fake Copilot implement 단계에서 소스와 테스트를 수정하면,
runner의 `AutomatedTestService` 가 실제로 `dotnet test` 를 실행하고 TRX를 만든다.

### 방법 B. fake test result 파일 직접 생성

더 빠르게 가려면 자동 테스트 자체는 별도 계층으로 보고,
runner review loop 검증에서는 condition에 `tests/evidence` 를 직접 넣는다.

- 장점: 빠름
- 단점: `AutomatedTestService.RunAndSyncAsync` 경로는 직접 검증 못 함

실무적으로는 A와 B를 둘 다 두는 게 낫다.

- A: 소수의 느린 smoke scenario
- B: 대부분의 상태 전이 scenario

## 시나리오 매트릭스

최소한 아래 6개는 있으면 좋다.

### Scenario 1. 구현 후 review handoff 유지

초기 상태:

- spec status = `queued`
- condition status = `draft`

fake implement:

- 파일 수정만 수행
- condition/tests/evidence는 충분치 않음

기대 결과:

- cycle 1 종료 후 spec = `needs-review`
- `reviewDisposition = review-pending` 또는 `missing-evidence`
- worktree metadata 유지

### Scenario 2. 자동 테스트와 evidence로 review loop가 verified 확정

초기 상태:

- feature spec
- condition 1개 이상

fake implement:

- 테스트 통과 가능한 코드/테스트 생성

review loop 기대 결과:

- condition = `verified`
- spec = `verified`
- `verificationSource = runner-review-pass`
- worktree metadata 정리

### Scenario 3. 자동 테스트 실패로 needs-review 유지

fake implement:

- 실패하는 테스트 결과 생성

기대 결과:

- spec = `needs-review`
- `reviewDisposition = auto-test-failed`
- `lastError` 또는 test sync 정보 남음

### Scenario 4. 수동 테스트 필요로 needs-review 유지

fake implement:

- condition metadata에 `requiresManualVerification = true`
- passing test가 있어도 수동 검증 요구 유지

기대 결과:

- review loop 후에도 spec = `needs-review`
- `reviewDisposition = manual-test-required`

### Scenario 5. review에서 open question 발생

fake review:

- 질문 1개 생성

기대 결과:

- spec = `needs-review`
- `questionStatus = waiting-user-input`
- 다음 review candidate 선택에서 제외

### Scenario 6. task spec이 review loop에서 done 확정

초기 상태:

- `nodeType = task`

기대 결과:

- 구현 종료 시점은 `needs-review`
- review loop 이후 `done`

## 테스트 작성 순서

처음부터 “모든 걸 한 번에” 만들지 말고 아래 순서가 안전하다.

1. fake Copilot script만 붙인 `RunOnceAsync` smoke test
2. worktree 생성과 `queued -> working -> needs-review` 검증
3. review loop finalization 검증
4. 실제 `dotnet test` 를 태우는 느린 smoke test 1개
5. 실패/수동검증/open-question 변형 scenario 추가

## 추천 파일 구조

`tools/flow-cli.Tests/Runner/Integration/` 폴더를 새로 두는 편이 좋다.

예시:

- `RunnerEndToEndFixture.cs`
- `FakeCopilotScriptBuilder.cs`
- `FakeSolutionBuilder.cs`
- `RunnerEndToEndTests.cs`
- `RunnerReviewLoopIntegrationTests.cs`

## 구현 예시 스케치

```csharp
[Fact]
public async Task RunOnce_CompletesImplementation_AndLeavesSpecInNeedsReview()
{
    using var fixture = await RunnerEndToEndFixture.CreateAsync(new RunnerScenario
    {
        SpecId = "F-900",
        NodeType = "feature",
        InitialStatus = "queued",
        ReviewMode = FakeReviewMode.KeepNeedsReview,
        UseRealDotnetTests = false
    });

    var runner = fixture.CreateRunner();
    var results = await runner.RunOnceAsync();

    var spec = fixture.CreateStore().Get("F-900");
    spec!.Status.Should().Be("needs-review");
    spec.Metadata!["lastCompletedAt"].Should().NotBeNull();
    results.Should().ContainSingle(r => r.SpecId == "F-900" && r.Success);
}
```

review loop까지 보려면 second cycle 또는 review 전용 호출을 추가한다.

```csharp
await runner.RunOnceAsync();
await runner.RunOnceAsync();

var spec = fixture.CreateStore().Get("F-900");
spec!.Status.Should().Be("verified");
```

현재 구조에서는 첫 cycle에서 implementation, 다음 cycle에서 auto-verify/review finalization을 검증하는 방식이 가장 단순하다.

## 테스트 더블 설계 원칙

fake Copilot은 “텍스트를 잘 생성하는 AI”를 흉내 내지 말고,
“정해진 시나리오대로 파일과 spec을 바꾸는 deterministic script” 여야 한다.

좋은 더블 조건:

- 입력이 같으면 항상 같은 결과
- 랜덤 없음
- 시간 의존 최소화
- prompt 내용 일부가 바뀌어도 테스트가 쉽게 깨지지 않음

즉 prompt 전체 문자열 비교 대신,

- 실행 단계 구현 여부
- spec 파일 결과
- evidence/test metadata 결과

만 검증한다.

## 장기적으로 더 좋은 구조

지금도 가능하지만, 장기적으로는 아래 seam이 있으면 테스트 비용이 크게 줄어든다.

- `IRunnerCopilotClient`
- `IAutomatedTestExecutor`
- `IGitWorktreeManager`
- `IClock`

그러면 temp git repo 없이도 대부분의 시나리오를 in-memory 또는 fake filesystem 수준에서 검증할 수 있다.

권장 방향:

1. 기존 public API는 유지
2. `RunnerService` 에 내부용 constructor overload 추가
3. 기본 constructor는 실제 구현을 주입
4. 테스트는 fake implementation 주입

이렇게 가면 “진짜 E2E 2~3개 + 빠른 fake integration 수십 개” 조합이 가능하다.

## 현실적인 추천 조합

운영 부담까지 고려하면 다음 조합이 가장 낫다.

- 단위 테스트: 상태 규칙 대부분
- command 통합 테스트: review metadata 반영
- fake Copilot + temp git repo 기반 runner integration: 4~6개
- 실제 `dotnet test` 까지 태우는 느린 smoke test: 1~2개

이 정도면 상태 전이 회귀와 운영 경로를 둘 다 잡을 수 있다.