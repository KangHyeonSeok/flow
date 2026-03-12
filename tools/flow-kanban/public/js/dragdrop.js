let draggedCard = null;

function setupCardDrag(card) {
  card.addEventListener('dragstart', (e) => {
    draggedCard = card;
    card.classList.add('dragging');
    e.dataTransfer.effectAllowed = 'move';
    e.dataTransfer.setData('text/plain', card.dataset.specId);
  });

  card.addEventListener('dragend', () => {
    card.classList.remove('dragging');
    draggedCard = null;
    document.querySelectorAll('.column__body.drag-over').forEach(el => {
      el.classList.remove('drag-over');
    });
  });
}

function setupColumnDrop(columnBody, status) {
  columnBody.addEventListener('dragover', (e) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';
    columnBody.classList.add('drag-over');
  });

  columnBody.addEventListener('dragleave', (e) => {
    if (!columnBody.contains(e.relatedTarget)) {
      columnBody.classList.remove('drag-over');
    }
  });

  columnBody.addEventListener('drop', async (e) => {
    e.preventDefault();
    columnBody.classList.remove('drag-over');

    const specId = e.dataTransfer.getData('text/plain');
    if (!specId || !draggedCard) return;

    // Check if already in this column
    if (draggedCard.closest('.column')?.dataset.status === status) return;

    // Optimistic UI: move card immediately
    const oldParent = draggedCard.parentNode;
    columnBody.appendChild(draggedCard);
    updateColumnCounts();

    try {
      await API.updateStatus(specId, status);
      showToast(`${specId} → ${statusLabel(status)}`, 'success');
      // Refresh after a short delay to get updated data
      setTimeout(() => refreshBoard(), 500);
    } catch (err) {
      // Revert on error
      oldParent.appendChild(draggedCard);
      updateColumnCounts();
      showToast(`이동 실패: ${err.message}`, 'error');
    }
  });
}

function updateColumnCounts() {
  document.querySelectorAll('.column').forEach(col => {
    const count = col.querySelector('.column__body').children.length;
    const badge = col.querySelector('.column__count');
    if (badge) badge.textContent = count;
  });
}
