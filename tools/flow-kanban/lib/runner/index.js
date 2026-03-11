const { createPipeline } = require('./pipeline');
const { createWorktreeManager } = require('./worktree');
const EventEmitter = require('events');

/**
 * Runner states:
 *   stopped  — not running
 *   running  — processing specs
 *   stopping — finish current spec, then stop
 */
function createRunner(specsDir, reader, writer, logger) {
  const events = new EventEmitter();
  const worktree = createWorktreeManager(specsDir);
  const pipeline = createPipeline(specsDir, reader, writer, logger, worktree, events);

  let state = 'stopped';      // stopped | running | stopping
  let currentSpec = null;      // spec being processed
  let currentStage = null;     // 작업 | 테스트 검증 | 리뷰
  let loopTimer = null;
  let cycleCount = 0;

  const POLL_INTERVAL_MS = parseInt(process.env.RUNNER_POLL_MS || '10000', 10);

  function getStatus() {
    return {
      state,
      currentSpec: currentSpec ? { id: currentSpec.id, title: currentSpec.title } : null,
      currentStage,
      cycleCount,
      pollIntervalMs: POLL_INTERVAL_MS,
    };
  }

  async function start() {
    if (state === 'running') return getStatus();
    if (state === 'stopping') {
      // Cancel scheduled stop, resume running
      state = 'running';
      events.emit('state', getStatus());
      return getStatus();
    }

    state = 'running';
    cycleCount = 0;
    events.emit('state', getStatus());
    log('Runner started');
    scheduleNext(0);
    return getStatus();
  }

  function stop() {
    if (state === 'stopped') return getStatus();

    if (!currentSpec) {
      // Not working on anything, stop immediately
      doStop();
    } else {
      // Force stop — abort current work
      state = 'stopped';
      currentSpec = null;
      currentStage = null;
      clearTimeout(loopTimer);
      loopTimer = null;
      events.emit('state', getStatus());
      log('Runner force-stopped');
    }
    return getStatus();
  }

  function scheduleStop() {
    if (state !== 'running') return getStatus();

    state = 'stopping';
    events.emit('state', getStatus());
    log('Runner stop scheduled — will stop after current spec completes');

    // If idle (no current spec), stop now
    if (!currentSpec) {
      doStop();
    }
    return getStatus();
  }

  function doStop() {
    state = 'stopped';
    currentSpec = null;
    currentStage = null;
    clearTimeout(loopTimer);
    loopTimer = null;
    events.emit('state', getStatus());
    log('Runner stopped');
  }

  function scheduleNext(delayMs) {
    clearTimeout(loopTimer);
    if (state === 'stopped') return;
    loopTimer = setTimeout(() => runCycle(), delayMs);
  }

  async function runCycle() {
    if (state === 'stopped') return;

    // If stopping and no current work, stop now
    if (state === 'stopping' && !currentSpec) {
      doStop();
      return;
    }

    try {
      cycleCount++;
      events.emit('state', getStatus());

      // Find next spec to work on
      const specs = await reader.listSpecs();
      const target = specs.find(s => s.status === '대기' && !isOnCooldown(s));

      if (!target) {
        currentSpec = null;
        currentStage = null;
        events.emit('state', getStatus());

        if (state === 'stopping') {
          doStop();
          return;
        }

        scheduleNext(POLL_INTERVAL_MS);
        return;
      }

      // Process the spec through the pipeline
      currentSpec = target;
      events.emit('state', getStatus());

      await processSpec(target);

      currentSpec = null;
      currentStage = null;
      events.emit('state', getStatus());

      // Check if we should stop
      if (state === 'stopping') {
        doStop();
        return;
      }

      // Continue to next cycle immediately
      scheduleNext(1000);
    } catch (err) {
      log(`Cycle error: ${err.message}`);
      currentSpec = null;
      currentStage = null;
      events.emit('state', getStatus());
      scheduleNext(POLL_INTERVAL_MS);
    }
  }

  async function processSpec(spec) {
    const fullSpec = await reader.getSpec(spec.id);
    if (!fullSpec) return;

    // Stage 1: 작업 (Developer)
    currentStage = '작업';
    events.emit('state', getStatus());
    await writer.updateStatus(spec.id, '작업');
    await writer.incrementAttemptCount(spec.id);
    await logger.append(spec.id, {
      role: 'runner',
      summary: '구현 단계 시작',
      statusChange: { from: '대기', to: '작업' },
      result: 'handoff',
    });

    const devResult = await pipeline.develop(spec.id, fullSpec);
    if (devResult.failed) {
      await handleFailure(spec, devResult);
      return;
    }

    // Check attempt count limit
    const updatedSpec = await reader.getSpec(spec.id);
    if (updatedSpec && updatedSpec.attemptCount >= 3) {
      await writer.updateStatus(spec.id, '검토');
      await logger.append(spec.id, {
        role: 'runner',
        summary: '작업 횟수 3회 초과, 검토로 전환',
        statusChange: { from: '작업', to: '검토' },
        result: 'needs-review',
      });
      return;
    }

    // Stage 2: 테스트 검증 (Test Validator)
    currentStage = '테스트 검증';
    events.emit('state', getStatus());
    await writer.updateStatus(spec.id, '테스트 검증');
    await logger.append(spec.id, {
      role: 'runner',
      summary: '테스트 검증 단계 시작',
      statusChange: { from: '작업', to: '테스트 검증' },
      result: 'handoff',
    });

    const valResult = await pipeline.validate(spec.id, fullSpec);
    if (valResult.requeue) {
      await writer.updateStatus(spec.id, '대기');
      await logger.append(spec.id, {
        role: 'validator',
        summary: `테스트 부적절: ${valResult.reason}`,
        statusChange: { from: '테스트 검증', to: '대기' },
        result: 'requeue',
      });
      return;
    }

    // Stage 3: 리뷰 (Test Reviewer)
    currentStage = '리뷰';
    events.emit('state', getStatus());
    await writer.updateStatus(spec.id, '리뷰');
    await logger.append(spec.id, {
      role: 'runner',
      summary: '리뷰 단계 시작',
      statusChange: { from: '테스트 검증', to: '리뷰' },
      result: 'handoff',
    });

    const reviewResult = await pipeline.review(spec.id, fullSpec);

    if (reviewResult.needsUserReview) {
      await writer.updateStatus(spec.id, '검토');
      await logger.append(spec.id, {
        role: 'reviewer',
        summary: reviewResult.reason || '사용자 확인 필요',
        statusChange: { from: '리뷰', to: '검토' },
        result: 'needs-review',
      });
    } else if (reviewResult.requeue) {
      await writer.updateStatus(spec.id, '대기');
      await writer.setLastError(spec.id, reviewResult.reason);
      await logger.append(spec.id, {
        role: 'reviewer',
        summary: `테스트 실패: ${reviewResult.reason}`,
        statusChange: { from: '리뷰', to: '대기' },
        result: 'requeue',
      });
    } else {
      // Success — set final status based on type
      const finalStatus = fullSpec.type === '태스크' ? '완료' : '활성';
      await writer.updateStatus(spec.id, finalStatus);
      await logger.append(spec.id, {
        role: 'reviewer',
        summary: `모든 테스트 통과, ${finalStatus}으로 전환`,
        statusChange: { from: '리뷰', to: finalStatus },
        result: finalStatus === '완료' ? 'done' : 'verified',
      });
    }
  }

  async function handleFailure(spec, result) {
    await writer.updateStatus(spec.id, '대기');
    await writer.setLastError(spec.id, result.reason);
    if (result.retryAfterMs) {
      await writer.setRetryAt(spec.id, new Date(Date.now() + result.retryAfterMs).toISOString());
    }
    await logger.append(spec.id, {
      role: 'developer',
      summary: `구현 실패: ${result.reason}`,
      statusChange: { from: '작업', to: '대기' },
      result: 'requeue',
    });
  }

  function isOnCooldown(spec) {
    if (!spec.retryAt) return false;
    return new Date(spec.retryAt) > new Date();
  }

  function log(msg) {
    const ts = new Date().toISOString();
    console.log(`[runner ${ts}] ${msg}`);
  }

  return { getStatus, start, stop, scheduleStop, events };
}

module.exports = { createRunner };
