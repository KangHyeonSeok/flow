// Internal constants (English) → Display labels (Korean)
// Mirror of lib/constants.js for frontend use

const STATUS_LABEL = {
  draft: '초안',
  queued: '대기',
  working: '작업',
  testing: '테스트 검증',
  review: '리뷰',
  inspect: '검토',
  active: '활성',
  done: '완료',
};

const STATUS_LIST = ['draft', 'queued', 'working', 'testing', 'review', 'inspect', 'active', 'done'];

const TYPE_LABEL = {
  feature: '기능',
  task: '태스크',
};

const CONDITION_STATUS_LABEL = {
  draft: '초안',
  'needs-review': '검토 필요',
  verified: '검증 완료',
};

const QUESTION_STATUS_LABEL = {
  pending: '응답 대기',
  answered: '응답 완료',
};

const TEST_TYPE_LABEL = {
  unit: '단위 테스트',
  e2e: 'E2E 테스트',
  user: '사용자 테스트',
};

function statusLabel(key) { return STATUS_LABEL[key] || key; }
function typeLabel(key) { return TYPE_LABEL[key] || key; }
function conditionStatusLabel(key) { return CONDITION_STATUS_LABEL[key] || key; }
function questionStatusLabel(key) { return QUESTION_STATUS_LABEL[key] || key; }
function testTypeLabel(key) { return TEST_TYPE_LABEL[key] || key; }
