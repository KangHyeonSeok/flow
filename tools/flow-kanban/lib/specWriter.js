const fs = require('fs').promises;
const fsSync = require('fs');
const path = require('path');

function createSpecWriter(specsDir) {
  function findSpecPath(specId) {
    try {
      const groups = fsSync.readdirSync(specsDir, { withFileTypes: true });
      for (const group of groups) {
        if (!group.isDirectory()) continue;
        const specPath = path.join(specsDir, group.name, specId);
        if (fsSync.existsSync(specPath)) return specPath;
      }
    } catch { /* */ }
    return null;
  }

  async function readMeta(specPath) {
    const metaPath = path.join(specPath, 'meta.json');
    const content = await fs.readFile(metaPath, 'utf-8');
    return JSON.parse(content);
  }

  async function writeMeta(specPath, meta) {
    const metaPath = path.join(specPath, 'meta.json');
    meta.updatedAt = new Date().toISOString();
    await fs.writeFile(metaPath, JSON.stringify(meta, null, 2), 'utf-8');
  }

  async function updateStatus(specId, newStatus) {
    const specPath = findSpecPath(specId);
    if (!specPath) throw new Error(`Spec not found: ${specId}`);
    const meta = await readMeta(specPath);
    meta.status = newStatus;
    await writeMeta(specPath, meta);
  }

  async function resetAttemptCount(specId) {
    const specPath = findSpecPath(specId);
    if (!specPath) throw new Error(`Spec not found: ${specId}`);
    const meta = await readMeta(specPath);
    meta.attemptCount = 0;
    await writeMeta(specPath, meta);
  }

  async function answerQuestion(specId, questionId, answer) {
    const specPath = findSpecPath(specId);
    if (!specPath) throw new Error(`Spec not found: ${specId}`);

    const qPath = path.join(specPath, 'questions.md');
    let content = '';
    try {
      content = await fs.readFile(qPath, 'utf-8');
    } catch { /* file may not exist */ }

    // Find the question block and update it
    const pattern = new RegExp(`(## \\[${questionId}\\][^]*?\`)(응답 대기)(\`[^]*?)(?=## \\[|$)`, 's');
    const match = content.match(pattern);
    if (match) {
      let block = match[0];
      block = block.replace('`응답 대기`', '`응답 완료`');
      if (block.includes('> 답변:')) {
        block = block.replace(/> 답변:.*/, `> 답변: ${answer}`);
      } else {
        block = block.trimEnd() + `\n\n> 답변: ${answer}\n`;
      }
      content = content.replace(match[0], block);
    } else {
      // Append new answer section
      content += `\n## [${questionId}] \`응답 완료\`\n\n> 답변: ${answer}\n`;
    }

    await fs.writeFile(qPath, content, 'utf-8');
  }

  async function submitTestResult(specId, testId, result, comment) {
    const specPath = findSpecPath(specId);
    if (!specPath) throw new Error(`Spec not found: ${specId}`);

    // Update test result in meta.json
    const meta = await readMeta(specPath);
    const test = (meta.tests || []).find(t => t.id === testId);
    if (test) {
      test.lastResult = result;
      test.lastResultAt = new Date().toISOString();
      if (comment) test.lastComment = comment;
    }
    await writeMeta(specPath, meta);

    // Write evidence file
    const evidenceDir = path.join(specPath, 'evidence', 'user');
    await fs.mkdir(evidenceDir, { recursive: true });
    const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
    const evidenceFile = path.join(evidenceDir, `${timestamp}-${testId}.json`);
    await fs.writeFile(evidenceFile, JSON.stringify({
      testId,
      result,
      comment: comment || null,
      timestamp: new Date().toISOString(),
    }, null, 2), 'utf-8');
  }

  async function incrementAttemptCount(specId) {
    const specPath = findSpecPath(specId);
    if (!specPath) throw new Error(`Spec not found: ${specId}`);
    const meta = await readMeta(specPath);
    meta.attemptCount = (meta.attemptCount || 0) + 1;
    await writeMeta(specPath, meta);
  }

  async function setLastError(specId, error) {
    const specPath = findSpecPath(specId);
    if (!specPath) throw new Error(`Spec not found: ${specId}`);
    const meta = await readMeta(specPath);
    meta.lastError = error || null;
    await writeMeta(specPath, meta);
  }

  async function setRetryAt(specId, isoTime) {
    const specPath = findSpecPath(specId);
    if (!specPath) throw new Error(`Spec not found: ${specId}`);
    const meta = await readMeta(specPath);
    meta.retryAt = isoTime || null;
    await writeMeta(specPath, meta);
  }

  async function updateTests(specId, tests) {
    const specPath = findSpecPath(specId);
    if (!specPath) throw new Error(`Spec not found: ${specId}`);
    const meta = await readMeta(specPath);
    meta.tests = tests;
    await writeMeta(specPath, meta);
  }

  async function updateConditions(specId, conditions) {
    const specPath = findSpecPath(specId);
    if (!specPath) throw new Error(`Spec not found: ${specId}`);
    const meta = await readMeta(specPath);
    meta.conditions = conditions;
    await writeMeta(specPath, meta);
  }

  return {
    updateStatus, resetAttemptCount, answerQuestion, submitTestResult,
    incrementAttemptCount, setLastError, setRetryAt, updateTests, updateConditions,
  };
}

module.exports = { createSpecWriter };
