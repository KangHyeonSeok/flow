# Flow 변경 입력과 스펙 그래프 운영

이 문서는 Flow에서 `프로젝트 -> 에픽 -> 스펙` 구조를 유지하면서도, 장기간 개발과 운영 중 발생하는 요구사항 변경, 하드웨어 변경, 운영 주체 변경, 유지 보수 이슈 같은 현실의 변화를 어떻게 누적하고 코드에 반영할지 정리한다.

핵심은 `모든 것이 스펙이다`가 아니라 `모든 중요한 변화는 스펙 그래프에 들어오는 입력이다`라는 관점이다.

## 1. 왜 이 문서가 필요한가

- 프로젝트가 길어질수록 변경의 원인과 구현 결과가 분리되기 쉽다.
- 스펙만 쌓으면 무엇이 왜 바뀌었는지 사라질 수 있다.
- 반대로 모든 운영 사실을 스펙으로 만들면 실행 단위와 맥락 단위가 섞인다.
- Flow는 변경의 원인, 영향 분석, 실행 단위, 검증 근거를 분리하되 서로 강하게 연결해야 한다.

## 2. 기본 구조

Flow의 권장 계층은 아래와 같다.

```text
Project
  Epic
    Spec
      Acceptance Criteria
      Tests
      Evidence
```

여기에 장기 운영을 위해 아래 입력 계층을 추가한다.

```text
Change Record
  -> impacts Project
  -> impacts Epic
  -> impacts Spec
  -> impacts Code / Tests / Evidence
```

정리하면 각 계층의 책임은 아래와 같다.

- Project: 왜 존재하는가, 어떤 방향과 제약을 갖는가
- Epic: 어떤 가치 묶음을 어떤 milestone으로 달성하는가
- Spec: 실제 구현과 검증의 실행 단위
- Change Record: 왜 바뀌었는가, 무엇에 영향을 주는가

## 3. 핵심 구분: 변화 자체와 영향받은 스펙은 다르다

질문은 자연스럽다. `스펙에 영향을 주면 결국 스펙 그래프에도 영향을 주는 것 아닌가?`

맞다. 결과적으로는 그렇다. 하지만 아래 두 문장은 같은 말이 아니다.

1. 변경이 스펙에 영향을 준다.
2. 변경이 스펙 그래프에 들어오는 입력이다.

차이는 `역할`과 `시점`에 있다.

### 3.1 `스펙에 영향을 준다`는 결과 관점이다

이 말은 이미 어떤 변화가 발생했고, 그 변화 때문에 아래 중 하나가 일어났다는 뜻이다.

- 기존 spec 본문이 수정된다.
- 새 spec이 추가된다.
- epic 범위가 바뀐다.
- acceptance criteria, tests, evidence가 다시 검토된다.

즉 변화가 반영된 뒤의 상태를 보는 말이다.

### 3.2 `스펙 그래프에 들어오는 입력이다`는 운영 관점이다

이 말은 변화가 아직 스펙 수정으로 완전히 환원되기 전에도, 시스템이 먼저 그 변화를 `독립된 원인`으로 기록하고 추적해야 한다는 뜻이다.

즉 Flow는 아래 순서를 가져야 한다.

1. 현실의 변화가 발생한다.
2. 그 변화를 Change Record로 기록한다.
3. 영향 분석을 수행한다.
4. 영향받은 project/epic/spec/code/test를 연결한다.
5. 필요한 spec 수정 또는 새 spec 생성을 수행한다.
6. 코드, 테스트, evidence까지 반영되었는지 검증한다.

여기서 Change Record는 `원인`, spec graph 변경은 `결과`다.

### 3.3 왜 이 구분이 중요한가

이 구분이 없으면 아래 문제가 생긴다.

- 나중에 왜 그 spec이 생겼는지 모른다.
- 하드웨어나 운영 주체 변경처럼 코드 밖에서 시작된 변화가 누락된다.
- 스펙 본문은 바뀌었는데 영향 분석이 끝났는지 알 수 없다.
- 코드와 테스트가 실제로 따라왔는지 닫힘 조건을 강제하기 어렵다.

즉 `영향을 받았다`는 사실만으로는 부족하고, `무엇이 원인이었는가`를 독립적으로 남겨야 한다.

## 4. 예시로 보면 더 명확하다

### 4.1 요구사항 변경

상황:

- 사용자가 승인 기준을 바꿨다.

