const fs = require('fs').promises;
const fsSync = require('fs');
const path = require('path');

function createActivityLogger(specsDir) {
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

  async function append(specId, entry) {
    const specPath = findSpecPath(specId);
    if (!specPath) return;

    const logPath = path.join(specPath, 'activity.log.md');
    const timestamp = new Date().toISOString();

    let block = `\n---\n`;
    block += `- **시각**: ${timestamp}\n`;
    block += `- **역할**: ${entry.role || 'system'}\n`;
    if (entry.summary) block += `- **요약**: ${entry.summary}\n`;
    if (entry.statusChange) {
      block += `- **상태 변경**: ${entry.statusChange.from} → ${entry.statusChange.to}\n`;
    }
    if (entry.result) block += `- **결과**: ${entry.result}\n`;
    if (entry.detail) block += `- **상세**: ${entry.detail}\n`;
    block += '\n';

    await fs.appendFile(logPath, block, 'utf-8');
  }

  return { append };
}

module.exports = { createActivityLogger };
