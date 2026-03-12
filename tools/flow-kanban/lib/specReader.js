const fs = require('fs').promises;
const fsSync = require('fs');
const path = require('path');
const {
  STATUS, TYPE, CONDITION_STATUS, QUESTION_STATUS, TEST_TYPE,
  normalize,
  LABEL_TO_STATUS, LABEL_TO_TYPE,
  LABEL_TO_CONDITION_STATUS, LABEL_TO_QUESTION_STATUS, LABEL_TO_TEST_TYPE,
} = require('./constants');

/**
 * Spec directory structure:
 *   specsDir/
 *     <project>/              ← 프로젝트 폴더
 *       project.json          ← { name, root, defaultBranch }
 *       <specId>/
 *         meta.json
 *         spec.md
 *         activity.log.md
 *         questions.md
 *         tests/ evidence/ artifacts/
 */
function createSpecReader(specsDir) {

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

  // List all projects
  async function listProjects() {
    const projects = [];
    try {
      const entries = await fs.readdir(specsDir, { withFileTypes: true });
      for (const entry of entries) {
        if (!entry.isDirectory()) continue;
        const projPath = path.join(specsDir, entry.name);
        const projJson = await readJson(path.join(projPath, 'project.json'));
        projects.push({
          key: entry.name,
          name: projJson?.name || entry.name,
          root: projJson?.root || null,
          defaultBranch: projJson?.defaultBranch || 'main',
        });
      }
    } catch { /* specsDir may not exist */ }
    return projects;
  }

  // Find all spec folders across all projects
  async function findSpecDirs() {
    const dirs = [];
    try {
      const projects = await fs.readdir(specsDir, { withFileTypes: true });
      for (const proj of projects) {
        if (!proj.isDirectory()) continue;
        const projPath = path.join(specsDir, proj.name);
        const entries = await fs.readdir(projPath, { withFileTypes: true });
        for (const entry of entries) {
          if (!entry.isDirectory()) continue;
          // Skip if it's not a spec folder (project.json is a file, not a dir)
          dirs.push({
            specId: entry.name,
            project: proj.name,
            path: path.join(projPath, entry.name),
          });
        }
      }
    } catch { /* specsDir may not exist */ }
    return dirs;
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
      const rawStatus = statusMatch ? statusMatch[1] : QUESTION_STATUS.PENDING;
      const status = normalize(rawStatus, LABEL_TO_QUESTION_STATUS) || QUESTION_STATUS.PENDING;
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
    const openQuestionCount = questions.filter(q => q.status === QUESTION_STATUS.PENDING).length;

    const tests = meta.tests || [];
    const testTotal = tests.length;
    const testPass = tests.filter(t => t.lastResult === 'pass').length;

    return {
      id: specDir.specId,
      project: specDir.project,
      title: meta.title || specDir.specId,
      type: normalize(meta.type, LABEL_TO_TYPE, Object.values(TYPE)) || TYPE.FEATURE,
      status: normalize(meta.status, LABEL_TO_STATUS, Object.values(STATUS)) || STATUS.DRAFT,
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
    for (const sub of ['unit', 'e2e', 'user']) {
      try {
        const files = await fs.readdir(path.join(evidenceDir, sub));
        for (const f of files) {
          evidenceFiles.push({ type: sub, name: f, path: `${sub}/${f}` });
        }
      } catch { /* subdir may not exist */ }
    }

    // Read project info
    const projJson = await readJson(path.join(specsDir, dir.project, 'project.json'));

    const conditions = (meta.conditions || []).map(c => ({
      ...c,
      status: normalize(c.status, LABEL_TO_CONDITION_STATUS, Object.values(CONDITION_STATUS)) || CONDITION_STATUS.DRAFT,
    }));

    const tests = (meta.tests || []).map(t => ({
      ...t,
      type: normalize(t.type, LABEL_TO_TEST_TYPE, Object.values(TEST_TYPE)) || t.type,
    }));

    return {
      id: dir.specId,
      project: dir.project,
      projectRoot: projJson?.root || null,
      title: meta.title || dir.specId,
      type: normalize(meta.type, LABEL_TO_TYPE, Object.values(TYPE)) || TYPE.FEATURE,
      status: normalize(meta.status, LABEL_TO_STATUS, Object.values(STATUS)) || STATUS.DRAFT,
      attemptCount: meta.attemptCount || 0,
      conditions,
      tests,
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
    try {
      const projects = fsSync.readdirSync(specsDir, { withFileTypes: true });
      for (const proj of projects) {
        if (!proj.isDirectory()) continue;
        const specPath = path.join(specsDir, proj.name, specId);
        const filePath = path.join(specPath, 'evidence', fileName);
        if (fsSync.existsSync(filePath)) return filePath;
      }
    } catch { /* */ }
    return null;
  }

  /**
   * Read project.json for a given project key.
   */
  async function getProject(projectKey) {
    const projJson = await readJson(path.join(specsDir, projectKey, 'project.json'));
    if (!projJson) return null;
    return {
      key: projectKey,
      name: projJson.name || projectKey,
      root: projJson.root || null,
      defaultBranch: projJson.defaultBranch || 'main',
    };
  }

  return { listSpecs, listProjects, getSpec, getProject, getActivity, getEvidencePath };
}

module.exports = { createSpecReader };
