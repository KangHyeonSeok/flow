---
agent: agent
---

# Flow Prompt

## User Input
```text
$ARGUMENTS
```
---

## 실행 흐름

### Step 1: 상태 파일 읽기 (필수 - 생략 불가)

```
반드시 첫 번째로 실행:
read_file(".flow/context/current-phase.json")
```

이 파일을 읽기 전에는 **어떤 판단도 하지 않는다**.

---

### Step 2: 상태 확인 및 보고

상태 파일을 읽은 후, 다음 형식으로 보고:

```
  FLOW STATUS
  {phase} : {feature_name ?? "없음"}
```

---

### Step 3: 상태별 분기

현재 상태에 따라 **정해진 행동만** 수행한다.

#### IDLE 상태

**가능한 행동**: 플랜 생성 시작만 가능

1. 사용자 요청(`$ARGUMENTS`)을 분석
2. 요청이 비어있거나 현재 문서를 따르라는 명령이면::
   a. 백로그 큐 확인:
      ```powershell
      cd .flow/scripts; ./pop-backlog.ps1 -Preview
      ```
   b. 큐에 작업이 있으면 (질문 없이 바로 진행):
      ```powershell
      cd .flow/scripts; ./pop-backlog.ps1
      ```
      - `plan_type`이 "reviewed"면: 플랜 읽고 컨텍스트 수집 후 EXECUTING으로 진행
      - `plan_type`이 "need-review"면: 플랜 읽고 사용자에게 리뷰 요청 후 PLANNING
   c. 큐가 비어있으면: "무엇을 하시겠습니까?" 질문
3. 요청이 있으면:
   ```powershell
   cd .flow/scripts; ./start-plan.ps1 -Title "요청 제목"
   ```
4. 플랜 파일 생성 후 PLANNING으로 전이됨
5. **코드 수정 절대 불가** - 플랜 작성으로만 진행

#### PLANNING 상태

**가능한 행동**: 플랜 작성만 가능

1. 활성 플랜 파일 읽기: `docs/implements/{feature_name}/plan.md`
2. 4개 필수 섹션 작성:
   - 입력 (Inputs)
   - 출력 (Outputs)
   - 검증 방법 (Validation)
   - 완료 조건 (Done Criteria)
3. 플랜 작성 완료 후:
   ```powershell
   cd .flow/scripts; ./approve-plan.ps1
   ```
4. REVIEWING 상태로 전이

##### 추가 정보가 필요한 경우

플랜 작성 중 사용자로부터 추가 정보가 필요한 경우 `human-input.ps1` 스크립트를 사용하여 입력을 받는다:

**사용 가능한 입력 타입:**

| 타입 | 용도 | 예시 |
|------|------|------|
| `Confirm` | Yes/No 확인 | 특정 기능 포함 여부 확인 |
| `Select` | 선택지 중 선택 | 여러 구현 방식 중 선택 |
| `Text` | 자유 텍스트 입력 | 상세 요구사항 입력 |
| `Review` | 검토 후 확인 | 파일 검토 완료 확인 |

**사용 예시:**

```powershell
# Yes/No 확인
cd .flow/scripts; ./human-input.ps1 -Type Confirm -Prompt "이 기능에 로깅을 포함시킬까요?"

# 선택지 중 선택
cd .flow/scripts; ./human-input.ps1 -Type Select -Prompt "어떤 방식으로 구현할까요?" -Options @("방식A: 기존 컴포넌트 수정", "방식B: 새 컴포넌트 생성")

# 자유 텍스트 입력
cd .flow/scripts; ./human-input.ps1 -Type Text -Prompt "추가로 고려해야 할 사항이 있으면 입력해주세요"
```

**응답 활용**: 스크립트는 JSON 형식으로 결과를 반환하며, `response` 필드에 사용자 입력이 포함됨.

#### REVIEWING 상태

**가능한 행동**: 승인 요청만 가능

