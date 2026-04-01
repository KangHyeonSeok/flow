# Flow Iteration Rules

이 저장소에서는 매 시간 반복되는 개발 루프를 아래 순서로 수행한다.

## 목적

- 문서, 구현, 테스트 상태를 매 반복마다 다시 일치시킨다.
- 현재 반복에서 해야 할 일을 문서로 남기고, 그 문서를 기준으로 바로 구현한다.
- 다음 반복이 더 나아지도록 중요한 운영 지식을 이 파일에 축적한다.

## 반복 순서

1. 문서와 구현을 함께 읽고 현재 상태를 동기화한다.
2. 어긋난 문서를 먼저 수정한다.
3. 이번 반복의 할 일을 문서에 `Now`, `Next`, `Later` 또는 동등한 우선순위 구조로 정리한다.
4. `Now` 항목 중 가장 중요한 것부터 직접 구현한다.
5. 구현 후 테스트, 수동 확인, 남은 리스크를 문서에 반영한다.
6. 반복 종료 전에 이 파일의 `Iteration Notes`에 핵심 학습을 추가한다.

## 1단계: 문서 동기화와 할 일 정리

매 반복 시작 시 아래를 먼저 확인한다.

- 관련 spec 문서, 프로젝트 문서, 설계 문서
- 최근에 수정된 구현 파일과 테스트
- 직전 반복에서 남긴 `Iteration Notes`
- 현재 미완료 작업과 알려진 실패 상태

이 단계의 필수 결과물은 아래다.

- 구현과 맞지 않는 설명을 수정한 문서 변경
- 이번 반복의 작업 목록이 들어 있는 문서 변경
- 가정, 제약, 확인이 필요한 항목의 명시

문서에 할 일을 남길 때는 반드시 아래를 포함한다.

- 왜 필요한지
- 어떤 파일 또는 기능을 건드리는지
- 완료를 어떻게 확인할지

## 2단계: 할 일 기반 구현

- 문서에 정리한 `Now` 항목만 집어서 구현한다.
- 구현 중 범위가 커지면 먼저 문서의 할 일을 다시 쪼갠다.
- 구현 후에는 관련 테스트나 검증을 즉시 수행한다.
- 결과가 문서와 다르면 코드만 남기지 말고 문서도 함께 갱신한다.

## 반복 종료 규칙

매 반복이 끝날 때 아래를 반드시 남긴다.

- 완료한 항목
- 완료하지 못한 항목과 이유
- 다음 반복의 첫 작업
- 반복 중 새로 확인한 제약, 함정, 결정 사항
- 재사용할 명령, 테스트 절차, 확인 포인트

## Iteration Notes

Iteration Notes는 장기 로그가 아니라 다음 반복이 5분 안에 맥락을 복원하는 현재 스냅샷만 유지한다.
최신 상태로 아래 섹션만 갱신하고, 지난 시간대별 기록은 이 파일에 누적하지 않는다.

```md
## Current Iteration Snapshot

- Last Updated: YYYY-MM-DD HH:MM
- Focus: 이번 반복의 핵심 범위
- Done: 직전 반복까지 실제로 끝낸 작업
- Now: 지금 바로 구현할 1~3개
- Next: 다음 반복이 바로 시작할 작업 1~3개
- Risks: 남은 문제, 불확실성, 막힌 지점
- Learnings: 다음 반복이 반드시 기억해야 할 점
- Verification: 실행한 테스트, 확인한 화면, 확인하지 못한 항목
```

중복되는 긴 서술은 피하고, 현재 우선순위와 검증 결과만 남긴다.

## Current Iteration Snapshot

