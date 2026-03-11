const { spawn } = require('child_process');
const path = require('path');

/**
 * Pipeline stages: develop → validate → review
 *
 * Each stage invokes an external tool (Copilot CLI, etc.)
 * For now, stages have a stub mode that simulates work with a delay.
 * Set RUNNER_MODE=live to use real Copilot CLI.
 */
function createPipeline(specsDir, reader, writer, logger, worktree, events) {
  const mode = process.env.RUNNER_MODE || 'stub';

  /**
   * Stage 1: Developer — implement the spec
   */
  async function develop(specId, spec) {
    if (mode === 'stub') {
      return stubDevelop(specId, spec);
    }
    return liveDevelop(specId, spec);
  }

  async function stubDevelop(specId, spec) {
    events.emit('log', { specId, stage: '작업', message: '(stub) 구현 시뮬레이션 중...' });
    await delay(3000);

    // Simulate: 80% success, 20% failure
    if (Math.random() < 0.2) {
      return { failed: true, reason: '(stub) 구현 중 테스트 실패 시뮬레이션' };
    }

    // Mark tests as pass in stub mode
    const full = await reader.getSpec(specId);
    if (full && full.tests) {
      for (const test of full.tests) {
        if (test.type !== '사용자 테스트') {
          test.lastResult = 'pass';
          test.lastResultAt = new Date().toISOString();
        }
      }
      await writer.updateTests(specId, full.tests);
    }

    events.emit('log', { specId, stage: '작업', message: '(stub) 구현 완료' });
    return { failed: false };
  }

  async function liveDevelop(specId, spec) {
    // TODO: Invoke Copilot CLI in the spec's worktree
    // const wt = await worktree.ensure(specId);
    // const result = await runCopilot(wt.path, spec.specMd, 'develop');
    // return result;
    return { failed: true, reason: 'Live mode not yet implemented' };
  }

  /**
   * Stage 2: Test Validator — check tests match spec
   */
  async function validate(specId, spec) {
    if (mode === 'stub') {
      return stubValidate(specId, spec);
    }
    return liveValidate(specId, spec);
  }

  async function stubValidate(specId, spec) {
    events.emit('log', { specId, stage: '테스트 검증', message: '(stub) 테스트 검증 중...' });
    await delay(2000);

    // 90% pass validation
    if (Math.random() < 0.1) {
      return { requeue: true, reason: '(stub) 테스트가 조건을 충분히 검증하지 않음' };
    }

    events.emit('log', { specId, stage: '테스트 검증', message: '(stub) 테스트 검증 통과' });
    return { requeue: false };
  }

  async function liveValidate(specId, spec) {
    return { requeue: true, reason: 'Live mode not yet implemented' };
  }

  /**
   * Stage 3: Test Reviewer — review test results and evidence
   */
  async function review(specId, spec) {
    if (mode === 'stub') {
      return stubReview(specId, spec);
    }
    return liveReview(specId, spec);
  }

  async function stubReview(specId, spec) {
    events.emit('log', { specId, stage: '리뷰', message: '(stub) 테스트 결과 리뷰 중...' });
    await delay(2000);

    const full = await reader.getSpec(specId);

    // Check if user tests exist and need user action
    const hasUserTests = (full.tests || []).some(t => t.type === '사용자 테스트');
    const hasOpenQuestions = (full.questions || []).some(q => q.status === '응답 대기');

    if (hasUserTests || hasOpenQuestions) {
      return { needsUserReview: true, reason: '사용자 테스트 또는 질문 답변 필요' };
    }

    // Check if any non-user tests failed
    const failedTests = (full.tests || []).filter(t =>
      t.type !== '사용자 테스트' && t.lastResult === 'fail'
    );
    if (failedTests.length > 0) {
      return {
        requeue: true,
        reason: `테스트 실패: ${failedTests.map(t => t.id).join(', ')}`,
      };
    }

    events.emit('log', { specId, stage: '리뷰', message: '(stub) 리뷰 통과' });

    // Update condition statuses
    if (full.conditions) {
      for (const c of full.conditions) {
        c.status = '검증 완료';
      }
      await writer.updateConditions(specId, full.conditions);
    }

    return { needsUserReview: false, requeue: false };
  }

  async function liveReview(specId, spec) {
    return { needsUserReview: true, reason: 'Live mode not yet implemented' };
  }

  return { develop, validate, review };
}

function delay(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

module.exports = { createPipeline };
