const { execFile } = require('child_process');
const path = require('path');
const fs = require('fs').promises;
const { promisify } = require('util');

const exec = promisify(execFile);

/**
 * Git worktree manager for spec implementation.
 * Each spec gets its own worktree and branch.
 */
function createWorktreeManager(specsDir) {
  const PROJECT_ROOT = process.env.FLOW_PROJECT_ROOT || process.cwd();

  async function ensure(specId) {
    const branch = `spec/${specId}`;
    const wtPath = path.join(specsDir, '..', 'worktrees', specId);

    try {
      await fs.access(wtPath);
      // Worktree exists, sync with default branch
      await git(['fetch', 'origin'], { cwd: wtPath });
      return { path: wtPath, branch };
    } catch {
      // Create new worktree
      try {
        await git(['branch', branch], { cwd: PROJECT_ROOT });
      } catch {
        // Branch may already exist
      }

      await fs.mkdir(path.dirname(wtPath), { recursive: true });
      await git(['worktree', 'add', wtPath, branch], { cwd: PROJECT_ROOT });
      return { path: wtPath, branch };
    }
  }

  async function cleanup(specId) {
    const wtPath = path.join(specsDir, '..', 'worktrees', specId);
    try {
      await git(['worktree', 'remove', wtPath, '--force'], { cwd: PROJECT_ROOT });
    } catch {
      // Already removed
    }
  }

  async function git(args, options = {}) {
    const { stdout } = await exec('git', args, {
      cwd: options.cwd || PROJECT_ROOT,
      encoding: 'utf-8',
    });
    return stdout.trim();
  }

  return { ensure, cleanup };
}

module.exports = { createWorktreeManager };
