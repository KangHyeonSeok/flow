const fs = require('fs').promises;
const fsSync = require('fs');
const path = require('path');

function createSpecReader(specsDir) {
  // Find all spec folders: specsDir/<group>/<specId>/
  async function findSpecDirs() {
    const dirs = [];
    try {
      const groups = await fs.readdir(specsDir, { withFileTypes: true });
      for (const group of groups) {
        if (!group.isDirectory()) continue;
        const groupPath = path.join(specsDir, group.name);
        const specs = await fs.readdir(groupPath, { withFileTypes: true });
        for (const spec of specs) {
          if (!spec.isDirectory()) continue;
          dirs.push({
            specId: spec.name,
            group: group.name,
            path: path.join(groupPath, spec.name),
          });
        }
      }
    } catch {
      // specsDir may not exist
    }
    return dirs;
  }

  async function readJson(filePath) {
    try {
      const content = await fs.readFile(filePath, 'utf-8');
      return JSON.parse(content);
    } catch {
      return null;
    }
  }

  async function readText(filePath) {
    try {
      return await fs.readFile(filePath, 'utf-8');
    } catch {
      return null;
    }
  }

  function parseQuestions(text) {
    if (!text) return [];
    const questions = [];
    const blocks = text.split(/^## /m).filter(Boolean);
    for (const block of blocks) {
      const lines = block.trim().split('\n');
      const titleLine = lines[0] || '';
      const idMatch = titleLine.match(/\[([^\]]+)\]/);
      const statusMatch = titleLine.match(/`([^`]+)`/);
      const id = idMatch ? idMatch[1] : `q${questions.length + 1}`;
      const status = statusMatch ? statusMatch[1] : '응답 대기';
      const bodyLines = lines.slice(1);
      const questionText = bodyLines.filter(l => !l.startsWith('> 답변:')).join('\n').trim();
      const answerLine = bodyLines.find(l => l.startsWith('> 답변:'));
      const answer = answerLine ? answerLine.replace('> 답변:', '').trim() : null;
      questions.push({ id, status, question: questionText, answer });
    }
    return questions;
  }

  function parseActivityLog(text) {
    if (!text) return [];
    const entries = [];
    const blocks = text.split(/^---$/m).filter(b => b.trim());
    for (const block of blocks) {
      const entry = {};
      const lines = block.trim().split('\n');
      for (const line of lines) {
        const kv = line.match(/^- \*\*(.+?)\*\*:\s*(.+)/);
        if (kv) {
          const key = kv[1].trim();
          const val = kv[2].trim();
          if (key === '시각') entry.timestamp = val;
          else if (key === '역할') entry.role = val;
          else if (key === '요약') entry.summary = val;
          else if (key === '상태 변경') entry.statusChange = val;
          else if (key === '결과') entry.result = val;
          else if (key === '상세') entry.detail = val;
        }
      }
      if (Object.keys(entry).length > 0) entries.push(entry);
    }
    return entries;
  }

  async function readSpecSummary(specDir) {
    const meta = await readJson(path.join(specDir.path, 'meta.json'));
    if (!meta) return null;

    const questionsText = await readText(path.join(specDir.path, 'questions.md'));
    const questions = parseQuestions(questionsText);
    const openQuestionCount = questions.filter(q => q.status === '응답 대기').length;

    // Count tests from meta
    const tests = meta.tests || [];
    const testTotal = tests.length;
    const testPass = tests.filter(t => t.lastResult === 'pass').length;

    return {
      id: specDir.specId,
      group: specDir.group,
      title: meta.title || specDir.specId,
      type: meta.type || '기능',
      status: meta.status || '초안',
      attemptCount: meta.attemptCount || 0,
      openQuestionCount,
      testSummary: { total: testTotal, pass: testPass },
      updatedAt: meta.updatedAt || null,
      lastFailReason: meta.lastError || null,
      retryAt: meta.retryAt || null,
    };
  }

  async function listSpecs() {
    const dirs = await findSpecDirs();
    const summaries = [];
    for (const dir of dirs) {
      const summary = await readSpecSummary(dir);
      if (summary) summaries.push(summary);
    }
    return summaries;
  }

  async function getSpec(specId) {
    const dirs = await findSpecDirs();
    const dir = dirs.find(d => d.specId === specId);
    if (!dir) return null;

    const meta = await readJson(path.join(dir.path, 'meta.json'));
    if (!meta) return null;

    const specMd = await readText(path.join(dir.path, 'spec.md'));
    const questionsText = await readText(path.join(dir.path, 'questions.md'));
    const activityText = await readText(path.join(dir.path, 'activity.log.md'));

    const questions = parseQuestions(questionsText);
    const activity = parseActivityLog(activityText);

    // List evidence files
    const evidenceFiles = [];
    const evidenceDir = path.join(dir.path, 'evidence');
    try {
      const subdirs = ['unit', 'e2e', 'user'];
      for (const sub of subdirs) {
        const subPath = path.join(evidenceDir, sub);
        try {
          const files = await fs.readdir(subPath);
          for (const f of files) {
            evidenceFiles.push({ type: sub, name: f, path: `${sub}/${f}` });
          }
        } catch { /* subdir may not exist */ }
      }
    } catch { /* evidence dir may not exist */ }

    return {
      id: dir.specId,
      group: dir.group,
      title: meta.title || dir.specId,
      type: meta.type || '기능',
      status: meta.status || '초안',
      attemptCount: meta.attemptCount || 0,
      conditions: meta.conditions || [],
      tests: meta.tests || [],
      questions,
      activity,
      evidenceFiles,
      specMd: specMd || '',
      updatedAt: meta.updatedAt || null,
      lastFailReason: meta.lastError || null,
      retryAt: meta.retryAt || null,
      relatedFiles: meta.relatedFiles || [],
    };
  }

  async function getActivity(specId) {
    const dirs = await findSpecDirs();
    const dir = dirs.find(d => d.specId === specId);
    if (!dir) return [];
    const text = await readText(path.join(dir.path, 'activity.log.md'));
    return parseActivityLog(text);
  }

  function getEvidencePath(specId, fileName) {
    // Walk groups to find specId
    try {
      const groups = fsSync.readdirSync(specsDir, { withFileTypes: true });
      for (const group of groups) {
        if (!group.isDirectory()) continue;
        const specPath = path.join(specsDir, group.name, specId);
        const filePath = path.join(specPath, 'evidence', fileName);
        if (fsSync.existsSync(filePath)) return filePath;
      }
    } catch { /* */ }
    return null;
  }

  return { listSpecs, getSpec, getActivity, getEvidencePath };
}

module.exports = { createSpecReader };