잘못된 모델:

- 기존 spec 텍스트만 바로 수정한다.

권장 모델:

1. requirement change record를 만든다.
2. 어떤 epic과 spec이 영향받는지 연결한다.
3. 기존 spec의 acceptance criteria와 tests를 갱신한다.
4. 관련 코드와 evidence가 새 기준을 만족하는지 다시 검증한다.

### 4.2 하드웨어 변경

상황:

- 배포 서버가 CPU only 환경으로 바뀌었다.

잘못된 모델:

- 관련 spec 몇 개만 감으로 수정한다.

권장 모델:

1. hardware change record를 만든다.
2. 성능, 배포, 런타임 의존성이 있는 epic/spec를 영향 분석으로 찾는다.
3. 필요한 경우 신규 migration spec 또는 validation task spec를 만든다.
4. benchmark, smoke test, 운영 evidence를 붙인 뒤 change를 닫는다.

### 4.3 운영 주체 변경

상황:

- 운영 팀이 바뀌어 review SLA와 escalation 경로가 달라졌다.

잘못된 모델:

- 운영 문서만 수정하고 끝낸다.

권장 모델:

1. ownership change record를 만든다.
2. review request, assignment, 알림, 권한과 연결된 spec를 찾는다.
3. 필요한 spec과 project 문서를 함께 수정한다.
4. 실제 운영 경로가 바뀌었는지 evidence를 남긴다.

## 5. Change Record를 별도 계층으로 두는 이유

모든 변화를 곧바로 spec으로 바꾸면 아래 문제가 생긴다.

- spec이 실행 단위가 아니라 잡다한 사건 로그가 된다.
- 아직 어떤 spec이 영향받는지 모르는 초기에 기록할 자리가 없다.
- 하나의 change가 여러 spec에 퍼질 때 상위 원인을 잃는다.
- 완료되지 않은 영향 분석과 완료된 구현을 구분하기 어렵다.

반대로 Change Record를 별도 계층으로 두면 아래가 가능해진다.

- 원인과 결과를 분리해 저장한다.
- 한 change가 여러 epic/spec로 퍼지는 구조를 다룬다.
- spec 수정 전 단계에서도 change를 열어둘 수 있다.
- 닫힘 기준을 `문서 수정 완료`가 아니라 `영향 반영 완료`로 정의할 수 있다.

## 6. 권장 운영 모델

### 6.1 저장 모델

최소한 아래 네 가지를 구분한다.

- Project Document
- Epic Document
- Spec
- Change Record

Spec은 계속 authoritative execution contract다.

Change Record는 authoritative change input이다.

둘은 경쟁 관계가 아니라 역할이 다르다.

### 6.2 최소 Change Record 필드

```json
{
  "changeId": "CH-001",
  "projectId": "flow",
  "type": "requirement",
  "summary": "사용자 검토 응답의 최소 필드 강화",
  "reason": "부정확한 응답이 반복되어 review loop 품질이 낮아짐",
  "source": "ops-review",
  "status": "analyzed",
  "affectedEpicIds": ["EPIC-B", "EPIC-C"],
  "affectedSpecIds": ["F-001", "F-018"],
  "affectedCodeRefs": [
    "tools/flow-core/Runner/FlowRunner.cs"
  ],
  "requiredActions": [
    "review request validation 강화",
    "관련 spec acceptance criteria 갱신"
  ],
  "evidence": [],
  "createdAt": "2026-03-22T08:00:00Z"
}
```

최소 필드 의미:

- `type`: requirement, hardware, maintenance, ownership, policy, incident, dependency
- `status`: open, analyzed, planned, in-progress, verified, closed
- `affectedEpicIds`: 상위 계획 영향
- `affectedSpecIds`: 실행 단위 영향
- `affectedCodeRefs`: 코드 반영 추적
- `requiredActions`: 아직 해야 할 일
- `evidence`: 실제 반영 근거

### 6.3 상태 규칙

아래 규칙을 권장한다.

- change는 영향 분석 없이 닫을 수 없다.
- change가 열린 상태인데 affected spec이 비어 있으면 경고 또는 실패다.
- affected spec이 수정되면 관련 tests와 evidence도 다시 검토 대상이 된다.
- change가 닫히려면 spec, code, tests, evidence가 함께 수렴해야 한다.
- UI와 runner는 change 자체로 spec state를 직접 수정하지 않고, core 규칙을 통해 반영한다.

