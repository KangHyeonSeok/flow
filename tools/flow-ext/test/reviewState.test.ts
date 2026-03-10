import test from 'node:test';
import assert from 'node:assert/strict';
import { getUserFeedbackState } from '../src/reviewState';

test('additionalInformationRequests are read-only and excluded from open question counts', () => {
    const feedback = getUserFeedbackState({
        metadata: {
            questionStatus: 'waiting-user-input',
            reviewDisposition: 'open-question',
            lastAnsweredAt: '2026-03-10T15:08:59.519Z',
            review: {
                additionalInformationRequests: [
                    '통합 테스트 실행을 위한 로컬/CI 재현 가이드',
                    '확정된 UI 목업 또는 기대 화면',
                ],
            },
            questions: [
                {
                    id: 'F-001-Q1',
                    type: 'user-decision',
                    question: '우선 노출 필드는?',
                    status: 'answered',
                    answer: '로컬 시간, role, comment',
                    answeredAt: '2026-03-10T15:08:59.519Z',
                },
            ],
        },
    });

    assert.equal(feedback.requiresUserInput, false);
    assert.equal(feedback.openQuestionCount, 0);
    assert.deepEqual(feedback.openQuestions, []);
    assert.equal(feedback.questions.length, 1);
    assert.deepEqual(feedback.additionalInformationRequests, [
        '통합 테스트 실행을 위한 로컬/CI 재현 가이드',
        '확정된 UI 목업 또는 기대 화면',
    ]);
    assert.equal(feedback.lastAnsweredAt, '2026-03-10T15:08:59.519Z');
});

test('actual open questions still drive user input gating', () => {
    const feedback = getUserFeedbackState({
        metadata: {
            questions: [
                {
                    id: 'F-001-Q2',
                    type: 'missing-info',
                    question: '기대 UI 목업은?',
                    status: 'open',
                },
            ],
            review: {
                additionalInformationRequests: ['별도 참고 자료'],
            },
        },
    });

    assert.equal(feedback.requiresUserInput, true);
    assert.equal(feedback.openQuestionCount, 1);
    assert.equal(feedback.openQuestions[0]?.question, '기대 UI 목업은?');
    assert.deepEqual(feedback.additionalInformationRequests, ['별도 참고 자료']);
});