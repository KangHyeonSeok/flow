// Initialize the app
document.addEventListener('DOMContentLoaded', () => {
  createBoard();
  loadProjects();
  refreshBoard();
  initRunner();

  // Search & project filter
  const searchInput = document.getElementById('search');
  const projectFilter = document.getElementById('project-filter');
  let searchTimeout;
  searchInput.addEventListener('input', () => {
    clearTimeout(searchTimeout);
    searchTimeout = setTimeout(() => renderSpecs(allSpecs), 200);
  });
  projectFilter.addEventListener('change', () => renderSpecs(allSpecs));

  // Demo seed button
  document.getElementById('seed-demo').addEventListener('click', async () => {
    try {
      const result = await API.seedDemo();
      showToast(`데모 추가: ${result.created}개 생성 (${result.total}개 중)`, 'success');
      await loadProjects();
      await refreshBoard();
    } catch (err) {
      showToast(`데모 추가 실패: ${err.message}`, 'error');
    }
  });

  // Add Project modal
  initAddProject();

  // Close detail panel
  document.getElementById('detail-close').addEventListener('click', closeDetail);
  document.getElementById('detail-overlay').addEventListener('click', (e) => {
    if (e.target === e.currentTarget) closeDetail();
  });

  // Escape to close
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') closeDetail();
  });

  // SSE live updates
  let refreshDebounce;
  API.connectSSE(
    () => {
      clearTimeout(refreshDebounce);
      refreshDebounce = setTimeout(() => refreshBoard(), 1000);
    },
    (status) => {
      updateRunnerUI(status);
    }
  );
});

// --- Project Filter ---

async function loadProjects() {
  try {
    const projects = await API.getProjects();
    const select = document.getElementById('project-filter');
    const current = select.value;
    // Keep first option ("전체 프로젝트"), clear the rest
    while (select.options.length > 1) select.remove(1);
    for (const proj of projects) {
      const opt = document.createElement('option');
      opt.value = proj.key;
      opt.textContent = proj.name;
      select.appendChild(opt);
    }
    select.value = current;
  } catch { /* */ }
}

// --- Runner UI ---

async function initRunner() {
  const startBtn = document.getElementById('runner-start');
  const scheduleBtn = document.getElementById('runner-schedule');
  const forceStopBtn = document.getElementById('runner-force-stop');

  startBtn.addEventListener('click', async () => {
    try {
      const status = await API.runnerStart();
      updateRunnerUI(status);
      showToast('Runner started', 'success');
    } catch (err) {
      showToast(`Start failed: ${err.message}`, 'error');
    }
  });

  scheduleBtn.addEventListener('click', async () => {
    try {
      const status = await API.runnerScheduleStop();
      updateRunnerUI(status);
      showToast('Stop scheduled', 'success');
    } catch (err) {
      showToast(`Schedule stop failed: ${err.message}`, 'error');
    }
  });

  forceStopBtn.addEventListener('click', async () => {
    try {
      const status = await API.runnerStop();
      updateRunnerUI(status);
      showToast('Runner force-stopped', 'success');
    } catch (err) {
      showToast(`Stop failed: ${err.message}`, 'error');
    }
  });

  // Load initial status
  try {
    const status = await API.getRunnerStatus();
    updateRunnerUI(status);
  } catch { /* server may not be ready */ }
}

function updateRunnerUI(status) {
  const dot = document.getElementById('runner-dot');
  const label = document.getElementById('runner-label');
  const specEl = document.getElementById('runner-spec');
  const startBtn = document.getElementById('runner-start');
  const scheduleBtn = document.getElementById('runner-schedule');
  const forceStopBtn = document.getElementById('runner-force-stop');

  // State classes
  dot.className = `runner-dot ${status.state}`;
  label.className = `runner-label ${status.state}`;

  // Label text
  const stateLabels = {
    stopped: 'Stopped',
    running: 'Running',
    stopping: 'Stopping...',
  };
  label.textContent = stateLabels[status.state] || status.state;

  // Current spec info
  if (status.currentSpec) {
    specEl.textContent = `${status.currentSpec.id} · ${status.currentStage || ''}`;
    specEl.title = status.currentSpec.title;
  } else {
    specEl.textContent = status.state === 'running' ? 'idle' : '';
    specEl.title = '';
  }

  // Button states
  startBtn.disabled = status.state === 'running';
  scheduleBtn.disabled = status.state !== 'running';
  forceStopBtn.disabled = status.state === 'stopped';
}

// --- Toast ---

// --- Add Project Modal ---

function initAddProject() {
  const btn = document.getElementById('add-project');
  const overlay = document.getElementById('add-project-overlay');
  const closeBtn = document.getElementById('add-project-close');
  const cancelBtn = document.getElementById('add-project-cancel');
  const submitBtn = document.getElementById('add-project-submit');
  const pathInput = document.getElementById('add-project-path');
  const nameInput = document.getElementById('add-project-name');
  const branchInput = document.getElementById('add-project-branch');

  function openModal() {
    pathInput.value = '';
    nameInput.value = '';
    branchInput.value = '';
    overlay.classList.add('open');
    pathInput.focus();
  }

  function closeModal() {
    overlay.classList.remove('open');
  }

  btn.addEventListener('click', openModal);
  closeBtn.addEventListener('click', closeModal);
  cancelBtn.addEventListener('click', closeModal);
  overlay.addEventListener('click', (e) => {
    if (e.target === overlay) closeModal();
  });

  submitBtn.addEventListener('click', async () => {
    const repoPath = pathInput.value.trim();
    if (!repoPath) {
      showToast('경로를 입력하세요.', 'error');
      return;
    }
    submitBtn.disabled = true;
    try {
      const result = await API.addProject(repoPath, nameInput.value.trim(), branchInput.value.trim());
      showToast(`프로젝트 "${result.name}" 추가 완료`, 'success');
      closeModal();
      await loadProjects();
      await refreshBoard();
    } catch (err) {
      showToast(`프로젝트 추가 실패: ${err.message}`, 'error');
    } finally {
      submitBtn.disabled = false;
    }
  });

  // Enter to submit
  for (const input of [pathInput, nameInput, branchInput]) {
    input.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') submitBtn.click();
    });
  }
}

// --- Toast ---

function showToast(message, type = 'success') {
  const container = document.getElementById('toast-container');
  const toast = document.createElement('div');
  toast.className = `toast toast--${type}`;
  toast.textContent = message;
  container.appendChild(toast);
  setTimeout(() => {
    toast.style.opacity = '0';
    toast.style.transition = 'opacity 0.3s';
    setTimeout(() => toast.remove(), 300);
  }, 3000);
}
