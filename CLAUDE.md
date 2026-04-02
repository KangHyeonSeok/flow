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

- Last Updated: 2026-04-02 15:56
- Focus: Slack clone 진행 상태 스팸 원인을 front task-progress 경로 기준으로 정리하고, delivery throttle + progress 요약을 live dev에 반영한다.
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
  - **프로젝트 작업 폴더 경로 입력 + 프롬프트 주입 (Round 27)**:
    1. 프로젝트 메타데이터에 `workspacePath` 추가 — stack CRUD, REST API, CLI, dashboard project modal 모두 자유 입력 필드로 연결.
    2. company dashboard 프로젝트 카드/상세에 작업 폴더 경로 표시 추가.
    3. Slack 채널 대화의 `Slack conversation context` 와 worker의 `Channel project context` 모두에 작업 폴더 경로가 주입되도록 연결.
    4. `project-context-builder` 캐시를 프로젝트 CRUD 후 즉시 무효화하도록 API route 보강.
    5. 검증: `pnpm exec tsc --noEmit`, `vitest` 7 passed, `node --check` 0 errors.
  - **Copilot ACP cwd/진단 보강 (Round 28)**:
    1. `src/utils/copilot-acp-runtime.ts` 추가 — 프롬프트 내 `/workspace|/shared|/app` 경로를 실제 runtime root 로 매핑하고 session cwd 후보를 결정하는 헬퍼 구현.
    2. `src/utils/copilot-acp.ts` 가 process cwd 를 `/app` 대신 partner workspace root 로 사용하고, `session/new` 에도 resolved cwd 를 전달하도록 수정.
    3. Copilot ACP 실패 detail 에 `processCwd`, `sessionCwd`, `workspaceDir`, `sharedDir`, `promptPathHint`, `stderrTail` 을 포함하도록 진단 포맷 보강.
    4. `src/copilot-acp-runtime.test.ts` 추가 — cwd resolution / diagnostic formatting 회귀 테스트 고정.
  - **Native spec ops 전체 권한 기본화 (Round 29)**:
    1. `agents/default.json`, runtime `agent.json` 생성 템플릿, native-spec-ops fallback 을 `enabled: true`, `allow: ["*:*"]`, `deny: []` 로 정렬.
    2. runtime sync 가 기존 `.runtime/agent/agent.json` 도 함께 보정하도록 확장하고, partner별 `companyId` scope 를 권한 블록에 주입.
    3. `partner-sync-all-runtimes` 출력과 문서를 전체 권한 기본값 기준으로 갱신.
    4. `stack-config.md`, `native-spec-ops/SKILL.md` 에 deny-only 운영 예시 추가.
    5. `pnpm compose -- up -d --force-recreate` 로 전체 partner + dashboard 컨테이너 재기동 후 live `policy:show` 검증 완료.
  - **Copilot ACP 실패 구조화 (Round 30)**:
    1. worker IPC / dependency graph / front trace / task routing metrics 에 `failureInfo` 구조(`provider`, `type`, `sessionCwd`, `promptPathHint` 등) 추가.
    2. Copilot ACP / Claude CLI / dependency 실패를 공통 classifier 로 정규화하고 timeout slice 를 metrics/trace 에 남기도록 연결.
    3. front trace completion 에 failure summary 집계 추가, tuning 문서에 metrics schema 확장 반영.
  - **Copilot ACP cwd anchor 완화 (Round 32)**:
    1. `resolveCopilotWorkingDirectory()` 가 deepest leaf 대신 root 아래 2단계 anchor cwd 를 선택하도록 조정.
    2. exact 파일/하위 경로는 기존처럼 `promptPathHint` 로 유지하여 범용 작업과 타깃 파일 탐색을 함께 보존.
    3. `/workspace/...` 일반 운영 문서/비프로젝트 경로도 broader workspace anchor 로 처리하는 회귀 테스트 추가.
  - **Schedule front fast-path + shared skill command (Round 31)**:
    1. `src/partner/schedule-ops.ts` 추가 — `data/schedules.json` CRUD/list/validation 공용 모듈 구현, front 와 skill CLI 가 동일 로직 재사용.
    2. front analyzer 계약에 `type: "schedule"` 액션 추가, schedule 조회/추가/수정/삭제/비활성화 요청을 worker 분해 없이 front 에서 즉시 실행하도록 연결.
    3. `skills/schedule/schedule.ts` 추가 + `skills/schedule/SKILL.md` 갱신 — 실행형 스킬 명령과 front fast-path 계약 문서화.
    4. `src/schedule-ops.test.ts`, `src/front-analyzer.test.ts` 추가 — CRUD/cron validation 및 analyzer schedule 파싱 회귀 테스트 고정.
    5. bulk 연산(`delete-all`, `disable-all`) 추가 — `전체 스케줄 삭제/중지` 요청도 개별 task 4개 분해 없이 front 에서 직접 처리하도록 확장.
  - **Front single-task 우선 정책 전환 (Round 33)**:
    1. `prompts/front-analyzer.template.md` 를 single-task 우선 정책으로 수정하고, 병렬 이득/명확한 단계 경계가 있을 때만 decomposition 하도록 규칙을 재정의.
    2. `src/partner/front/model-routing.ts` 에서 `taskCount` 기반 complexity 상승을 제거해 decomposition 여부와 complexity 판정을 분리.
    3. `src/partner/front/task-collapse.ts` 추가 — 동일 고성능 모델(`copilot-gpt-5.4`/`copilot-opus`/`claude-opus`)로 가는 선형 task chain 은 graph 생성 전 단일 task로 재통합.
    4. `src/partner/front/task-collapse.test.ts`, `src/partner/front/model-routing.test.ts` 로 회귀 테스트 추가.
  - **Copilot ACP 프로젝트/공유 경로 힌트 보강 (Round 34)**:
    1. `src/company/project-context-builder.ts` 에 `toContainerWorkspacePathHint()` 추가 — 저장된 상대 `workspacePath` 를 `/workspace/...` 또는 `/shared/...` 컨테이너 절대 경로 힌트로 변환.
    2. 채널 프로젝트 컨텍스트와 Slack 컨텍스트에 `Container path hint` 를 함께 주입해 Copilot 이 `/app` 대신 실제 작업 루트를 찾기 쉽게 보강.
    3. `src/partner/worker/worker-process.ts` 가 decomposition 된 worker task 에도 channel project context 를 주입하도록 수정.
    4. `src/project-metadata.test.ts` 기대값 확장 후 `tsc` + 관련 `vitest` 19개 통과로 회귀 검증.
  - **Front-safe Slack skill fast-path 추가 (Round 35)**:
    1. front analyzer 계약에 `type: "skill"` 추가 — `slack-history`, `slack-canvas`, `slack-file-download` 만 whitelist 기반으로 front 에서 직접 실행하도록 확장.
    2. `src/partner/front/front-skill-ops.ts` 추가 — 고정된 `pnpm exec tsx skills/...` 커맨드만 실행하고, 첨부 Slack 파일 여러 개도 front 가 일괄 다운로드 후 단일 후속 task 로 worker 에 넘기도록 구현.
    3. `prompts/front-analyzer.template.md` 에 Slack skill direct/delegate 규칙 추가 — retrieval 전용 요청은 direct, 분석/요약 후속 작업은 `delegatePrompt` 기반 단일 task 로 위임.
    4. `src/front-analyzer.test.ts`, `src/front-skill-ops.test.ts` 로 skill parsing / 다중 첨부파일 batch planning 회귀 테스트 추가.
  - **copilot-cli 스킬 제거 및 참조 정리 (Round 36)**:
    1. 공용 `skills/copilot-cli` 와 runtime 설치본(`subak`, `serv`)을 삭제.
    2. `skill-registry`, 기본/system prompt, Claude skill sync 스크립트에서 `copilot-cli` 참조 제거.
    3. active runtime prompt/knowledge/schedule 에 남은 `copilot-cli` 의존 문구를 일반 지침으로 치환.
    4. archived flow 문서의 `copilot-cli` actor 예시를 generic actor 로 정리.
  - **Copilot ACP idle timeout 15분 상향 (Round 37)**:
    1. `src/core/config.ts` 기본 `COPILOT_AGENT_IDLE_TIMEOUT_MS` 를 180000 -> 900000 으로 상향.
    2. `scripts/lib/partner-stack-admin.mjs` runtime `.env` 템플릿 기본값을 15분으로 상향.
    3. `docs/stack-config.md` 에 현재 기본값 15분을 명시.
    4. `dev/.runtime/.env` live 값을 15분으로 갱신하고 재기동 대상으로 표시.
  - **Slack progress delivery hardening (Round 38)**:
    1. clone 요청 trace(`front-1-1775112474253`) 기준으로 spam 경로를 front single-task `task-progress` + raw git clone status 로 특정.
    2. `src/partner/slack/app.ts` 에 message key 기준 공통 Slack update throttle/dedupe 추가 — front/direct progress 모두 `config.slack.progressUpdateMinIntervalMs` 를 공유.
    3. `src/partner/slack/progress.ts` 가 `git clone` 전송 로그와 multiline shell output 을 짧은 상태문으로 요약하도록 보강.
    4. `src/partner/slack/progress.test.ts` 에 clone/multiline progress 회귀 테스트 추가.
  - **pm -> dev channel smoke test 문서화 (Round 39)**:
    1. `C0ARA2ZF3BJ` 채널에서 `pm` 이 `dev` 를 mention 하는 `app_mention` 경로로 단일 파일 생성 요청 검증.
    2. thread reply, `partner-dev` 로그, `/workspace/slack-smoke/pm-channel-test.txt` 생성까지 성공 확인.
    3. `docs/slack-channel-smoke-test.md` 에 재현 절차, 성공 기준, 확인 명령, DM 스코프 제약 문서화.
  - **Completion 본문 transcript 정제 (Round 40)**:
    1. `src/partner/front/reviewer.ts` 에 single-task 완료 응답 후처리 추가 — `Cloning repository`, `STATUS:`, `<exited with exit code ...>` 같은 transcript marker 가 섞인 경우 마지막 사용자용 요약 anchor 부터만 전달.
    2. `src/partner/front/reviewer.test.ts` 추가 — clone completion transcript trimming 회귀 테스트 고정.