- Last Updated: 2026-04-02 03:05
- Focus: Partner 공유 런타임 build lock 대기 문제 복구 및 중복 startup build 제거.
- Done:
  - Phase 1-5 완료.
  - Hotspots 섹션 구현 + Sidebar quick links.
  - ListEditor 공통 컴포넌트 추출.
  - Runner 버그 수정 4건 + CLI --risk 플래그.
  - Partner 자기개선 시스템 (scorecard + plan + self-improve + retrospective).
  - 문서 리뷰 & 동기화 (Round 11).
  - 서브에이전트 프롬프트 정교화 (Round 12).
  - RAG 검색 품질 개선 (Round 13).
  - **관리자 기능 완성 (Round 14)**.
  - **잔여 관리자 기능 (Round 15)**.
  - **버그 수정 5건 (Round 16)**: recommendedCommands whitelist 일치, null guard 9건, allowTrailingArgs, dryRun cache 가드, project form companyId auto-fill.
  - **아키텍처 리뷰 & 구조적 개선 (Round 17)**:
    1. server.mjs 정규식 프리컴파일 — 30+ 인라인 regex → 모듈-레벨 상수 (RE_SCOPED_COMPANY, RE_ADMIN_COMPANY 등). 매 요청 regex 재생성 제거.
    2. 에러 핸들링 일관성 — 회사 생성(POST), 파트너 생성(POST) 라우트에 try/catch 래핑 추가. 기존엔 throw 시 외부 catch만 의존.
    3. 프로젝트 CRUD 캐시 무효화 — createProjectRecord/updateProjectRecord/deleteProjectRecord 후 `invalidateStackConfigCache()` 호출 추가. stack.json 쓰기 후 캐시가 stale 상태로 2초간 유지되던 버그 수정.
    4. 에러 응답 표준화 — 모든 에러 응답에 `ok: false` 필드 추가 (sendUnauthorized, sendForbidden, 404, 외부 catch 등). 프론트엔드에서 `result.ok` 기반 분기 가능.
    5. admin.js 폼 제출 추상화 — `submitFormWithDryRun()` + `submitSimpleForm()` 공통 헬퍼 추출. 9개 폼 제출 함수(회사/파트너/프로젝트 CRUD)를 헬퍼 기반으로 리팩터링. ~200줄 중복 제거.
  - **Slack task-progress 스로틀링 (Round 18)**:
    1. `task-progress`용 `chat.update()` 경로에 태스크별 1초 최소 간격 적용.
    2. 업데이트 문자열이 직전과 동일하면 Slack 갱신 생략.
    3. dev 컨테이너 재기동으로 변경 반영.
  - **Front-worker 자동 모델 라우팅 구현 (Round 19)**:
    1. worker 기본값을 `auto` 로 확장하고, 난이도 5단계(`very-easy`, `easy`, `normal`, `hard`, `very-hard`)별 모델 매핑 설정 추가.
    2. 관리자 설정에 worker preference(`auto` 포함)와 난이도별 모델 라우팅 UI/저장 경로 추가.
    3. front analyzer 출력에 `complexity` 계약 추가, front 가 task별 `workerModel` 을 확정해 queue payload 로 전달.
    4. worker-process 가 전역 `config.workerModel` 대신 task별 resolved model 로 실행하도록 변경.
    5. `copilot-gpt-5-mini` 전용 overflow concurrency 1 슬롯 추가.
    6. task별 `complexity`/`workerModel`/성공 여부/실행 시간 JSONL 메트릭 기록 추가 (`data/improvement/task-routing-metrics.jsonl`).
    7. 검증: `pnpm exec tsc --noEmit` 0 errors, `vitest` 대상 24 tests passed, `node --check` 0 errors.
  - **라우팅 기본값/운영 가시성 보강 (Round 20)**:
    1. 기본 라우팅 보정: `easy -> copilot-haiku`, `hard -> copilot-gpt-5.4`.
    2. 관리자 화면에 task routing metrics 요약 카드 + 난이도별 집계 표 추가.
    3. dashboard server 가 `data/improvement/task-routing-metrics.jsonl` 를 읽어 성공률/평균 시간 집계.
    4. `front-analyzer-complexity-tuning-plan.md` 추가 — Langfuse + DSPy 기반 complexity 튜닝 단계 계획 문서화.
