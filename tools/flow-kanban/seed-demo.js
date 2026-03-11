/**
 * Seed demo spec data for testing the kanban board.
 * Run: npm run seed
 */
const fs = require('fs');
const path = require('path');
const os = require('os');

const SPECS_DIR = process.env.FLOW_SPECS_DIR || path.join(os.homedir(), '.flow', 'specs');

const demoSpecs = [
  {
    group: 'core',
    id: 'F-001',
    meta: {
      title: '사용자 인증 시스템',
      type: '기능',
      status: '활성',
      attemptCount: 2,
      updatedAt: '2026-03-11T10:30:00Z',
      conditions: [
        { id: 'C-001', description: '이메일/비밀번호 로그인 가능', status: '검증 완료' },
        { id: 'C-002', description: 'JWT 토큰 발급 및 검증', status: '검증 완료' },
        { id: 'C-003', description: '비밀번호 해싱 (bcrypt)', status: '검증 완료' },
      ],
      tests: [
        { id: 'T-001', type: '단위 테스트', conditionId: 'C-001', description: '로그인 성공/실패 케이스', lastResult: 'pass' },
        { id: 'T-002', type: '단위 테스트', conditionId: 'C-002', description: 'JWT 생성 및 만료 검증', lastResult: 'pass' },
        { id: 'T-003', type: 'E2E 테스트', conditionId: 'C-001', description: '로그인 페이지 E2E', lastResult: 'pass' },
      ],
      relatedFiles: ['src/auth/login.ts', 'src/auth/jwt.ts', 'tests/auth.test.ts'],
    },
    specMd: `# F-001: 사용자 인증 시스템

## 배경과 목적
사용자가 이메일과 비밀번호로 로그인할 수 있어야 한다.

## 범위
- 로그인 API
- JWT 토큰 발급
- 비밀번호 해싱

## 제외 범위
- 소셜 로그인
- 2FA
`,
    questions: '',
    activity: `---
- **시각**: 2026-03-10T08:00:00Z
- **역할**: runner
- **요약**: 구현 시작
- **상태 변경**: 대기 → 작업
- **결과**: handoff

---
- **시각**: 2026-03-11T10:30:00Z
- **역할**: reviewer
- **요약**: 모든 테스트 통과, 활성으로 전환
- **상태 변경**: 리뷰 → 활성
- **결과**: verified
`,
  },
  {
    group: 'core',
    id: 'F-002',
    meta: {
      title: '사용자 프로필 관리',
      type: '기능',
      status: '작업',
      attemptCount: 1,
      updatedAt: '2026-03-12T02:15:00Z',
      conditions: [
        { id: 'C-001', description: '프로필 조회 API', status: '초안' },
        { id: 'C-002', description: '프로필 수정 API', status: '초안' },
        { id: 'C-003', description: '프로필 이미지 업로드', status: '초안' },
      ],
      tests: [
        { id: 'T-001', type: '단위 테스트', conditionId: 'C-001', description: '프로필 조회 테스트', lastResult: null },
        { id: 'T-002', type: '사용자 테스트', conditionId: 'C-003', description: '이미지 업로드 확인', lastResult: null },
      ],
      relatedFiles: ['src/profile/profile.ts'],
    },
    specMd: `# F-002: 사용자 프로필 관리

## 배경과 목적
사용자가 자신의 프로필을 조회하고 수정할 수 있어야 한다.

## 범위
- 프로필 조회/수정 API
- 프로필 이미지 업로드
`,
    questions: `## [Q-001] \`응답 대기\`

프로필 이미지 최대 크기를 몇 MB로 제한할까요?
`,
    activity: `---
- **시각**: 2026-03-12T02:15:00Z
- **역할**: runner
- **요약**: 구현 시작
- **상태 변경**: 대기 → 작업
- **결과**: handoff
`,
  },
  {
    group: 'core',
    id: 'F-003',
    meta: {
      title: '알림 시스템',
      type: '기능',
      status: '대기',
      attemptCount: 0,
      updatedAt: '2026-03-10T14:00:00Z',
      conditions: [
        { id: 'C-001', description: '실시간 알림 WebSocket', status: '초안' },
        { id: 'C-002', description: '이메일 알림 발송', status: '초안' },
      ],
      tests: [],
      relatedFiles: [],
    },
    specMd: `# F-003: 알림 시스템

## 배경과 목적
사용자에게 실시간 알림과 이메일 알림을 보낼 수 있어야 한다.
`,
    questions: '',
    activity: '',
  },
  {
    group: 'core',
    id: 'F-004',
    meta: {
      title: '대시보드 위젯',
      type: '기능',
      status: '테스트 검증',
      attemptCount: 1,
      updatedAt: '2026-03-12T01:00:00Z',
      conditions: [
        { id: 'C-001', description: '위젯 렌더링', status: '검증 완료' },
        { id: 'C-002', description: '위젯 드래그 앤 드롭', status: '검토 필요' },
      ],
      tests: [
        { id: 'T-001', type: '단위 테스트', conditionId: 'C-001', description: '위젯 렌더링 테스트', lastResult: 'pass' },
        { id: 'T-002', type: 'E2E 테스트', conditionId: 'C-002', description: 'DnD E2E 테스트', lastResult: 'fail' },
      ],
      lastError: 'E2E 테스트 T-002 실패: 드롭 좌표 계산 오류',
      relatedFiles: ['src/dashboard/widget.tsx', 'src/dashboard/dnd.ts'],
    },
    specMd: `# F-004: 대시보드 위젯

## 배경과 목적
사용자가 대시보드에 위젯을 추가하고 드래그로 배치할 수 있어야 한다.
`,
    questions: '',
    activity: `---
- **시각**: 2026-03-12T01:00:00Z
- **역할**: validator
- **요약**: E2E 테스트 실패 확인, 드롭 좌표 계산 문제
- **결과**: requeue
`,
  },
  {
    group: 'core',
    id: 'F-005',
    meta: {
      title: '검색 기능',
      type: '기능',
      status: '리뷰',
      attemptCount: 1,
      updatedAt: '2026-03-12T03:00:00Z',
      conditions: [
        { id: 'C-001', description: '키워드 검색 API', status: '검증 완료' },
        { id: 'C-002', description: '검색 결과 페이징', status: '검증 완료' },
      ],
      tests: [
        { id: 'T-001', type: '단위 테스트', conditionId: 'C-001', description: '검색 쿼리 테스트', lastResult: 'pass' },
        { id: 'T-002', type: '단위 테스트', conditionId: 'C-002', description: '페이징 테스트', lastResult: 'pass' },
        { id: 'T-003', type: '사용자 테스트', conditionId: 'C-001', description: '검색 결과 정확성 확인', lastResult: null },
      ],
      relatedFiles: ['src/search/search.ts', 'src/search/index.ts'],
    },
    specMd: `# F-005: 검색 기능

## 배경과 목적
사용자가 키워드로 콘텐츠를 검색할 수 있어야 한다.
`,
    questions: '',
    activity: `---
- **시각**: 2026-03-12T03:00:00Z
- **역할**: validator
- **요약**: 단위 테스트 모두 통과, 리뷰 단계로 이동
- **상태 변경**: 테스트 검증 → 리뷰
- **결과**: handoff
`,
  },
  {
    group: 'infra',
    id: 'F-006',
    meta: {
      title: 'CI/CD 파이프라인 구축',
      type: '태스크',
      status: '검토',
      attemptCount: 3,
      updatedAt: '2026-03-11T22:00:00Z',
      conditions: [
        { id: 'C-001', description: 'GitHub Actions 워크플로우', status: '검증 완료' },
        { id: 'C-002', description: '자동 배포 스크립트', status: '검토 필요' },
      ],
      tests: [
        { id: 'T-001', type: '사용자 테스트', conditionId: 'C-002', description: '배포 스크립트 수동 검증', lastResult: null },
      ],
      lastError: '작업 횟수 3회 초과로 검토 전환',
      relatedFiles: ['.github/workflows/deploy.yml', 'scripts/deploy.sh'],
    },
    specMd: `# F-006: CI/CD 파이프라인 구축

## 배경과 목적
코드 푸시 시 자동으로 테스트를 실행하고 배포하는 파이프라인을 구축한다.
`,
    questions: `## [Q-001] \`응답 대기\`

배포 대상 환경은 staging과 production 두 곳인가요?

## [Q-002] \`응답 대기\`

배포 승인 절차가 필요한가요?
`,
    activity: `---
- **시각**: 2026-03-11T22:00:00Z
- **역할**: runner
- **요약**: 작업 횟수 3회 초과, 검토로 전환
- **상태 변경**: 작업 → 검토
- **결과**: needs-review
`,
  },
  {
    group: 'infra',
    id: 'T-001',
    meta: {
      title: '데이터베이스 마이그레이션 스크립트',
      type: '태스크',
      status: '완료',
      attemptCount: 1,
      updatedAt: '2026-03-09T16:00:00Z',
      conditions: [
        { id: 'C-001', description: '마이그레이션 up/down 동작', status: '검증 완료' },
      ],
      tests: [
        { id: 'T-001', type: '단위 테스트', conditionId: 'C-001', description: '마이그레이션 롤백 테스트', lastResult: 'pass' },
      ],
      relatedFiles: ['db/migrations/001_init.sql'],
    },
    specMd: `# T-001: 데이터베이스 마이그레이션 스크립트

## 배경과 목적
데이터베이스 스키마 변경을 버전 관리하고 롤백할 수 있어야 한다.
`,
    questions: '',
    activity: `---
- **시각**: 2026-03-09T16:00:00Z
- **역할**: reviewer
- **요약**: 모든 테스트 통과, 완료
- **상태 변경**: 리뷰 → 완료
- **결과**: done
`,
  },
  {
    group: 'core',
    id: 'F-007',
    meta: {
      title: '다국어 지원 (i18n)',
      type: '기능',
      status: '초안',
      attemptCount: 0,
      updatedAt: '2026-03-08T12:00:00Z',
      conditions: [],
      tests: [],
      relatedFiles: [],
    },
    specMd: `# F-007: 다국어 지원

## 배경과 목적
한국어, 영어, 일본어 UI 지원이 필요하다.

## 범위
- 아직 범위 미정
`,
    questions: `## [Q-001] \`응답 대기\`

초기 지원 언어를 한국어/영어만 할까요, 일본어도 포함할까요?
`,
    activity: '',
  },
  {
    group: 'core',
    id: 'F-008',
    meta: {
      title: '파일 업로드 및 관리',
      type: '기능',
      status: '대기',
      attemptCount: 1,
      updatedAt: '2026-03-11T08:00:00Z',
      lastError: '파일 크기 제한 로직 누락으로 테스트 실패',
      conditions: [
        { id: 'C-001', description: '파일 업로드 API', status: '초안' },
        { id: 'C-002', description: '파일 다운로드 API', status: '초안' },
        { id: 'C-003', description: '파일 크기 제한', status: '초안' },
      ],
      tests: [
        { id: 'T-001', type: '단위 테스트', conditionId: 'C-001', description: '업로드 테스트', lastResult: 'fail' },
      ],
      relatedFiles: ['src/files/upload.ts'],
    },
    specMd: `# F-008: 파일 업로드 및 관리

## 배경과 목적
사용자가 파일을 업로드하고 다운로드할 수 있어야 한다.
`,
    questions: '',
    activity: `---
- **시각**: 2026-03-11T08:00:00Z
- **역할**: reviewer
- **요약**: 파일 크기 제한 로직 누락, 대기로 복귀
- **상태 변경**: 리뷰 → 대기
- **결과**: requeue
`,
  },
];

