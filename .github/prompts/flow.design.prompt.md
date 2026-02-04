---
agent: agent
---
# Flow Design Prompt

## 🎯 목표

사용자 요구사항을 분석하여 **구현 가능한 설계 문서**와 **Backlog Queue**를 생성한다.
⚠️ **절대 코드를 수정하거나 구현하지 않는다.** 오직 설계와 계획만 수행한다.

---

## 🛠️ 단계별 핵심 액션 (State Machine)

### 1. CONTEXT_GATHERING

* **행동**: 프로젝트 구조 및 기존 공통 패턴을 탐색한다.
* **참조**: `REAMDME.md`, 관련 있는 소스코드 등.
* **제한**: 코드 수정 금지. 오직 읽기 권한만 사용.

### 2. DESIGNING

* **행동**: `docs/flow/implements/designs/{feature_name}.md` 생성.
* **필수 포함**:
* 시스템 아키텍처 및 데이터 흐름
* **인터페이스 명세** (제공/의존하는 기능 명시)
* 예외 처리 및 보안 고려사항

### 3. REVIEW_LOOP

* **행동**: 설계 완료 후 `./approve-design.ps1`을 실행하여 사용자 승인을 받는다.
* **규칙**: 승인(`Status: Approved`) 없이 다음 단계(BACKLOG_GENERATION) 진입 절대 금지.
* **전이**: 승인(`Status: Approved`) 일 경우, AI는 즉시 BACKLOG_GENERATION을 수행한다.

### 4. BACKLOG_GENERATION

* **행동**: 설계를 독립적 구현 단위로 쪼개어 `docs/flow/backlogs/{task_name}/plan.md` 생성.
* **Task Plan 필수 항목**:
* `Input/Output` 및 `Interface` (연결성 보장)
* `Done Criteria` (완료 조건) 및 `Validation` (검증 방법)
* `Tags` (Canonical Tags - 아래 규칙 준수)

### 5. QUEUE_OPTIMIZATION

* **행동**: 의존성 그래프를 분석하여 `docs/flow/backlogs/queue` 파일을 생성/업데이트한다.
* **정렬 기준**:
1. **의존성**: 타 기능의 기반이 되는 모듈 우선.
2. **리스크**: 불확실성이 큰 핵심 로직 우선.


* **보고**: `queue-rationale.md`에 정렬 근거를 표로 정리하여 보고.

---

## 🚫 금지 및 주의 사항

1. **No Implementation**: 단 한 줄의 기능 코드도 수정하지 않는다.
2. **Strict Gatekeeping**: 사용자 승인 스크립트 실행 없이 백로그로 넘어가지 않는다.
3. **Interface First**: 모든 백로그는 상호 간의 인터페이스(연결점)가 명시되어야 한다.
4. **Continuous Workflow**: 각 단계의 산출물이 완성되거나 승인 조건이 충족되면, 사용자의 추가 명령을 기다리지 않고 즉시 다음 단계를 실행한다.