-  - **Langfuse trace / labeling / 모델 breakdown 보강 (Round 21)**:
    1. `src/partner/front/front-trace.ts` 추가 — `front-analyzer`, `front-routing-plan`, `front-routing-complete` JSONL trace schema 구현 (`data/improvement/front-analyzer-traces.jsonl`).
    2. front controller + analyzer 연결 — request 단위 `traceId/sessionId` 생성, analyzer 결과와 최종 task outcome 을 동일 trace 축에 기록.
    3. `scripts/extract-complexity-labeling-samples.mjs` 추가 — 실패/장시간/manual override 우선 샘플 JSON 추출 (`data/improvement/complexity-labeling-samples.json`).
    4. 관리자 화면에 모델별 breakdown 표 추가, dashboard server 가 worker model 기준 성공률/평균 시간/주요 난이도 집계 제공.
    5. 검증: `pnpm exec tsc --noEmit`, `node --check`, `vitest` 26 passed, labeling extractor 실행 확인.
  - **기존 파트너 런타임 일괄 동기화 (Round 22)**:
    1. `scripts/lib/partner-stack-admin.mjs` 에 `syncAllPartnerRuntimeConfigs()` 추가 — legacy `claude-sonnet`/unset workerModel 을 `auto` 로 정규화하고 모든 agent 의 routing summary / runtime defaults 보정.
    2. `scripts/partner-sync-all-runtimes.mjs` 추가 + `pnpm partner:runtimes:sync-all` 스크립트 등록.
    3. 실제 운영 stack 에 적용 — 10개 파트너 모두 `workerModel: auto` + `workerModelRouting` + `.runtime/.env` 의 `PARTNER_COMPANY_ID`, `PARTNER_RUNTIME_PROFILE`, `PARTNER_FRONT_MODEL`, `PARTNER_WORKER_MODEL`, `PARTNER_WORKER_MODEL_ROUTING_JSON` 동기화 완료.
    4. `pnpm stack:generate` 재실행으로 generated compose 갱신.
  - **관리자 DSPy tuning control plane 추가 (Round 23)**:
    1. `scripts/lib/front-analyzer-dspy.mjs` 추가 — trace/gold-label/result 파일 기반 readiness/status 계산과 입력/출력 경로 규약 구현.
    2. `scripts/front-analyzer-dspy-tune.mjs` + `pnpm dspy:front-analyzer:tune` 추가 — readiness 확인 후 외부 DSPy tuner command 실행 및 input bundle 생성.
    3. `apps/company-dashboard/server.mjs` 에 DSPy tuning status 포함 + `/api/admin/front-analyzer/dspy-tune` 실행 엔드포인트 추가.
    4. `apps/company-dashboard/public/admin.html|admin.js|styles.css` 에 readiness 카드, 요구사항 목록, 실행 버튼, 최근 결과 표시 패널 추가.
    5. `docs/front-analyzer-complexity-tuning-plan.md` 에 관리자 실행 경로와 `PARTNER_DSPY_TUNER_COMMAND` 계약 반영.
  - **DSPy gold-label / result schema 고정 (Round 24)**:
    1. `scripts/lib/front-analyzer-dspy.mjs` 에 gold-label / tuning-result v1 schema normalization 추가, malformed 문서를 readiness issue 로 노출.
    2. `scripts/front-analyzer-dspy-tune.mjs` 가 input bundle 에 output contract 를 포함하고, tuner 결과 파일을 후검증하도록 강화.
    3. `src/front-analyzer-dspy.test.ts` 에 malformed label / schema normalization 경계 테스트 추가.
    4. `docs/front-analyzer-complexity-tuning-plan.md` 에 `complexity-gold-labels.json`, `front-analyzer-dspy-tuning-result.json` 예시와 필수 필드 명시.
  - **Langfuse trace / DSPy readiness 품질 보강 (Round 26)**:
    1. `src/partner/front/analyzer.ts` + `front-trace.ts` 에 `templateHash` / `promptHash` 추가 — trace 기준 prompt lineage 재현성 확보.
    2. `scripts/extract-complexity-labeling-samples.mjs` 가 analyzer fingerprint 와 completion context 를 샘플에 포함하도록 확장.
    3. `scripts/lib/front-analyzer-dspy.mjs` 가 gold label 난이도 coverage, 추천 보강사항, comparison baseline/tuned/delta metrics 를 status/result 에 포함하도록 강화.
    4. `apps/company-dashboard/public/admin.js` 가 coverage 카드와 tuning delta 를 관리자 패널에 표시하도록 보강.
    5. `src/front-analyzer-dspy.test.ts`, `src/partner/front/front-trace.test.ts` 에 coverage/comparison/fingerprint 회귀 테스트 추가.
  - **Partner build lock recovery 보강 (Round 25)**:
    1. `deploy/container-entrypoint.sh` 에 build lock heartbeat/metadata 추가, stale lock 기본값을 45초로 축소.
    2. abandoned `build.lock` 이 남아도 다른 컨테이너가 heartbeat 기준으로 자동 정리하도록 수정.
    3. `scripts/watch-restart.mjs` 가 초기 기동 시 기존 shared dist 산출물을 재사용하고 불필요한 startup `pnpm build` 를 건너뛰도록 수정.
    4. `deploy/container-entrypoint.sh` 의 build/deps hash 계산을 `scripts/partner-compose.mjs` 와 같은 relative-path 기반 규약으로 맞춰 host build 결과를 컨테이너가 재사용하도록 수정.
    5. `docs/server-restart-guide.md` 에 공유 build lock / stale lock recovery / startup build 생략 동작 문서화.
