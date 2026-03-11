const { execFile } = require('child_process');
const path = require('path');
const fs = require('fs').promises;
const fsSync = require('fs');
const { promisify } = require('util');

const exec = promisify(execFile);

/**
 * Git worktree manager — per-project isolation.
 *
 * Directory layout:
 *   ~/.flow/specs/<project>/project.json   → { root, defaultBranch }
 *   ~/.flow/worktrees/<project>/<specId>/  → git worktree
 *
 * project.json의 root 필드에서 프로젝트 소스 경로를 읽는다.
 * 워크트리의 폴더명은 원본과 다를 수 있으므로,
 * 스펙 경로는 항상 절대경로로 전달한다.
 */
function createWorktreeManager(specsDir, reader) {
  const WORKTREES_DIR = process.env.FLOW_WORKTREES_DIR || path.join(require('os').homedir(), '.flow', 'worktrees');

  /**
   * specId가 속한 프로젝트 키와 스펙 절대경로를 찾는다.
   */
  function findSpecInfo(specId) {
    try {
      const projects = fsSync.readdirSync(specsDir, { withFileTypes: true });
      for (const proj of projects) {
        if (!proj.isDirectory()) continue;
        const candidate = path.join(specsDir, proj.name, specId);
        if (fsSync.existsSync(candidate)) {
          return { projectKey: proj.name, specPath: candidate };
        }
      }
    } catch { /* */ }
    return null;
  }

  /**
   * project.json에서 프로젝트 루트 경로를 읽는다.
   */
  function readProjectRoot(projectKey) {
    try {
      const projPath = path.join(specsDir, projectKey, 'project.json');
      const content = fsSync.readFileSync(projPath, 'utf-8');
      const proj = JSON.parse(content);
      return {
        root: proj.root || null,
        defaultBranch: proj.defaultBranch || 'main',
      };
    } catch {
      return { root: null, defaultBranch: 'main' };
    }
  }

  /**
   * 워크트리를 생성 또는 재사용한다.
   * 반환: { worktreePath, branch, specPath, projectRoot }
   */
  async function ensure(specId) {
    const info = findSpecInfo(specId);
    if (!info) {
      return { worktreePath: null, branch: null, specPath: null, projectRoot: null };
    }

    const { projectKey, specPath } = info;
    const proj = readProjectRoot(projectKey);

    if (!proj.root) {
      return { worktreePath: null, branch: null, specPath, projectRoot: null };
    }

    const branch = `spec/${specId}`;
    const wtPath = path.join(WORKTREES_DIR, projectKey, specId);

    try {
      await fs.access(wtPath);
      // Worktree exists
      try {
        await git(['fetch', 'origin'], { cwd: wtPath });
      } catch { /* offline */ }
    } catch {
      // Create new worktree from project root
      try {
        await git(['branch', branch], { cwd: proj.root });
      } catch { /* branch exists */ }

      await fs.mkdir(path.join(WORKTREES_DIR, projectKey), { recursive: true });
      await git(['worktree', 'add', wtPath, branch], { cwd: proj.root });
    }

    return {
      worktreePath: wtPath,     // 코드 작업 경로
      branch,
      specPath,                 // 스펙 파일 절대경로
      projectRoot: proj.root,   // 원본 프로젝트 경로
    };
  }

  async function cleanup(specId) {
    const info = findSpecInfo(specId);
    if (!info) return;

    const proj = readProjectRoot(info.projectKey);
    if (!proj.root) return;

    const wtPath = path.join(WORKTREES_DIR, info.projectKey, specId);
    try {
      await git(['worktree', 'remove', wtPath, '--force'], { cwd: proj.root });
    } catch { /* already removed */ }
  }

  async function git(args, options = {}) {
    const { stdout } = await exec('git', args, {
      cwd: options.cwd,
      encoding: 'utf-8',
    });
    return stdout.trim();
  }

  return { ensure, cleanup, findSpecInfo };
}

module.exports = { createWorktreeManager };
