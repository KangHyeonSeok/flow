// Initialize the app
document.addEventListener('DOMContentLoaded', () => {
  createBoard();
  refreshBoard();

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
  API.connectSSE(() => {
    clearTimeout(refreshDebounce);
    refreshDebounce = setTimeout(() => refreshBoard(), 1000);
  });
});

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
