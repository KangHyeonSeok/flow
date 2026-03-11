// Initialize the app
document.addEventListener('DOMContentLoaded', () => {
  createBoard();
  refreshBoard();
  initRunner();

  // Search
  const searchInput = document.getElementById('search');
  let searchTimeout;
  searchInput.addEventListener('input', () => {
    clearTimeout(searchTimeout);
    searchTimeout = setTimeout(() => renderSpecs(allSpecs), 200);
  });

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
