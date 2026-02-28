---
name: flow-rag
description: RAG(Retrieval-Augmented Generation) 데이터베이스 관리. flow db-add, db-query 커맨드로 문서를 저장하고 벡터 검색한다. 과거 작업 이력 조회, 문서 저장, 임베딩 기반 유사도 검색이 필요할 때 사용.
---

# Flow RAG

SQLite + sqlite-vec 기반 RAG 데이터베이스. 문서 저장 및 벡터 유사도 검색.

## 문서 추가 (db-add)

```bash
# 기본 사용
./flow.ps1 db-add --content "구현 내용 설명" --tags "cli,refactor"

# commit 정보 포함  
./flow.ps1 db-add --content "빌드 시스템 구현" --tags "build" --commit-id "abc123"

# 기능 이름 명시
./flow.ps1 db-add --content "스펙 그래프 구현" --feature "spec-graph" --tags "spec"

# plan/result 파일 포함
./flow.ps1 db-add --content "작업 완료" --plan ./docs/plan.md --result ./docs/result.md
```

## 문서 검색 (db-query)

```bash
# 텍스트 검색 (벡터 검색이 가능하면 자동으로 하이브리드 검색)
./flow.ps1 db-query --query "빌드 시스템"

# 태그 필터
./flow.ps1 db-query --query "빌드" --tags "cli,build"

# plan/result 포함
./flow.ps1 db-query --query "빌드" --plan --result

# 결과 수 제한
./flow.ps1 db-query --query "테스트" --top 10
```

## 검색 방식

1. **벡터 검색** (embed.exe + sqlite-vec 사용 가능 시): 코사인 유사도 기반
2. **텍스트 검색** (폴백): LIKE 기반 매칭
3. **하이브리드 스코어링**: semantic(0.5) + tag(0.1) 가중치

## DB 위치

- DB 파일: `.flow/rag/db/local.db`
- 임베딩 바이너리: `.flow/rag/bin/embed.exe`

## 활용 패턴

- 작업 시작 전 `db-query`로 과거 유사 작업 참조
- 작업 완료 후 `db-add`로 결과 기록
- 태그 체계를 일관되게 유지하여 검색 품질 향상
