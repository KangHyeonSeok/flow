---
agent: agent
---
# Flow Extension Manager (flowext)

Flow 확장 상태를 추가, 수정, 활성화/비활성화하는 프롬프트입니다.

## User Input
```text
$ARGUMENTS
```

---

## 사용법

### 확장 추가

```
/flowext add <확장ID> <트리거상태>에서 <행동설명>
```

**예시:**
```
/flowext add STRUCTURE_REVIEW VALIDATING에서 구조 리뷰 실행
/flowext add TEST_SUGGESTION STRUCTURE_REVIEW 후에 테스트 케이스 제안
/flowext add SECURITY_CHECK EXECUTING 전에 보안 검토
```

### 확장 활성화/비활성화

```
/flowext enable <확장ID>
/flowext disable <확장ID>
```

**예시:**
```
/flowext enable STRUCTURE_REVIEW
/flowext disable TEST_SUGGESTION
```

### 확장 목록 조회

```
/flowext list
/flowext list enabled
/flowext list disabled
```

### 확장 상세 조회

```
/flowext show <확장ID>
```

### 확장 제거

```
/flowext remove <확장ID>
```

---

## 실행 흐름

### Step 1: 명령어 파싱

사용자 입력(`$ARGUMENTS`)을 파싱하여 명령 유형 식별:
- `add`: 새 확장 추가
- `enable`: 확장 활성화
- `disable`: 확장 비활성화
- `list`: 확장 목록 조회
- `show`: 확장 상세 조회
- `remove`: 확장 제거

### Step 2: extensions.json 읽기

```
read_file(".flow/extensions.json")
```

### Step 3: 명령별 처리

#### add 명령

1. 확장 ID 생성 (대문자_언더스코어)
2. 트리거 상태 파싱 (VALIDATING, EXECUTING, PLANNING 등)
3. 행동 설명에서 actions 생성
4. 기본 transitions 설정
5. extensions.json에 추가 (enabled: false로 시작)
6. 사용자에게 확인: "확장을 활성화하시겠습니까?"

#### enable/disable 명령

1. 확장 ID로 해당 확장 찾기
2. enabled 필드 변경
3. extensions.json 저장
4. 변경 결과 보고

#### list 명령

1. extensions 객체 순회
2. 필터 적용 (enabled/disabled)
3. 목록 형식으로 출력:
   ```
   ✅ STRUCTURE_REVIEW: 구조 리뷰 (after VALIDATING)
   ❌ DESIGN_REVIEW: 설계 리뷰 (after PLANNING)
   ```

#### show 명령

1. 확장 ID로 찾기
2. 상세 정보 출력:
   - 기본 정보
   - 트리거 조건
   - 행동 목록
   - 상태 전이
   - 설정

#### remove 명령

1. 확장 ID로 찾기
2. 사용자 확인: "정말 제거하시겠습니까?"
3. extensions 객체에서 삭제
4. extensions.json 저장

---

## 확장 스키마 참조

새 확장 추가 시 `.flow/templates/extension-template.md` 참조

### 필수 필드

| 필드 | 설명 |
|------|------|
| id | 고유 식별자 |
| name | 표시 이름 |
| description | 설명 |
| enabled | 활성화 여부 |
| trigger | 트리거 조건 |
| actions | 수행할 행동 |
| transitions | 상태 전이 |

### 트리거 상태

| 상태 | 설명 |
|------|------|
| PLANNING | 플랜 작성 중 |
| REVIEWING | 플랜 검토 중 |
| EXECUTING | 실행 중 |
| VALIDATING | 검증 중 |
| COMPLETED | 완료 |

### 트리거 타이밍

| 키 | 설명 |
|-----|------|
| after | 상태 완료 후 |
| before | 상태 진입 전 |

---

## 예시 세션

### 구조 리뷰 확장 추가

**입력:**
```
/flowext add STRUCTURE_REVIEW VALIDATING에서 구조 리뷰 실행
```

**출력:**
```
✅ 확장 추가됨

ID: STRUCTURE_REVIEW
이름: 구조 리뷰
트리거: VALIDATING 완료 후
행동: 구조 리뷰 실행

확장을 활성화하시겠습니까? (Y/N)
```

### 확장 목록 조회

**입력:**
```
/flowext list
```

**출력:**
```
Flow 확장 목록

✅ STRUCTURE_REVIEW: 구조 리뷰 (after VALIDATING)
❌ DESIGN_REVIEW: 설계 리뷰 (after PLANNING)
❌ TEST_SUGGESTION: 테스트 제안 (after STRUCTURE_REVIEW)
❌ SECURITY_REVIEW: 보안 검토 (after VALIDATING)

활성화: 1개 / 비활성화: 3개
```

---

## 참조 문서

- 확장 정의 파일: `.flow/extensions.json`
- 확장 템플릿: `.flow/templates/extension-template.md`
- Flow 원칙: `.flow/memory/principles.md`