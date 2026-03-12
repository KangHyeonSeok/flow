const fs = require('fs');
const path = require('path');
const { STATUS, TYPE, CONDITION_STATUS, TEST_TYPE } = require('./constants');

const projects = [
  {
    key: 'flow',
    config: { name: 'flow', root: 'D:\\Projects\\flow', defaultBranch: 'main' },
  },
  {
    key: 'my-app',
    config: { name: 'my-app', root: 'D:\\Projects\\my-app', defaultBranch: 'main' },
  },
];

const specs = [
  {
    project: 'flow', id: 'F-001',
    meta: {
      title: '사용자 인증 시스템', type: TYPE.FEATURE, status: STATUS.ACTIVE, attemptCount: 2,
      updatedAt: '2026-03-11T10:30:00Z',
      conditions: [
        { id: 'C-001', description: '이메일/비밀번호 로그인 가능', status: CONDITION_STATUS.VERIFIED },
        { id: 'C-002', description: 'JWT 토큰 발급 및 검증', status: CONDITION_STATUS.VERIFIED },
      ],
      tests: [
        { id: 'T-001', type: TEST_TYPE.UNIT, conditionId: 'C-001', description: '로그인 성공/실패 케이스', lastResult: 'pass' },
        { id: 'T-002', type: TEST_TYPE.E2E, conditionId: 'C-001', description: '로그인 페이지 E2E', lastResult: 'pass' },
      ],
      relatedFiles: ['src/auth/login.ts', 'src/auth/jwt.ts'],
    },
    specMd: '# F-001: 사용자 인증 시스템\n\n## 배경과 목적\n사용자가 이메일과 비밀번호로 로그인할 수 있어야 한다.\n',
  },
  {
    project: 'flow', id: 'F-002',
    meta: {
      title: '사용자 프로필 관리', type: TYPE.FEATURE, status: STATUS.WORKING, attemptCount: 1,
      updatedAt: '2026-03-12T02:15:00Z',
      conditions: [
        { id: 'C-001', description: '프로필 조회 API', status: CONDITION_STATUS.DRAFT },
        { id: 'C-002', description: '프로필 이미지 업로드', status: CONDITION_STATUS.DRAFT },
      ],
      tests: [
        { id: 'T-001', type: TEST_TYPE.UNIT, conditionId: 'C-001', description: '프로필 조회 테스트', lastResult: null },
        { id: 'T-002', type: TEST_TYPE.USER, conditionId: 'C-002', description: '이미지 업로드 확인', lastResult: null },
      ],
    },
    specMd: '# F-002: 사용자 프로필 관리\n\n## 배경과 목적\n사용자가 프로필을 조회하고 수정할 수 있어야 한다.\n',
    questions: '## [Q-001] `응답 대기`\n\n프로필 이미지 최대 크기를 몇 MB로 제한할까요?\n',
  },
  {
    project: 'flow', id: 'F-003',
    meta: {
      title: '알림 시스템', type: TYPE.FEATURE, status: STATUS.QUEUED, attemptCount: 0,
      updatedAt: '2026-03-10T14:00:00Z',
      conditions: [{ id: 'C-001', description: '실시간 알림 WebSocket', status: CONDITION_STATUS.DRAFT }],
      tests: [],
    },
    specMd: '# F-003: 알림 시스템\n\n## 배경과 목적\n사용자에게 실시간 알림을 보낼 수 있어야 한다.\n',
  },
  {
    project: 'flow', id: 'F-004',
    meta: {
      title: '대시보드 위젯', type: TYPE.FEATURE, status: STATUS.TESTING, attemptCount: 1,
      updatedAt: '2026-03-12T01:00:00Z',
      conditions: [
        { id: 'C-001', description: '위젯 렌더링', status: CONDITION_STATUS.VERIFIED },
        { id: 'C-002', description: '위젯 드래그 앤 드롭', status: CONDITION_STATUS.NEEDS_REVIEW },
      ],
      tests: [
        { id: 'T-001', type: TEST_TYPE.UNIT, conditionId: 'C-001', description: '위젯 렌더링 테스트', lastResult: 'pass' },
        { id: 'T-002', type: TEST_TYPE.E2E, conditionId: 'C-002', description: 'DnD E2E 테스트', lastResult: 'fail' },
      ],
      lastError: 'E2E 테스트 T-002 실패: 드롭 좌표 계산 오류',
    },
    specMd: '# F-004: 대시보드 위젯\n\n## 배경과 목적\n대시보드에 위젯을 추가하고 배치할 수 있어야 한다.\n',
  },
  {
    project: 'flow', id: 'F-005',
    meta: {
      title: '검색 기능', type: TYPE.FEATURE, status: STATUS.REVIEW, attemptCount: 1,
      updatedAt: '2026-03-12T03:00:00Z',
      conditions: [{ id: 'C-001', description: '키워드 검색 API', status: CONDITION_STATUS.VERIFIED }],
      tests: [
        { id: 'T-001', type: TEST_TYPE.UNIT, conditionId: 'C-001', description: '검색 쿼리 테스트', lastResult: 'pass' },
        { id: 'T-002', type: TEST_TYPE.USER, conditionId: 'C-001', description: '검색 결과 정확성 확인', lastResult: null },
      ],
    },
    specMd: '# F-005: 검색 기능\n\n## 배경과 목적\n키워드로 콘텐츠를 검색할 수 있어야 한다.\n',
  },
  {
    project: 'flow', id: 'F-006',
    meta: {
      title: 'CI/CD 파이프라인', type: TYPE.TASK, status: STATUS.INSPECT, attemptCount: 3,
      updatedAt: '2026-03-11T22:00:00Z',
      conditions: [{ id: 'C-001', description: 'GitHub Actions 워크플로우', status: CONDITION_STATUS.VERIFIED }],
      tests: [{ id: 'T-001', type: TEST_TYPE.USER, conditionId: 'C-001', description: '배포 스크립트 수동 검증', lastResult: null }],
      lastError: '작업 횟수 3회 초과로 검토 전환',
    },
    specMd: '# F-006: CI/CD 파이프라인\n\n## 배경과 목적\n자동 테스트/배포 파이프라인을 구축한다.\n',
    questions: '## [Q-001] `응답 대기`\n\n배포 환경은 staging + production인가요?\n',
  },
  {
    project: 'flow', id: 'T-001',
    meta: {
      title: 'DB 마이그레이션 스크립트', type: TYPE.TASK, status: STATUS.DONE, attemptCount: 1,
      updatedAt: '2026-03-09T16:00:00Z',
      conditions: [{ id: 'C-001', description: '마이그레이션 up/down 동작', status: CONDITION_STATUS.VERIFIED }],
      tests: [{ id: 'T-001', type: TEST_TYPE.UNIT, conditionId: 'C-001', description: '롤백 테스트', lastResult: 'pass' }],
    },
    specMd: '# T-001: DB 마이그레이션 스크립트\n\n## 배경과 목적\nDB 스키마를 버전 관리한다.\n',
  },
  {
    project: 'flow', id: 'F-007',
    meta: {
      title: '다국어 지원 (i18n)', type: TYPE.FEATURE, status: STATUS.DRAFT, attemptCount: 0,
      updatedAt: '2026-03-08T12:00:00Z', conditions: [], tests: [],
    },
    specMd: '# F-007: 다국어 지원\n\n## 배경과 목적\n한국어, 영어 UI 지원.\n',
    questions: '## [Q-001] `응답 대기`\n\n초기 지원 언어를 한국어/영어만 할까요?\n',
  },
  {
    project: 'my-app', id: 'F-001',
    meta: {
      title: '결제 시스템 연동', type: TYPE.FEATURE, status: STATUS.QUEUED, attemptCount: 0,
      updatedAt: '2026-03-11T09:00:00Z',
      conditions: [
        { id: 'C-001', description: 'PG사 API 연동', status: CONDITION_STATUS.DRAFT },
        { id: 'C-002', description: '결제 취소 처리', status: CONDITION_STATUS.DRAFT },
      ],
      tests: [],
    },
    specMd: '# F-001: 결제 시스템 연동\n\n## 배경과 목적\nPG사를 통한 결제/환불 처리.\n',
  },
  {
    project: 'my-app', id: 'F-002',
    meta: {
      title: '주문 관리', type: TYPE.FEATURE, status: STATUS.WORKING, attemptCount: 1,
      updatedAt: '2026-03-12T04:00:00Z',
      conditions: [{ id: 'C-001', description: '주문 생성 API', status: CONDITION_STATUS.DRAFT }],
      tests: [{ id: 'T-001', type: TEST_TYPE.UNIT, conditionId: 'C-001', description: '주문 생성 테스트', lastResult: null }],
    },
    specMd: '# F-002: 주문 관리\n\n## 배경과 목적\n사용자가 주문을 생성하고 관리할 수 있어야 한다.\n',
  },
  {
    project: 'my-app', id: 'T-001',
    meta: {
      title: '초기 DB 스키마 설계', type: TYPE.TASK, status: STATUS.DONE, attemptCount: 1,
      updatedAt: '2026-03-08T10:00:00Z',
      conditions: [{ id: 'C-001', description: 'ERD 작성', status: CONDITION_STATUS.VERIFIED }],
      tests: [],
    },
    specMd: '# T-001: 초기 DB 스키마 설계\n\n## 배경과 목적\n서비스에 필요한 초기 DB 스키마를 설계한다.\n',
  },
];

