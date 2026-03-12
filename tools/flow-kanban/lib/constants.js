// Internal constants (English, encoding-safe) → Display labels (Korean)

const STATUS = {
  DRAFT: 'draft',
  QUEUED: 'queued',
  WORKING: 'working',
  TESTING: 'testing',
  REVIEW: 'review',
  INSPECT: 'inspect',
  ACTIVE: 'active',
  DONE: 'done',
};

const STATUS_LIST = [
  STATUS.DRAFT, STATUS.QUEUED, STATUS.WORKING, STATUS.TESTING,
  STATUS.REVIEW, STATUS.INSPECT, STATUS.ACTIVE, STATUS.DONE,
];

const STATUS_LABEL = {
  [STATUS.DRAFT]: '초안',
  [STATUS.QUEUED]: '대기',
  [STATUS.WORKING]: '작업',
  [STATUS.TESTING]: '테스트 검증',
  [STATUS.REVIEW]: '리뷰',
  [STATUS.INSPECT]: '검토',
  [STATUS.ACTIVE]: '활성',
  [STATUS.DONE]: '완료',
};

const TYPE = {
  FEATURE: 'feature',
  TASK: 'task',
};

const TYPE_LABEL = {
  [TYPE.FEATURE]: '기능',
  [TYPE.TASK]: '태스크',
};

const CONDITION_STATUS = {
  DRAFT: 'draft',
  NEEDS_REVIEW: 'needs-review',
  VERIFIED: 'verified',
};

const CONDITION_STATUS_LABEL = {
  [CONDITION_STATUS.DRAFT]: '초안',
  [CONDITION_STATUS.NEEDS_REVIEW]: '검토 필요',
  [CONDITION_STATUS.VERIFIED]: '검증 완료',
};

const QUESTION_STATUS = {
  PENDING: 'pending',
  ANSWERED: 'answered',
};

const QUESTION_STATUS_LABEL = {
  [QUESTION_STATUS.PENDING]: '응답 대기',
  [QUESTION_STATUS.ANSWERED]: '응답 완료',
};

const TEST_TYPE = {
  UNIT: 'unit',
  E2E: 'e2e',
  USER: 'user',
};

const TEST_TYPE_LABEL = {
  [TEST_TYPE.UNIT]: '단위 테스트',
  [TEST_TYPE.E2E]: 'E2E 테스트',
  [TEST_TYPE.USER]: '사용자 테스트',
};

// Reverse maps: Korean label → English key (for migrating legacy data)
function buildReverse(map) {
  const rev = {};
  for (const [k, v] of Object.entries(map)) {
    rev[v] = k;
  }
  return rev;
}

const LABEL_TO_STATUS = buildReverse(STATUS_LABEL);
const LABEL_TO_TYPE = buildReverse(TYPE_LABEL);
const LABEL_TO_CONDITION_STATUS = buildReverse(CONDITION_STATUS_LABEL);
const LABEL_TO_QUESTION_STATUS = buildReverse(QUESTION_STATUS_LABEL);
const LABEL_TO_TEST_TYPE = buildReverse(TEST_TYPE_LABEL);

// Normalize a value: if it's already English key, pass through; if Korean label, convert
// If validKeys is provided, return null for unrecognized values
function normalize(value, labelToKey, validKeys) {
  if (!value) return value;
  if (labelToKey[value]) return labelToKey[value];
  // Check if it's already a valid English key
  if (validKeys && !validKeys.includes(value)) return null;
  return value;
}

module.exports = {
  STATUS, STATUS_LIST, STATUS_LABEL,
  TYPE, TYPE_LABEL,
  CONDITION_STATUS, CONDITION_STATUS_LABEL,
  QUESTION_STATUS, QUESTION_STATUS_LABEL,
  TEST_TYPE, TEST_TYPE_LABEL,
  LABEL_TO_STATUS, LABEL_TO_TYPE,
  LABEL_TO_CONDITION_STATUS, LABEL_TO_QUESTION_STATUS, LABEL_TO_TEST_TYPE,
  normalize,
};