// Create spec directories and files
for (const spec of demoSpecs) {
  const specDir = path.join(SPECS_DIR, spec.group, spec.id);
  fs.mkdirSync(specDir, { recursive: true });

  // Write meta.json
  fs.writeFileSync(
    path.join(specDir, 'meta.json'),
    JSON.stringify(spec.meta, null, 2),
    'utf-8'
  );

  // Write spec.md
  fs.writeFileSync(path.join(specDir, 'spec.md'), spec.specMd, 'utf-8');

  // Write questions.md
  if (spec.questions) {
    fs.writeFileSync(path.join(specDir, 'questions.md'), spec.questions, 'utf-8');
  }

  // Write activity.log.md
  if (spec.activity) {
    fs.writeFileSync(path.join(specDir, 'activity.log.md'), spec.activity, 'utf-8');
  }

  // Create subdirectories
  for (const sub of ['tests/unit', 'tests/e2e', 'tests/user', 'evidence/unit', 'evidence/e2e', 'evidence/user', 'artifacts', 'worktree']) {
    fs.mkdirSync(path.join(specDir, sub), { recursive: true });
  }

  console.log(`Created: ${spec.group}/${spec.id} (${spec.meta.status})`);
}

console.log(`\nDone! ${demoSpecs.length} specs created in ${SPECS_DIR}`);