- Now:
  - gold-label 편집/업로드 UI 또는 관리 스크립트 추가.
  - Langfuse remote ingestion 연결 방식 결정.
  - build lock recovery 실제 컨테이너 startup 검증.
- Next:
  - tuned prompt 자동 diff/승인 플로우 정의.
  - 튜닝 실행 이력/기간 필터를 관리자 화면에 추가.
  - labeling sample -> gold label 변환 반자동화.
- Risks:
  - 회사 삭제 시 dashboardDataDir이 파일시스템에 남음 — 운영자 수동 정리 필요.
  - 파트너 삭제 시 워크스페이스/런타임 파일 잔존 — 수동 정리 필요.
  - 프롬프트 토큰 증가 (~2배) → 백엔드 비용/속도에 영향 가능. 모니터링 필요.
  - 관리자 메트릭은 현재 전체 누적 기준이다. 기간 필터는 아직 없다.
  - Langfuse remote ingestion 은 아직 미연결이다. 현재는 로컬 JSONL trace 를 기준 데이터로 사용한다.
  - `PARTNER_DSPY_TUNER_COMMAND` 가 미설정이면 관리자 버튼은 비활성화된다. 실제 DSPy optimizer 는 아직 외부 구현이 필요하다.
  - current tuning result comparison 은 핵심 수치만 표시한다. confusion matrix 등 richer eval artifact 는 아직 없다.