- Now:
  - clone completion 본문이 실제 Slack thread 에서 정제된 형태로 보이는지 재확인.
  - DM/direct 요청이 프로젝트 힌트 없이 `/workspace` fallback 으로 내려가는 경로를 보강할 지점 정리.
  - 간단한 계산기 요청이 MyKnitLog 작업으로 오인된 front context/Slack 문맥 사례를 분리 분석.
- Next:
  - front-safe skill 범위를 upload 포함으로 넓힐지 검토.
  - 필요 시 anchor depth (1단계 vs 2단계) 조정 실험.
  - labeling sample -> gold label 변환 반자동화.
- Risks:
  - 회사 삭제 시 dashboardDataDir이 파일시스템에 남음 — 운영자 수동 정리 필요.
  - 파트너 삭제 시 워크스페이스/런타임 파일 잔존 — 수동 정리 필요.
  - 프롬프트 토큰 증가 (~2배) → 백엔드 비용/속도에 영향 가능. 모니터링 필요.
  - Copilot ACP 는 initialize 성공 후에도 잘못된 session cwd 또는 긴 무응답으로 180초 idle timeout 이 발생할 수 있다.
  - ACP timeout 은 현재 대부분 `copilot-gpt-5.4` 대형 탐색 작업에서 보이지만, structured metric 누적 전까지 cwd 문제와 idle budget 문제의 비중은 확정할 수 없다.
  - anchor cwd 를 너무 얕게 잡으면 검색 범위가 다시 넓어져 timeout 이 늘 수 있다. 현재는 root 아래 2단계 anchor 로만 완화.
  - native spec ops 전체 권한 기본화 이후에는 개별 제한이 필요하면 runtime agent.json 의 `deny` 로 명시해야 한다.
  - 관리자 메트릭은 현재 전체 누적 기준이다. 기간 필터는 아직 없다.
  - Langfuse remote ingestion 은 아직 미연결이다. 현재는 로컬 JSONL trace 를 기준 데이터로 사용한다.
  - `PARTNER_DSPY_TUNER_COMMAND` 가 미설정이면 관리자 버튼은 비활성화된다. 실제 DSPy optimizer 는 아직 외부 구현이 필요하다.
  - current tuning result comparison 은 핵심 수치만 표시한다. confusion matrix 등 richer eval artifact 는 아직 없다.
  - single-task 우선 전환 후에도 병렬성이 실제로 필요한 작업 slice 는 별도 샘플링이 필요하다.
  - 모든 skills/ 를 front 로 직접 실행하면 권한/부작용 범위가 커진다. 현재는 read-mostly Slack skill 3종만 whitelist 했다.
  - skill direct/delegate 결과가 길어지면 후속 단일 task 프롬프트 길이가 커질 수 있다. trace 와 응답 시간 모니터링이 필요하다.
  - idle timeout 을 15분으로 올려도 DM/direct 요청이 프로젝트 힌트 없이 `/workspace` 로 시작되면 대형 저장소 루트 탐색 비용 문제는 남을 수 있다.
  - progress 텍스트를 과도하게 요약하면 장기 작업의 세부 단계가 덜 보일 수 있다. 실제 운영에서 정보 손실 여부 확인이 필요하다.
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
  - front single-task 경로에서도 shell raw output 이 progress status 로 들어오면 upstream throttle 만으로는 Slack flood 를 막기 부족하다. Slack 전달 직전 공통 throttle 과 상태 요약이 함께 필요하다.
  - `auto` 모델 선택은 설정값으로만 남기면 안 된다. queue payload 에 task별 resolved model 을 실어야 실행/로그/디버깅이 일치한다.
  - 경량 모델 overflow concurrency 는 “총 동시성 증가”가 아니라 “mini 전용 추가 슬롯”으로 구현해야 무거운 작업이 extra slot을 잠식하지 않는다.
  - Copilot ACP 는 `--add-dir` 만 추가해도 충분하지 않다. process cwd 와 `session/new` cwd 가 partner workspace 와 맞지 않으면 `/workspace` 탐색 실패가 180초 idle timeout 으로 표면화될 수 있다.
  - Copilot ACP timeout detail 에 stderr tail 과 cwd 계열 진단값이 없으면 initialize 성공/세션 실패/도구 무응답을 로그만으로 구분하기 어렵다.
  - ACP 실패는 문자열 한 줄만 남기면 routing/tuning 개선에 쓸 수 없다. worker IPC 단계에서 provider/type/sessionCwd/promptPathHint 를 구조화해 trace/metrics 로 흘려야 slice 분석이 가능하다.
  - Partner 작업은 프로젝트 구현 외에도 운영 문서, shared 자산, 루트 스크립트 수정이 섞인다. session cwd 를 특정 프로젝트 루트로 고정하면 범용 작업 실패가 늘 수 있어 anchor + prompt hint 조합이 더 안전하다.
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
  - 프로젝트별 작업 폴더는 `shared/projects/...`, `workspace/...`, 그 외 커스텀 루트가 혼재할 수 있으므로 enum/path prefix 제한보다 freeform string + prompt 주입이 운영상 안전하다.
  - native spec ops 권한을 기본 전체 허용으로 바꾸려면 default config 만 바꾸면 부족하다. runtime 생성 템플릿, fallback 정책, 기존 `.runtime/agent/agent.json` sync 를 함께 맞춰야 한다.
  - 스케줄 편집은 worker로 넘기면 model routing + queue 대기 비용만 추가되고 실질 작업은 로컬 JSON CRUD다. front가 구조화 액션으로 바로 처리하는 편이 더 맞다.
  - 실행형 스킬이 필요해도 비즈니스 로직은 skill 파일에만 두지 말고 `src` 공용 모듈로 빼서 front fast-path 와 CLI가 같은 검증/쓰기 규약을 공유해야 drift가 줄어든다.
  - schedule fast-path 가 들어가도 지원 연산 집합에 없는 요청은 모델이 기존처럼 decomposition 으로 우회한다. `전체 삭제/전체 중지` 같은 bulk 의도도 별도 operation 으로 계약에 넣어야 실제 현장 요청이 direct path 로 고정된다.
  - decomposition 여부와 complexity를 같은 신호로 취급하면 분해된 요청이 자동으로 더 비싼 모델로 몰리는 자기증폭이 생긴다. complexity는 작업 본질과 실패 비용만 반영해야 한다.
  - 프로젝트 메타데이터의 `workspacePath` 는 운영 편의상 상대 경로로 저장해도 되지만, Copilot prompt 에는 `/workspace/...` 또는 `/shared/...` 절대 컨테이너 힌트를 같이 넣어야 cwd resolver 가 실제 경로 후보로 인식한다.
  - direct worker 경로만 프로젝트 컨텍스트를 받으면 부족하다. decomposition 된 child task 에도 같은 channel project context 를 넣어야 `/app` 기준 오판이 줄어든다.
  - 스킬 fast-path 는 "모든 skill front 실행"이 아니라 deterministic + whitelist 기반으로 좁혀야 안전하다.
  - Slack 첨부 파일이 여러 개여도 front 가 먼저 일괄 다운로드하고, 후속 해석만 단일 worker task 로 넘기면 file-count 기반 decomposition 압력을 줄일 수 있다.
  - 스킬 제거는 폴더 삭제만으로 끝나지 않는다. runtime prompt, knowledge, installed skill copy, sync 스크립트까지 같이 정리해야 재등장과 stale 참조를 막을 수 있다.
  - Slack 문맥에 직전 MyKnitLog 스레드 이력이 길게 섞인 상태에서 direct DM 요청이 들어오면, 명시적 프로젝트 힌트가 없을 때 front/worker 가 이전 프로젝트 맥락으로 오인할 수 있다. timeout 상향만으로는 해결되지 않는다.
