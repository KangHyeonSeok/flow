# 확장 상태 템플릿

새 확장 상태를 추가할 때 이 템플릿을 참고하세요.

---

## 확장 정의 스키마

```json
{
  "EXTENSION_ID": {
    "id": "EXTENSION_ID",
    "name": "확장 이름 (한글)",
    "description": "이 확장이 하는 일",
    "enabled": true,
    "priority": 10,
    
    "trigger": {
      "after": "VALIDATING",
      "condition": "validation_passed",
      "description": "트리거 조건 설명"
    },
    
    "actions": [
      {
        "type": "analyze|suggest|generate|prompt_user",
        "target": "changed_files|plan_file|all_files",
        "checks": ["check1", "check2"]
      }
    ],
    
    "transitions": {
      "success_case": "NEXT_STATE",
      "failure_case": "FALLBACK_STATE",
      "error": "BLOCKED"
    },
    
    "config": {
      "custom_option": "value"
    }
  }
}
```

---

## 필드 설명

### 기본 정보

| 필드 | 타입 | 필수 | 설명 |
|------|------|------|------|
| `id` | string | ✅ | 고유 식별자 (대문자_언더스코어) |
| `name` | string | ✅ | 표시 이름 |
| `description` | string | ✅ | 확장 설명 |
| `enabled` | boolean | ✅ | 활성화 여부 |
| `priority` | number | ❌ | 실행 우선순위 (낮을수록 먼저) |

### trigger (트리거)

| 필드 | 값 | 설명 |
|------|-----|------|
| `after` | 상태명 | 이 상태 완료 후 실행 |
| `before` | 상태명 | 이 상태 진입 전 실행 |
| `condition` | 조건명 | 추가 조건 |

**사용 가능한 조건:**

- `always`: 항상 실행
- `validation_passed`: 검증 통과 시
- `validation_failed`: 검증 실패 시
- `new_feature`: 새 기능 추가 시
- `refactoring`: 리팩토링 작업 시
- `security_related_files`: 보안 관련 파일 변경 시
- `has_tests`: 테스트 파일 포함 시

### actions (행동)

| type | 설명 |
|------|------|
| `analyze` | 코드/구조 분석 |
| `suggest` | 개선 사항 제안 |
| `generate` | 코드/문서 생성 |
| `prompt_user` | 사용자에게 질문 |
| `execute` | 명령 실행 |

### transitions (상태 전이)

확장 실행 결과에 따른 다음 상태 정의:

```json
"transitions": {
  "성공_조건": "다음_상태",
  "실패_조건": "폴백_상태",
  "error": "BLOCKED"
}
```

---

## 예시: 코드 커버리지 확장

```json
{
  "CODE_COVERAGE": {
    "id": "CODE_COVERAGE",
    "name": "코드 커버리지",
    "description": "테스트 커버리지를 측정하고 보고합니다.",
    "enabled": false,
    "priority": 25,
    
    "trigger": {
      "after": "VALIDATING",
      "condition": "has_tests",
      "description": "테스트 파일이 있을 때 커버리지 측정"
    },
    
    "actions": [
      {
        "type": "execute",
        "command": "npm run test:coverage",
        "output": "coverage_report"
      },
      {
        "type": "analyze",
        "target": "coverage_report",
        "checks": ["minimum_coverage"]
      }
    ],
    
    "transitions": {
      "coverage_met": "COMPLETED",
      "coverage_low": "BLOCKED",
      "no_tests": "COMPLETED"
    },
    
    "config": {
      "minimum_coverage": 80,
      "report_format": "lcov"
    }
  }
}
```

---

## 확장 추가 방법

1. `extensions.json`의 `extensions` 객체에 새 확장 추가
2. `enabled: false`로 시작하여 테스트
3. 테스트 완료 후 `enabled: true`로 변경

## 확장 비활성화 방법

```json
"EXTENSION_ID": {
  "enabled": false,
  ...
}
```

## 확장 제거 방법

`extensions.json`에서 해당 확장 객체 삭제

---

## 주의사항

1. **무한 루프 방지**: 전이 상태가 자기 자신을 트리거하지 않도록 주의
2. **우선순위 충돌**: 같은 트리거에 여러 확장이 있으면 priority 순서로 실행
3. **타임아웃**: `settings.extension_timeout_seconds` 초과 시 BLOCKED
4. **최대 실행 수**: `settings.max_extensions_per_run` 초과 시 나머지 스킵