function seedDemo(specsDir) {
  let count = 0;

  for (const proj of projects) {
    const projDir = path.join(specsDir, proj.key);
    fs.mkdirSync(projDir, { recursive: true });
    const projJsonPath = path.join(projDir, 'project.json');
    if (!fs.existsSync(projJsonPath)) {
      fs.writeFileSync(projJsonPath, JSON.stringify(proj.config, null, 2), 'utf-8');
    }
  }

  for (const spec of specs) {
    const specDir = path.join(specsDir, spec.project, spec.id);
    if (fs.existsSync(path.join(specDir, 'meta.json'))) continue; // skip existing

    fs.mkdirSync(specDir, { recursive: true });
    fs.writeFileSync(path.join(specDir, 'meta.json'), JSON.stringify(spec.meta, null, 2), 'utf-8');
    fs.writeFileSync(path.join(specDir, 'spec.md'), spec.specMd || '', 'utf-8');
    if (spec.questions) fs.writeFileSync(path.join(specDir, 'questions.md'), spec.questions, 'utf-8');
    if (spec.activity) fs.writeFileSync(path.join(specDir, 'activity.log.md'), spec.activity, 'utf-8');

    for (const sub of ['tests/unit', 'tests/e2e', 'tests/user', 'evidence/unit', 'evidence/e2e', 'evidence/user', 'artifacts']) {
      fs.mkdirSync(path.join(specDir, sub), { recursive: true });
    }
    count++;
  }

  return { created: count, total: specs.length, projects: projects.length };
}

module.exports = { seedDemo };