- Verification:
  - `node --check` — server.mjs, admin.js, partner-stack-admin.mjs, extract-complexity-labeling-samples.mjs 모두 0 errors.
  - `pnpm exec tsc --noEmit` — 0 errors.
  - `pnpm exec vitest run src/partner/front/model-routing.test.ts src/partner/front/task-collapse.test.ts src/front-analyzer.test.ts src/partner/front/front-trace.test.ts` — 8 passed, 0 failed.
  - `pnpm exec vitest run src/dependency-graph.test.ts src/worker-queue.test.ts src/partner/front/model-routing.test.ts src/partner/front/front-trace.test.ts src/config.test.ts` — 26 passed, 0 failed.
  - `pnpm exec tsc --noEmit && pnpm exec vitest run src/schedule-ops.test.ts src/front-analyzer.test.ts && pnpm exec tsx skills/schedule/schedule.ts list` — 5 passed, CLI 출력 `현재 등록된 스케줄이 없습니다.` 확인.
  - `pnpm compose -- up -d --force-recreate subak` — 대상 partner container 재생성 완료.
  - `docker exec partner-subak grep -n "delete-all\|disable-all\|전체 스케줄 삭제" /app/prompts/front-analyzer.template.md` — 런타임 컨테이너에 bulk schedule prompt 규칙 반영 확인.
  - `node scripts/extract-complexity-labeling-samples.mjs --limit 5` — 실행 성공, 현재 trace 부재로 0 candidates 출력.
  - `node scripts/partner-sync-all-runtimes.mjs --dry-run` — 10개 파트너 변경 예정 확인.
  - `node scripts/partner-sync-all-runtimes.mjs` — 10개 파트너 동기화 완료.
  - `pnpm stack:generate` — `docker-compose.generated.yml` 재생성 완료.
  - `pnpm compose -- up -d --force-recreate` — 10개 파트너 + company-dashboard 컨테이너 recreate 완료.
  - `pnpm dashboard:status:build` — 5개 회사 dashboard status 재생성 완료.
  - `node --check apps/company-dashboard/server.mjs && node --check scripts/lib/front-analyzer-dspy.mjs && node --check scripts/front-analyzer-dspy-tune.mjs` — 0 errors.
  - `pnpm exec tsc --noEmit && pnpm exec vitest run src/front-analyzer-dspy.test.ts src/partner/front/front-trace.test.ts src/partner/front/model-routing.test.ts` — 7 passed, 0 failed.
  - `pnpm exec tsc --noEmit && pnpm exec vitest run src/project-metadata.test.ts src/partner/slack/progress.test.ts src/partner/front/front-trace.test.ts` — 7 passed, 0 failed.
  - `pnpm exec tsc --noEmit` — Copilot ACP cwd/diagnostic 변경 후 0 errors.
  - `pnpm exec vitest run src/copilot-acp-runtime.test.ts` — 3 passed, 0 failed.
  - `pnpm compose -- up -d --force-recreate subak dev` — 대상 partner container 재기동 완료.
  - `docker exec partner-dev ... runCopilotSimplePrompt("Read /workspace/MyKnitLog/flutter_app/pubspec.yaml...")` — 성공, runtime log 에 `Delegating ... cwd=/workspace/MyKnitLog/flutter_app` / `Starting Copilot ACP session ... cwd=/workspace/MyKnitLog/flutter_app` 확인.
  - `docker exec partner-dev grep session/new /tmp/copilot-acp-debug.log` — `session/new` payload 의 `cwd` 가 `/workspace/MyKnitLog/flutter_app` 로 기록됨 확인.
  - `node --check apps/company-dashboard/public/app.js && node --check apps/company-dashboard/server.mjs` — 0 errors.
  - `pnpm partner:runtimes:sync-all --dry-run` — 10개 파트너의 native spec ops 권한 변경 예정 확인.
  - `pnpm partner:runtimes:sync-all` — 10개 파트너 runtime agent.json 동기화 완료.
  - `AGENT_CONFIG_PATH=/Users/KangHyeonSeok/Documents/Partners/subak/.runtime/agent/agent.json pnpm exec tsx skills/native-spec-ops/native-spec-ops.ts policy:show` — `enabled=true`, `companyId=Playfull`, `allow=["*:*"]`, `deny=[]` 확인.
  - `pnpm compose -- up -d --force-recreate` — partner 10개 + company-dashboard 컨테이너 재생성 완료.
  - `pnpm dashboard:status:build` — 5개 회사 dashboard status 재생성 완료.
  - `docker exec partner-subak pnpm exec tsx skills/native-spec-ops/native-spec-ops.ts policy:show` — live container 에서 `enabled=true`, `allow=["*:*"]`, `deny=[]` 확인.
  - `pnpm exec tsc --noEmit` — Copilot ACP failureInfo 구조화 변경 후 0 errors.
  - `pnpm exec vitest run src/partner/worker/task-failure.test.ts src/worker-queue.test.ts src/dependency-graph.test.ts src/partner/front/front-trace.test.ts` — 24 passed, 0 failed.
  - `pnpm exec tsc --noEmit` — anchor cwd 변경 후 0 errors.
  - `pnpm exec vitest run src/copilot-acp-runtime.test.ts src/partner/worker/task-failure.test.ts src/worker-queue.test.ts src/dependency-graph.test.ts src/partner/front/front-trace.test.ts` — 28 passed, 0 failed.
  - `pnpm exec tsc --noEmit && pnpm exec vitest run src/project-metadata.test.ts src/partner/front/front-trace.test.ts src/partner/worker/task-failure.test.ts src/worker-queue.test.ts src/copilot-acp-runtime.test.ts` — 19 passed, 0 failed.
  - `pnpm compose -- up -d --force-recreate` — partner 10개 + company-dashboard 컨테이너 재생성 완료, host `pnpm build` 포함 반영.
  - `docker exec partner-dev sh -lc "grep -n 'Container path hint' /app/dist/company/project-context-builder.js /app/dist/partner/slack/app.js"` — live container dist 에 project/slack path hint 반영 확인.
  - `docker exec partner-dev sh -lc "grep -n 'Project context injection failed\|Channel project context' /app/dist/partner/worker/worker-process.js"` — live container dist 에 decomposed worker project context 주입 반영 확인.
  - `pnpm exec tsc --noEmit && pnpm exec vitest run src/schedule-ops.test.ts src/front-analyzer.test.ts && pnpm exec tsx skills/schedule/schedule.ts list` — 3 passed, CLI 출력 `현재 등록된 스케줄이 없습니다.` 확인.
  - `pnpm exec tsc --noEmit` — front skill fast-path 변경 후 0 errors.
  - `pnpm exec vitest run src/front-analyzer.test.ts src/front-skill-ops.test.ts src/partner/front/front-trace.test.ts` — 7 passed, 0 failed.
  - `grep -RInE --exclude-dir=node_modules --exclude-dir=.git --exclude-dir=dist 'copilot-cli|skills/copilot-cli|copilot\\.sh delegate|copilot\\.sh ask' ...` — active code/runtime 문서 참조 위치 확인, history/backups 잔존 확인.
  - `node scripts/sync-claude-skills.mjs` — `~/.claude/skills` 재동기화 완료, installed skill 목록에서 `copilot-cli` 제거 확인.
  - `pnpm exec tsc --noEmit` — Slack progress delivery hardening 변경 후 0 errors.
  - `pnpm exec vitest run src/partner/slack/progress.test.ts` — 6 passed, 0 failed.
  - `pnpm compose -- up -d --force-recreate dev` — dev 컨테이너 재기동 완료.
  - `docker exec partner-dev sh -lc "grep -n 'git clone 진행 중\|progressUpdateMinIntervalMs\|updateSlackMessage' /app/dist/partner/slack/progress.js /app/dist/partner/slack/app.js"` — live dist 에 progress 요약/공통 throttle 반영 확인.
  - `docker exec partner-subak ... chat.postMessage(channel=C0ARA2ZF3BJ, text='<@U0ALGECA883> /workspace/slack-smoke/pm-channel-test.txt ...')` — `pm` 봇 채널 테스트 메시지 전송 성공 (`ts=1775117192.306329`).
  - `docker exec partner-subak ... conversations.replies(channel=C0ARA2ZF3BJ, ts=1775117192.306329)` — `dev` 스레드 응답 1건 확인.
  - `docker exec partner-dev node -e "... /workspace/slack-smoke/pm-channel-test.txt ..."` — smoke test 파일 생성 및 내용 확인.
  - `docker logs --since 5m partner-dev | grep -iE 'Mentioned by|front-|task-|slack-app-mention|Queued user request'` — `Mentioned by 수박 ...`, queue/task completion 로그 확인.
  - `docker exec partner-subak ... chat.postMessage(channel=C0ARA2ZF3BJ, text='<@U0ALGECA883> https://github.com/paperclipai/paperclip ...')` — 장기 clone regression 요청 전송 성공 (`ts=1775117367.310959`).
  - `docker exec partner-subak ... conversations.replies(channel=C0ARA2ZF3BJ, ts=1775117367.310959)` — progress flood 없이 최종 응답 1건 확인.
  - `docker exec partner-dev node -e "... /workspace/slack-smoke/paperclip-regression ..."` — clone 결과 디렉터리 생성 확인.
  - `docker logs --since 12m partner-dev | grep -iE 'front-2-1775117370122|task-front-2-1775117370122-1|Completed task'` — task 시작/완료 시각 확인 (`08:09:40` -> `08:10:41`).
  - `pnpm exec tsc --noEmit && pnpm exec vitest run src/partner/front/reviewer.test.ts src/partner/slack/progress.test.ts` — 8 passed, 0 failed.
  - Pending: `sh deploy/container-entrypoint.sh` build lock recovery 동작 확인.
  - Pending: 전체 partner 대상 startup 검증.
  - `grep -RIn "COPILOT_AGENT_IDLE_TIMEOUT_MS" ...` — source 기본값/템플릿/live dev `.env` 모두 기존 180000 확인.

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