## 7. Flow에서 실제로 돌리는 순서

권장 운영 순서는 아래와 같다.

1. Change Record 생성
2. 영향 분석 수행
3. impacted project/epic/spec 연결
4. 필요한 spec 수정 또는 신규 spec 생성
5. state propagation 또는 review 재진입 수행
6. 코드, 테스트, evidence 반영
7. change verification 후 close

이를 한 줄로 줄이면 아래와 같다.

`현실 변화 -> change record -> impact analysis -> spec graph update -> code/test/evidence update -> verification`

## 8. 스펙 그래프 관점에서의 해석

스펙 그래프는 단순 트리만 의미하지 않는다. 최소한 아래 관계를 함께 다뤄야 한다.

- project -> epic -> spec 계층 관계
- spec -> spec dependency 관계
- change -> project/epic/spec impact 관계
- spec -> codeRefs/test/evidence 연결 관계

즉 장기적으로 Flow의 그래프는 아래처럼 읽는 편이 맞다.

```text
Change -> Project/Epic/Spec -> Code/Test/Evidence
```

이때 change edge는 `왜 바뀌는가`, spec edge는 `무엇이 연결되는가`, evidence edge는 `무엇으로 검증되었는가`를 설명한다.

## 9. 구현 우선순위

### 9.1 먼저 할 일

- Change Record 모델을 별도 저장 단위로 추가한다.
- spec에 `epicId`, `relatedChangeIds` 같은 연결 필드를 추가한다.
- 영향 분석 명령 또는 서비스(`change-impact`)를 추가한다.
- change close 시 검증 규칙을 추가한다.

### 9.2 나중에 할 일

- project/epic view에서 change hotspot을 보여준다.
- change type별 템플릿을 제공한다.
- 장기적으로 change와 review/activity를 함께 집계한 운영 dashboard를 만든다.

## 10. 이번 반복 할 일

### Now

- [x] `모든 것이 스펙이 아니라 모든 변화가 스펙 그래프의 입력`이라는 관점을 문서로 정리한다.
  왜 필요한가: 장기 운영에서 원인과 실행 단위를 섞지 않기 위해 필요하다.
  변경 대상: `docs/flow-change-driven-spec-graph.md`
  완료 기준: change record와 spec graph의 관계, 예시, 운영 규칙이 문서에 설명되어 있다.

- [x] 상위 문서에서 이 개념 문서를 찾을 수 있게 연결한다.
  왜 필요한가: 프로젝트 문맥에서 change-driven 운영 문서를 바로 발견할 수 있어야 한다.
  변경 대상: `README.md`, `docs/flow-project-document.md`
  완료 기준: 주요 문서 목록과 프로젝트 문서의 하위 문서 목록에 새 문서가 반영되어 있다.

### Next

- [ ] Change Record JSON 스키마와 저장 위치를 `flow-core` 관점에서 구체화한다.
  왜 필요한가: 문서 개념을 실제 저장 계약으로 내리기 위해 필요하다.
  변경 대상: `docs/flow-schema.md` 또는 별도 스키마 문서
  완료 기준: 최소 필드, 상태, 닫힘 규칙이 합의된다.

- [ ] `change-impact`, `change-close` 같은 CLI/API 계약을 설계한다.
  왜 필요한가: change를 실제 운영 입력으로 쓰려면 명령과 검증 경로가 필요하다.
  변경 대상: CLI/API 설계 문서
  완료 기준: 입력과 출력, 검증 규칙이 문서화된다.

### Later

- [ ] flow-web의 project/epic/spec 화면에 change hotspot과 related changes를 노출한다.
  왜 필요한가: 운영 화면에서 원인과 결과를 함께 봐야 장기 누적이 가능하다.
  변경 대상: web read model과 UI 문서
  완료 기준: project, epic, spec view 각각에서 열린 change를 탐색할 수 있다.

## 11. 결론

`변화가 스펙에 영향을 준다`는 말은 맞다. 다만 그것만으로는 충분하지 않다.

Flow는 아래 두 사실을 동시에 관리해야 한다.

- 무엇이 구현과 검증의 단위인가: spec
- 무엇이 그 변경의 원인이었는가: change record

그래서 Flow는 `모든 것을 spec으로 평탄화하는 시스템`이 아니라 `모든 중요한 변화를 change input으로 받아 spec graph와 code/test/evidence에 전파하는 시스템`이 되는 편이 맞다.