const fs = require('fs').promises;
const fsSync = require('fs');
const path = require('path');

function createSpecWriter(specsDir) {

  /**
   * Create a new spec under a project.
   * @param {string} projectKey - Project folder name
   * @param {string} specId - Spec ID (e.g. F-010)
   * @param {object} data - { title, type, description, status, conditions, tests, relatedFiles }
   * @param {string} [specMd] - Optional spec.md content
   */
  async function createSpec(projectKey, specId, data, specMd) {
    const projDir = path.join(specsDir, projectKey);
    // Ensure project dir exists
    await fs.mkdir(projDir, { recursive: true });

    const specDir = path.join(projDir, specId);
    if (fsSync.existsSync(path.join(specDir, 'meta.json'))) {
      throw new Error(`Spec already exists: ${projectKey}/${specId}`);
    }

    await fs.mkdir(specDir, { recursive: true });

    const meta = {
      title: data.title || specId,
      type: data.type || '기능',
      status: data.status || '초안',
      attemptCount: 0,
      updatedAt: new Date().toISOString(),
      conditions: data.conditions || [],
      tests: data.tests || [],
      relatedFiles: data.relatedFiles || [],
    };
    await fs.writeFile(path.join(specDir, 'meta.json'), JSON.stringify(meta, null, 2), 'utf-8');

    const md = specMd || `# ${specId}: ${meta.title}\n\n${data.description || ''}\n`;
    await fs.writeFile(path.join(specDir, 'spec.md'), md, 'utf-8');

    // Create subdirectories
    for (const sub of ['tests/unit', 'tests/e2e', 'tests/user', 'evidence/unit', 'evidence/e2e', 'evidence/user', 'artifacts']) {
      await fs.mkdir(path.join(specDir, sub), { recursive: true });
    }

    return { projectKey, specId, meta };
  }

  /**
   * Get the next available spec ID for a project.
   * Scans existing spec folders and returns the next F-NNN or T-NNN.
   */
  async function nextSpecId(projectKey, prefix = 'F') {
    const projDir = path.join(specsDir, projectKey);
    let maxNum = 0;
    try {
      const entries = await fs.readdir(projDir, { withFileTypes: true });
      for (const entry of entries) {
        if (!entry.isDirectory()) continue;
        const match = entry.name.match(new RegExp(`^${prefix}-(\\d+)$`));
        if (match) {
          const num = parseInt(match[1], 10);
          if (num > maxNum) maxNum = num;
        }
      }
    } catch { /* dir may not exist */ }
    return `${prefix}-${String(maxNum + 1).padStart(3, '0')}`;
  }

  function findSpecPath(specId) {
    try {
      const projects = fsSync.readdirSync(specsDir, { withFileTypes: true });
      for (const proj of projects) {
        if (!proj.isDirectory()) continue;
        const specPath = path.join(specsDir, proj.name, specId);
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
      content += `\n## [${questionId}] \`응답 완료\`\n\n> 답변: ${answer}\n`;
    }

    await fs.writeFile(qPath, content, 'utf-8');
  }

  async function submitTestResult(specId, testId, result, comment) {
    const specPath = findSpecPath(specId);
    if (!specPath) throw new Error(`Spec not found: ${specId}`);

    const meta = await readMeta(specPath);
    const test = (meta.tests || []).find(t => t.id === testId);
    if (test) {
      test.lastResult = result;
      test.lastResultAt = new Date().toISOString();
      if (comment) test.lastComment = comment;
    }
    await writeMeta(specPath, meta);

    const evidenceDir = path.join(specPath, 'evidence', 'user');
    await fs.mkdir(evidenceDir, { recursive: true });
    const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
    const evidenceFile = path.join(evidenceDir, `${timestamp}-${testId}.json`);
    await fs.writeFile(evidenceFile, JSON.stringify({
      testId, result, comment: comment || null, timestamp: new Date().toISOString(),
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
    createSpec, nextSpecId,
    updateStatus, resetAttemptCount, answerQuestion, submitTestResult,
    incrementAttemptCount, setLastError, setRetryAt, updateTests, updateConditions,
  };
}

module.exports = { createSpecWriter };