- Learnings:
  - stack.json 기반 CRUD에서 uniqueness 검증 시 자기 자신을 ignoredOwners에 포함해야 편집이 가능.
  - run-command 엔드포인트는 반드시 화이트리스트 방식으로 허용 명령만 실행. 프리폼 shell 실행은 보안 위험.
  - control lane 버튼과 명령 칩은 동일한 run-command 엔드포인트를 공유하여 중복 구현 방지.
  - 삭제 전에 referential integrity 확인 (회사→파트너 의존성) 필수.
  - admin.html에서 `<select>`에 빈 옵션 `(변경 안 함)`을 추가해야 편집 시 불필요한 변경 방지.
  - recommendedCommands 문자열은 run-command 화이트리스트와 정확히 일치해야 함. docker compose 직접 명령 → pnpm compose 으로 통일.
  - run-command 화이트리스트 매칭 시 trailing args 허용 여부를 명시적으로 제어 (`allowTrailingArgs` 플래그).
  - DOM querySelector 후 addEventListener 호출 전에 반드시 null 체크 필요. 방어적 가드 일관 적용.
  - Slack `task-progress`는 worker progress 스로틀과 별개 경로다. task별 별도 스로틀/중복 텍스트 방지가 없으면 `chat.update` 레이트리밋이 급증한다.
  - `auto` 모델 선택은 설정값으로만 남기면 안 된다. queue payload 에 task별 resolved model 을 실어야 실행/로그/디버깅이 일치한다.
  - 경량 모델 overflow concurrency 는 “총 동시성 증가”가 아니라 “mini 전용 추가 슬롯”으로 구현해야 무거운 작업이 extra slot을 잠식하지 않는다.
  - complexity 튜닝은 production runtime 에 DSPy 를 직접 넣기보다, Langfuse trace 기반 eval set + DSPy 오프라인 optimizer 로 프롬프트를 갱신하는 흐름이 안전하다.
  - trace 는 analyzer 결과만 남기면 부족하다. routing plan 과 최종 task outcome 을 같은 `traceId` 로 연결해야 labeling/eval set 에 바로 쓸 수 있다.
  - 기존 파트너 마이그레이션은 코드만 추가하면 적용되지 않는다. stack.json 과 각 workspace `.runtime/.env` 를 함께 동기화해야 실제 런타임 동작이 바뀐다.
  - 관리자에서 장시간 튜닝을 실행할 때는 일반 `run-command` 화이트리스트에 억지로 넣지 말고, 별도 endpoint 에 readiness 검증과 긴 timeout 을 분리하는 편이 안전하다.
  - DSPy runner 는 직접 optimizer 를 내장하기보다 input/output/prompt-output 계약만 먼저 고정하면 Python 구현과 JS control plane 을 독립적으로 진화시킬 수 있다.
  - gold-label 과 tuning-result 를 느슨하게 파싱하면 운영자는 파일이 깨진 줄 모른 채 readiness 0건 상태만 보게 된다. 스키마 오류를 명시적 requirement/issue 로 올려야 디버깅 시간이 줄어든다.
  - DSPy readiness 는 총 label 수만 보면 안 된다. 난이도 coverage 와 hard/very-hard 대표성이 부족하면 튜닝 지표가 좋아 보여도 실제 라우팅 개선으로 이어지지 않을 수 있다.
  - Langfuse trace 에 prompt/template fingerprint 가 없으면 같은 trace corpus 도 어떤 analyzer prompt 버전에서 생성된 데이터인지 추적하기 어렵다.
  - 공유 build lock 을 디렉터리 존재 여부만으로 관리하면 강제 재시작 뒤 orphan lock 이 남아 전체 파트너 startup 을 막을 수 있다. heartbeat 기반 stale 판단이 필요하다.
  - entrypoint 에서 이미 빌드한 뒤 watch 모드가 startup 직후 다시 `pnpm build` 를 수행하면 공용 dist 볼륨에서 중복 빌드 비용이 커진다. 초기 startup 은 기존 산출물 재사용이 낫다.
  - host compose wrapper 와 container entrypoint 의 build hash 알고리즘이 다르면 `.partner-runtime/dist` 와 state 를 미리 동기화해도 각 컨테이너가 모두 재빌드한다. relative path + file hash 계약을 양쪽에서 동일하게 유지해야 한다.