1. 플랜 내용을 사용자에게 보여주기
2. **반드시 질문**: "이 플랜을 승인하시겠습니까? (Y/N)"
3. 사용자 응답 대기:
   - Y: `./approve-plan.ps1` 실행 → EXECUTING 전이 (즉시 실행 시작)
   - N: "수정할 내용을 알려주세요" → PLANNING 복귀
4. **사용자 응답 없이 진행 금지**
5. 승인 후 실행 시작 메시지 출력: "플랜 대로 구현을 시작합니다..."

#### EXECUTING 상태

**가능한 행동**: 플랜에 정의된 단계 실행

1. 플랜의 "실행 단계" 섹션을 순차적으로 수행
2. 각 단계 완료 후 보고
3. 에러 발생 시:
   ```powershell
   cd .flow/scripts; ./abort-to-idle.ps1 -Reason "에러 내용"
   ```
   → BLOCKED 상태로 전이
4. 모든 단계 완료 시:
   ```powershell
   cd .flow/scripts; ./complete-execution.ps1 -Summary "실행 요약"
   ```
   → VALIDATING 전이

#### VALIDATING 상태

**가능한 행동**: 검증 실행

1. 검증 프로파일 참조: `.flow/validation-profiles.json`
   - `nextjs`: npm run build, npm run lint
   - `typescript`: npx tsc --noEmit
   - `powershell`: 스크립트 문법 검사
2. 플랜의 "검증 방법" 섹션 실행:
   ```powershell
   cd .flow/scripts; ./validation-runner.ps1 -Command "검증명령"
   ```
   - 스크립트는 **1회만 실행**하고 결과 반환
   - 재시도 횟수는 `current-phase.json`의 `retry_count`/`max_retries`로 관리
3. 검증 성공 시:
   - **확장 상태 체크** (아래 "확장 상태 시스템" 참조)
   - 활성화된 확장이 있으면 해당 확장 실행
   - 확장 없거나 확장 완료 시 COMPLETED 전이
4. 검증 실패 시:
   - 5회 미만: RETRYING 전이 → **AI가 오류 분석 및 수정 후 재검증**
   - 5회 초과: BLOCKED 전이

#### RETRYING 상태

**가능한 행동**: 오류 분석 후 수정, 재검증

1. 에러 분석 및 수정 시도
2. 재검증 실행
3. 5회 초과 시 BLOCKED 전이

#### BLOCKED 상태

**가능한 행동**: 사람 개입 요청

1. 문제 상황 설명
2. 선택지 제시:
   - 중단: `./abort-to-idle.ps1`
   - 재시도: 문제 해결 후 다시 시도
3. 사용자 지시 대기

#### COMPLETED 상태

**가능한 행동**: 완료 보고 및 result.md 작성

1. `docs/implements/{feature_name}/result.md` 파일 생성:
   - 템플릿: `.flow/templates/result-template.md` 참조
   - 필수 섹션: 요약, 변경 사항, 수정된 파일, 테스트 결과
   - PR 보고에 사용할 수 있도록 작성
2. 완료된 작업 요약
3. 상태를 IDLE로 복귀:
   ```powershell
   cd .flow/scripts; ./abort-to-idle.ps1 -Reason "작업 완료"
   ```
4. **백로그 기반 작업이었다면 중단하지 말고 즉시 다음 백로그를 진행**:
   - IDLE 복귀 직후 백로그 큐를 확인하고, 작업이 있으면 바로 이어서 처리한다.
   ```powershell
   cd .flow/scripts; ./pop-backlog.ps1 -Preview
   cd .flow/scripts; ./pop-backlog.ps1
   ```
   - `plan_type`이 "reviewed"면: 플랜 읽고 컨텍스트 수집 후 EXECUTING으로 진행
   - `plan_type`이 "need-review"면: 플랜 읽고 사용자에게 리뷰 요청 후 PLANNING

---

## 금지 사항 (위반 시 즉시 중단)

1. ❌ 상태 파일 읽기 전 행동
2. ❌ 플랜 없이 코드 수정
3. ❌ "간단한 수정"이라며 플랜 건너뛰기
4. ❌ 코드 수정 도구 직접 사용
5. ❌ 5회 재시도 초과 후 계속 시도

---

## 위반 감지 시

