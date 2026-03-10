# RAG Freshness Policy

Flow의 RAG는 SoT가 아니다. RAG의 역할은 긴 문서군과 작업 이력을 빠르게 회수하는 보조 기억장치다. 따라서 RAG의 품질 기준은 "항상 최신 사실을 직접 보장하는가"가 아니라, "현재 기준 정보와 과거 이력을 섞지 않고 LLM이 빠르게 판단할 수 있게 돕는가"에 있다.

## 목적

- 현재 기준 정보와 과거 의사결정 이력을 분리한다.
- append-only 로그가 시간이 지나며 현재 truth처럼 오인되는 일을 줄인다.
- 스펙과 운영 문서가 바뀌었을 때 canonical summary가 갱신되도록 한다.
- 검색 결과에 freshness 신호를 노출해 LLM과 사람이 stale 가능성을 판단할 수 있게 한다.

## 기본 원칙

1. 현재 truth는 spec graph와 code에 있다.
2. RAG는 retrieval cache이자 decision log다.
3. 현재 기준 정보는 upsert되어야 하고, 과거 로그는 append-only로 남겨도 된다.
4. 같은 질의에 대해 canonical summary가 있으면 historical note보다 우선되어야 한다.
5. 검색 결과에는 최소한 생성 시각, 마지막 갱신 시각, 문서 유형이 드러나야 한다.

## 문서 유형 구분

### 1. Canonical summary

현재 기준을 대표하는 요약 문서다.

- 스펙 본문 인덱스
- feature별 최신 planning summary
- 운영 규칙의 최신 요약본

이 유형은 append-only가 아니라 stable key 기준 upsert를 원칙으로 한다.

### 2. Historical log

과거 판단의 이유와 당시 상태를 기록하는 로그다.

- 작업 완료 기록
- 회고 메모
- 구현 당시의 판단 근거

이 유형은 append-only를 허용한다. 다만 현재 기준처럼 해석되면 안 된다.

### 3. Ephemeral note

짧은 실험 메모, 임시 TODO, 로컬 판단 메모다.

가능하면 RAG에 넣지 않는다. 넣더라도 짧은 수명 또는 낮은 우선순위를 가져야 한다.

## 쓰기 정책

### 스펙

- 스펙 JSON이 바뀌면 `spec-index`로 canonical summary를 갱신한다.
- 같은 스펙 ID는 동일한 stable key로 upsert한다.

### Planning / governance summary

- feature별 최신 요약은 별도 stable key로 upsert한다.
- 세부 작업 로그와 결정 이력은 별도 append-only로 남긴다.

### 작업 로그

- `db-add`는 historical log 용도로 주로 사용한다.
- 현재 기준을 대표하는 문서는 `db-add`만으로 관리하지 않는다.

## 읽기 정책

질문이 현재 상태를 묻는 경우:

1. spec graph와 code를 먼저 본다.
2. RAG에서는 canonical summary를 우선 조회한다.
3. historical log는 판단 이유 보강용으로만 사용한다.

질문이 과거 결정 이유를 묻는 경우:

1. historical log를 조회한다.
2. 현재 canonical summary와 충돌하는지 확인한다.
3. 충돌하면 현재 기준이 우선임을 명시한다.

## freshness 신호

검색 결과에는 가능하면 아래 정보가 포함되어야 한다.

- sourceType 또는 동등한 문서 유형
- stable key 또는 feature key
- created_at
- updated_at
- superseded 또는 stale 여부
- canonical 여부

## stale 방지 규칙

다음 조건이면 stale 후보로 간주한다.

- 동일 feature에 더 최근 canonical summary가 존재함
- 참조하는 spec이 deprecated 또는 superseded 상태임
- 마지막 갱신 시각이 오래되었고 이후 관련 스펙 변경이 있었음
- 임시 작업 메모인데 후속 확정 기록이 존재함

stale 후보는 삭제보다 감점 또는 명시적 표기를 우선한다.

## 권장 검색 우선순위

1. canonical summary
2. 최근 historical log
3. 오래된 historical log
4. ephemeral note

semantic similarity만으로 정렬하지 말고 freshness와 document type을 함께 반영한다.

## 안티패턴

- 현재 기준 문서를 append-only 로그로만 남기는 것
- 스펙 변경 후 `spec-index`를 생략하는 것
- 과거 planning note를 최신 기준처럼 프롬프트에 넣는 것
- created_at만 보고 freshness를 판단하는 것

## 운영 체크리스트

- 이 문서는 canonical summary인가, historical log인가?
- 같은 대상을 대표하는 최신 요약이 이미 존재하는가?
- append-only로 남겨도 현재 기준과 혼동되지 않는가?
- 검색 시 updated_at과 sourceType이 드러나는가?
- 현재 질문이 최신 truth를 요구하는가, 과거 이유를 요구하는가?