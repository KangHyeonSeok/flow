const COLUMNS = ['초안', '대기', '작업', '테스트 검증', '리뷰', '검토', '활성', '완료'];

let allSpecs = [];

function createBoard() {
  const board = document.getElementById('board');
  board.innerHTML = '';

  for (const status of COLUMNS) {
    const col = document.createElement('div');
    col.className = 'column';
    col.dataset.status = status;

    col.innerHTML = `
      <div class="column__header">
        <span>${status}</span>
        <span class="column__count">0</span>
      </div>
      <div class="column__body"></div>
    `;

    const body = col.querySelector('.column__body');
    setupColumnDrop(body, status);

    board.appendChild(col);
  }
}

async function refreshBoard() {
  try {
    allSpecs = await API.getSpecs();
  } catch (err) {
    showToast(`스펙 로드 실패: ${err.message}`, 'error');
    allSpecs = [];
  }

  renderSpecs(allSpecs);
}

function renderSpecs(specs) {
  // Clear all columns
  document.querySelectorAll('.column__body').forEach(body => {
    body.innerHTML = '';
  });

  const searchTerm = (document.getElementById('search').value || '').toLowerCase();
  const projectFilter = document.getElementById('project-filter').value;

  let filtered = specs;
  if (projectFilter) {
    filtered = filtered.filter(s => s.project === projectFilter);
  }
  if (searchTerm) {
    filtered = filtered.filter(s =>
      s.id.toLowerCase().includes(searchTerm) ||
      s.title.toLowerCase().includes(searchTerm)
    );
  }

  for (const spec of filtered) {
    const col = document.querySelector(`.column[data-status="${spec.status}"] .column__body`);
    if (col) {
      col.appendChild(renderCard(spec));
    } else {
      // Unknown status, put in 초안
      const fallback = document.querySelector('.column[data-status="초안"] .column__body');
      if (fallback) fallback.appendChild(renderCard(spec));
    }
  }

  // Update counts
  updateColumnCounts();

  // Update spec count
  document.getElementById('spec-count').textContent = `${filtered.length}개 스펙`;

  // Show empty state
  document.querySelectorAll('.column__body').forEach(body => {
    if (body.children.length === 0) {
      body.innerHTML = '<div class="column__empty">없음</div>';
    }
  });
}