- Verification:
  - `node --check` — server.mjs, admin.js, partner-stack-admin.mjs, extract-complexity-labeling-samples.mjs 모두 0 errors.
  - `pnpm exec tsc --noEmit` — 0 errors.
  - `pnpm exec vitest run src/dependency-graph.test.ts src/worker-queue.test.ts src/partner/front/model-routing.test.ts src/partner/front/front-trace.test.ts src/config.test.ts` — 26 passed, 0 failed.
  - `node scripts/extract-complexity-labeling-samples.mjs --limit 5` — 실행 성공, 현재 trace 부재로 0 candidates 출력.
  - `node scripts/partner-sync-all-runtimes.mjs --dry-run` — 10개 파트너 변경 예정 확인.
  - `node scripts/partner-sync-all-runtimes.mjs` — 10개 파트너 동기화 완료.
  - `pnpm stack:generate` — `docker-compose.generated.yml` 재생성 완료.
  - `pnpm compose -- up -d --force-recreate` — 10개 파트너 + company-dashboard 컨테이너 recreate 완료.
  - `pnpm dashboard:status:build` — 5개 회사 dashboard status 재생성 완료.
  - `node --check apps/company-dashboard/server.mjs && node --check scripts/lib/front-analyzer-dspy.mjs && node --check scripts/front-analyzer-dspy-tune.mjs` — 0 errors.
  - `pnpm exec tsc --noEmit && pnpm exec vitest run src/front-analyzer-dspy.test.ts src/partner/front/front-trace.test.ts src/partner/front/model-routing.test.ts` — 7 passed, 0 failed.
  - Pending: `sh deploy/container-entrypoint.sh` build lock recovery 동작 확인.
  - Pending: `pnpm compose -- up -d --force-recreate` 후 파트너 컨테이너 startup 검증.

## 2026-04-01 (Copilot ACP NDJSON 프로토콜 수정 Round 15)

- Context: Copilot ACP 프로세스가 initialize 요청에 무응답 → 1시간 타임아웃.
- Root Cause: Partner의 `copilot-acp.ts`가 Content-Length framing (LSP 스타일 `Content-Length: N\r\n\r\n{json}`)으로 메시지를 전송했으나, Copilot ACP는 NDJSON (`{json}\n`) 프로토콜 사용. copilot이 Content-Length 헤더를 파싱하지 못해 무한 대기.
- Done:
  1. `writeMessage()` — Content-Length framing → NDJSON (`${payload}\n`) 전환.
  2. `flushStdoutBuffer()` — Content-Length 파싱 로직 제거, 순수 newline 기반 NDJSON 파싱만 유지.
  3. 불필요한 `findHeaderEnd()`, `headerSeparatorLength()` helper 제거.
  4. `initializeTimeoutMs` 30초 → 1시간 (이전 반복에서 변경).
  5. dev 컨테이너 end-to-end 테스트: initialize → session/new → session/prompt 완료 확인.
- Learnings:
  - Copilot ACP (v1.0.12+)는 NDJSON 프로토콜만 사용. Content-Length framing(MCP/LSP 스타일)은 지원하지 않음.
  - `protocolVersion: 1` (number). string `"2025-03-26"` 전달 시 `invalid_type` 에러.
  - 컨테이너 내 copilot은 npm-loader.js → native binary (`@github/copilot-linux-x64/copilot`) 경로로 실행. `import.meta.resolve`로 binary를 찾아 `spawnSync` 실행.
  - 호스트 macOS copilot (v1.0.2)은 v1.0.14 업데이트 대기 상태에서 blocking 가능.
- Verification: `pnpm build` 0 errors. 컨테이너 내 Node.js 테스트 스크립트로 3단계 ACP 세션 완료.

## 작업 방식 규칙

- 문서 업데이트 없이 구현만 하고 끝내지 않는다.
- TODO는 채팅에만 남기지 말고 저장소 문서에도 남긴다.
- 막힌 이유가 있으면 원인과 재시도 조건을 적는다.
- 큰 결정은 설계 문서에 반영하고, 이 파일에는 요약과 후속 액션만 남긴다.
- 다음 반복이 시작 5분 안에 맥락을 복원할 수 있어야 한다.

## 2026-04-01 (Partner 자기개선 시스템 구축)