만약 위 규칙을 위반하려 했다면:

1. **즉시 중단**
2. 다음 메시지 출력:
   ```
   ⚠️ [ORCH 위반 감지]
   시도한 행동: {행동 설명}
   위반 규칙: {규칙 번호}
   
   올바른 절차로 다시 시작합니다.
   ```
3. Step 1부터 다시 시작

---

## 스크립트 경로

모든 스크립트는 `.flow/scripts/` 에 위치:

| 스크립트 | 용도 |
|----------|------|
| `get-status.ps1` | 상태 확인 |
| `start-plan.ps1 -Title "제목"` | 플랜 시작 |
| `approve-plan.ps1` | 플랜 승인 |
| `complete-execution.ps1 -Summary "요약"` | 실행 완료 → VALIDATING |
| `abort-to-idle.ps1 -Reason "사유"` | 중단 |
| `validation-runner.ps1 -Command "cmd"` | 검증 |
| `pop-backlog.ps1 [-Preview]` | 백로그 큐에서 다음 작업 가져오기 |

---

## 참조 문서

- 원칙: `.flow/memory/principles.md`
- 템플릿: `.flow/templates/plan-template.md`
- 검증 프로파일: `.flow/validation-profiles.json`
- 확장 정의: `.flow/extensions.json`

---

## 확장 상태 시스템

Flow는 확장 상태를 통해 추가 검증/리뷰 단계를 동적으로 추가할 수 있습니다.

### 확장 파일

확장 정의: `.flow/extensions.json`

### 확장 체크 시점

VALIDATING 성공 후, COMPLETED 전이 전에 확장 체크:

1. `.flow/extensions.json` 읽기
2. `enabled: true`인 확장 중 `trigger.after === "VALIDATING"` 찾기
3. 해당 확장의 `actions` 실행
4. **제안이 있으면**: 사용자에게 결과 보고 및 선택 요청
5. **제안이 없으면**: 사용자 확인 없이 바로 COMPLETED 전이
6. `transitions`에 따라 다음 상태 결정

### 확장 실행 예시 (STRUCTURE_REVIEW)

#### 제안이 있는 경우

```
📋 확장 실행: STRUCTURE_REVIEW (구조 리뷰)

변경된 파일을 분석하여 리팩토링 제안을 생성합니다...

🔍 분석 결과:
1. [components/SearchForm.tsx] 함수 길이 초과 (120줄 → 분리 권장)
2. [components/SearchForm.tsx] 중복 코드 감지 (드롭다운 로직)

리팩토링을 적용하시겠습니까?
- Y: 리팩토링 적용 후 재검증 (→ EXECUTING)
- N: 현재 상태로 완료 (→ COMPLETED)
- Skip: 이 확장 건너뛰기 (→ COMPLETED)
```

#### 제안이 없는 경우 (사용자 확인 스킵)

```
📋 확장 실행: STRUCTURE_REVIEW (구조 리뷰)

변경된 파일을 분석하여 리팩토링 제안을 생성합니다...

✅ 리팩토링 제안 없음 - 구조가 적절합니다.
→ COMPLETED 상태로 자동 전이
```

**중요**: 제안이 없으면 `human-input.ps1` 호출 없이 바로 COMPLETED로 진행한다.

### 확장 관리

확장 추가/수정/활성화는 `/flowext` 프롬프트 사용:

```
/flowext add STRUCTURE_REVIEW VALIDATING에서 구조 리뷰 실행
/flowext enable STRUCTURE_REVIEW
/flowext disable STRUCTURE_REVIEW
/flowext list
```

### 기본 제공 확장

| 확장 ID | 설명 | 기본 상태 |
|---------|------|----------|
| STRUCTURE_REVIEW | 구조/리팩토링 리뷰 | ✅ 활성화 |
| DESIGN_REVIEW | 설계/아키텍처 리뷰 | ❌ 비활성화 |
| TEST_SUGGESTION | 테스트 케이스 제안 | ❌ 비활성화 |
| SECURITY_REVIEW | 보안 취약점 검토 | ❌ 비활성화 |
