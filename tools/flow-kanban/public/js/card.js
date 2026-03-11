function renderCard(spec) {
  const card = document.createElement('div');
  card.className = 'card';
  card.setAttribute('draggable', 'true');
  card.dataset.specId = spec.id;

  const timeStr = spec.updatedAt ? formatRelativeTime(spec.updatedAt) : '';

  let metaBadges = '';
  metaBadges += `<span class="card__badge badge--project">${escapeHtml(spec.project)}</span>`;
  metaBadges += `<span class="card__badge badge--type-${spec.type}">${spec.type}</span>`;

  if (spec.attemptCount > 0) {
    metaBadges += `<span class="card__badge badge--attempts">시도 ${spec.attemptCount}</span>`;
  }

  if (spec.openQuestionCount > 0) {
    metaBadges += `<span class="card__badge badge--questions">질문 ${spec.openQuestionCount}</span>`;
  }

  if (spec.testSummary && spec.testSummary.total > 0) {
    const allPass = spec.testSummary.pass === spec.testSummary.total;
    const cls = allPass ? 'badge--tests' : 'badge--tests-fail';
    metaBadges += `<span class="card__badge ${cls}">${spec.testSummary.pass}/${spec.testSummary.total}</span>`;
  }

  let failHtml = '';
  if (spec.lastFailReason) {
    failHtml = `<div class="card__fail-reason" title="${escapeHtml(spec.lastFailReason)}">${escapeHtml(spec.lastFailReason)}</div>`;
  }

  card.innerHTML = `
    <div class="card__id">${escapeHtml(spec.id)}</div>
    <div class="card__title">${escapeHtml(spec.title)}</div>
    <div class="card__meta">
      ${metaBadges}
      <span class="card__time">${timeStr}</span>
    </div>
    ${failHtml}
  `;

  card.addEventListener('click', () => openDetail(spec.id));
  setupCardDrag(card);

  return card;
}

function formatRelativeTime(isoStr) {
  const date = new Date(isoStr);
  const now = new Date();
  const diffMs = now - date;
  const diffMin = Math.floor(diffMs / 60000);
  if (diffMin < 1) return '방금';
  if (diffMin < 60) return `${diffMin}분 전`;
  const diffHr = Math.floor(diffMin / 60);
  if (diffHr < 24) return `${diffHr}시간 전`;
  const diffDay = Math.floor(diffHr / 24);
  if (diffDay < 7) return `${diffDay}일 전`;
  return date.toLocaleDateString('ko-KR');
}

function escapeHtml(str) {
  const div = document.createElement('div');
  div.textContent = str;
  return div.innerHTML;
}