- Context: 파트너 에이전트의 반복적이고 방향 없는 자기개선 루프를 구조화.
- Done: scorecard.ts (메트릭 수집+추세), plan.ts (개선 백로그+dedup), self-improve.ts 연동, retrospective.ts 연동, policy 문서 갱신.
- Next: SpecValidator 프롬프트 튜닝, `flow init` 명령, retry backoff 실질화.
- Risks: plan.json 에이전트 직접 수정 시 JSON 파싱 오류 가능. 첫 실행 시 빈 메트릭.
- Learnings: 자기개선의 핵심 문제는 방향 없는 반복. signature 기반 dedup + 계획 우선순위 부여로 구조적 해결.
- Verification: `pnpm build` 0 errors. `tsc --noEmit` 0 errors.

## 2026-04-01 (문서 리뷰 & 코드-문서 동기화 Round 11)

- Context: 23개 문서 전수 검토, 코드-문서 불일치 6건 수정, 완료 문서 4건 아카이브.
- Done: server-restart-guide dist/index.js→dist/main-partner.js, watch-restart.mjs resolveEntrypoint(), company-dashboard-plan 경로 수정, rag-implementation-plan 상태 테이블, partner-system-plan-v1 참조 3건 추가, 완료 문서 4건 아카이브 노티스.
- Next: SpecValidator 프롬프트 튜닝, `flow init` 명령, retry backoff 실질화.
- Risks: ~15개 root shim 파일 잔존. 문서 자동 수정 시 제목 텍스트 미리 확인 필수.
- Learnings: 엔트리포인트 변경 시 스크립트+docs 동기화 필요. 한글 제목은 반드시 실제 텍스트 확인 후 수정.
- Verification: `tsc --noEmit` 0 errors. 문서 6건+스크립트 1건 수정 확인.

## 2026-04-01 (서브에이전트 프롬프트 정교화 Round 12)

- Context: 러너 PromptBuilder.cs + PlannerPromptBuilder.cs 의 6개 역할 프롬프트를 전면 개선.
- Done: SpecValidator(AC Precheck + Validation), Planner, Architect, Developer, TestGenerator, PlannerPromptBuilder(Path B) 프롬프트 체계화. PromptBuilder.cs 337줄 → 508줄.
- Next: `flow init` 명령, retry backoff 실질화, OutputParser 테스트 보강.
- Risks: 프롬프트 토큰 ~2배 증가 → 비용/속도 모니터링 필요. rework 에스컬레이션 강도 실사용 후 조정 필요.
- Learnings: ActivityAction은 enum (문자열 비교 불가). 프롬프트에 "허용 이벤트" 목록을 명시하면 잘못된 이벤트 제안 사전 차단 효과. rework 루프 방지 핵심은 횟수별 에스컬레이션 + 이전 판단 일관성 원칙.
- Verification: `dotnet build` 0 errors. `dotnet test` 328 passed, 0 failed.

## 2026-04-01 (RAG 검색 품질 개선 Round 13)

- Context: Partner RAG 시스템의 검색 품질 4가지 약점 식별 및 개선.
- Done: heading-aware chunking (`findActiveHeading()`), AND/OR fallback FTS5 쿼리, 5분 주기 리인덱싱 (`setInterval().unref()`), snippet trimming (`trimToRelevantParagraphs()`), `rag-implementation-plan.md` 상태 테이블 정확도 수정.
- Next: `flow init` 명령, retry backoff 실질화, OutputParser 테스트 보강.
- Risks: 프롬프트 토큰 증가 (~2배) → 백엔드 비용/속도에 영향 가능. periodic reindex가 대량 파일에서 I/O 부담 가능.
- Learnings: FTS5 AND 쿼리가 한국어 복합 키워드에서 precision 크게 향상. `setInterval().unref()` — Node.js 프로세스 종료를 블로킹하지 않으면서 주기 작업 실행. chunk에 heading context가 없으면 FTS5에서 섹션 제목 기반 검색 불가 — heading prefix 필수. snippet trimming: 키워드 포함 문단만 추출하면 프롬프트 토큰 30-50% 절감 가능.
- Verification: `tsc --noEmit` 0 errors. `vitest run src/rag/service.test.ts` 10 passed, 0 failed.