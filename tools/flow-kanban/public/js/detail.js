async function openDetail(specId) {
  const overlay = document.getElementById('detail-overlay');
  const content = document.getElementById('detail-content');

  overlay.classList.add('open');
  content.innerHTML = '<div style="padding:40px;text-align:center;color:var(--text-dim)">로딩 중...</div>';

  try {
    const spec = await API.getSpec(specId);
    content.innerHTML = renderDetail(spec);
    attachDetailHandlers(spec);
  } catch (err) {
    content.innerHTML = `<div style="padding:40px;color:var(--red)">오류: ${escapeHtml(err.message)}</div>`;
  }
}

function closeDetail() {
  document.getElementById('detail-overlay').classList.remove('open');
}

function renderDetail(spec) {
  return `
    <div class="detail-header">
      <div class="detail-header__id">${escapeHtml(spec.id)} · ${escapeHtml(spec.group)}</div>
      <div class="detail-header__title">${escapeHtml(spec.title)}</div>
      <div class="detail-header__badges">
        <span class="detail-badge detail-badge--status">${spec.status}</span>
        <span class="detail-badge detail-badge--type">${spec.type}</span>
        ${spec.attemptCount > 0 ? `<span class="detail-badge detail-badge--attempts">시도 ${spec.attemptCount}회</span>` : ''}
        ${spec.lastFailReason ? `<span class="detail-badge" style="background:rgba(243,139,168,0.15);color:var(--red)">${escapeHtml(spec.lastFailReason)}</span>` : ''}
      </div>
    </div>

    ${renderSpecContent(spec)}
    ${renderConditions(spec.conditions)}
    ${renderTests(spec)}
    ${renderEvidence(spec)}
    ${renderQuestions(spec)}
    ${renderActivity(spec.activity)}
    ${renderRelatedFiles(spec.relatedFiles)}
  `;
}

function renderSpecContent(spec) {
  if (!spec.specMd) return '';
  return `
    <details class="detail-section" open>
      <summary>스펙 본문</summary>
      <div class="detail-section__body">
        <div class="spec-content">${escapeHtml(spec.specMd)}</div>
      </div>
    </details>
  `;
}

function renderConditions(conditions) {
  if (!conditions || conditions.length === 0) return '';
  return `
    <details class="detail-section" open>
      <summary>조건 <span class="detail-section__count">${conditions.length}</span></summary>
      <div class="detail-section__body">
        <table class="conditions-table">
          <thead><tr><th>ID</th><th>설명</th><th>상태</th></tr></thead>
          <tbody>
            ${conditions.map(c => `
              <tr>
                <td style="font-family:var(--font-mono);color:var(--accent)">${escapeHtml(c.id)}</td>
                <td>${escapeHtml(c.description)}</td>
                <td><span class="condition-status condition-status--${(c.status || '초안').replace(/\s/g, '')}">${c.status || '초안'}</span></td>
              </tr>
            `).join('')}
          </tbody>
        </table>
      </div>
    </details>
  `;
}

function renderTests(spec) {
  const tests = spec.tests || [];
  if (tests.length === 0) return '';
  return `
    <details class="detail-section" open>
      <summary>테스트 <span class="detail-section__count">${tests.length}</span></summary>
      <div class="detail-section__body">
        ${tests.map(t => renderTestItem(spec, t)).join('')}
      </div>
    </details>
  `;
}

function renderTestItem(spec, test) {
  const resultClass = test.lastResult === 'pass' ? 'test-result--pass' :
                      test.lastResult === 'fail' ? 'test-result--fail' : 'test-result--pending';
  const resultText = test.lastResult || '미실행';

  let actions = '';
  if (test.type === '사용자 테스트') {
    actions = `
      <div class="test-actions">
        <button class="btn btn--pass" data-spec-id="${spec.id}" data-test-id="${test.id}" data-result="pass">Pass</button>
        <button class="btn btn--fail" data-spec-id="${spec.id}" data-test-id="${test.id}" data-result="fail">Fail</button>
      </div>
      <textarea class="test-comment" data-test-id="${test.id}" placeholder="실패 시 의견을 입력하세요..." style="display:none"></textarea>
    `;
  }

  return `
    <div class="test-item">
      <div class="test-item__header">
        <span class="test-item__id">${escapeHtml(test.id)}</span>
        <span class="test-item__type">${test.type || '단위 테스트'}</span>
        <span class="test-item__result ${resultClass}">${resultText}</span>
      </div>
      ${test.description ? `<div style="color:var(--text-muted);font-size:11px;margin-top:2px">${escapeHtml(test.description)}</div>` : ''}
      ${actions}
    </div>
  `;
}

function renderEvidence(spec) {
  const files = spec.evidenceFiles || [];
  if (files.length === 0) return '';
  return `
    <details class="detail-section">
      <summary>증거 <span class="detail-section__count">${files.length}</span></summary>
      <div class="detail-section__body">
        <div class="evidence-grid">
          ${files.map(f => {
            const isImage = /\.(png|jpg|jpeg|gif|webp)$/i.test(f.name);
            const url = `/api/specs/${encodeURIComponent(spec.id)}/evidence/${encodeURIComponent(f.path)}`;
            if (isImage) {
              return `<div class="evidence-item"><img src="${url}" alt="${escapeHtml(f.name)}" loading="lazy"><div>${escapeHtml(f.name)}</div></div>`;
            }
            return `<div class="evidence-item"><div style="padding:10px">📄</div><div>${escapeHtml(f.name)}</div></div>`;
          }).join('')}
        </div>
      </div>
    </details>
  `;
}

function renderQuestions(spec) {
  const questions = spec.questions || [];
  if (questions.length === 0) return '';
  return `
    <details class="detail-section" open>
      <summary>질문 <span class="detail-section__count">${questions.length}</span></summary>
      <div class="detail-section__body">
        ${questions.map(q => renderQuestionItem(spec, q)).join('')}
      </div>
    </details>
  `;
}

function renderQuestionItem(spec, q) {
  const statusCls = q.status === '응답 완료' ? 'q-status--응답완료' : 'q-status--응답대기';
  let answerHtml = '';

  if (q.answer) {
    answerHtml = `<div class="question-item__answer">${escapeHtml(q.answer)}</div>`;
  } else {
    answerHtml = `
      <div style="display:flex;gap:6px;margin-top:8px">
        <input class="answer-input" data-spec-id="${spec.id}" data-question-id="${q.id}" placeholder="답변 입력...">
        <button class="btn" data-action="answer" data-spec-id="${spec.id}" data-question-id="${q.id}">답변</button>
      </div>
    `;
  }

  return `
    <div class="question-item">
      <div>
        <strong>${escapeHtml(q.id)}</strong>
        <span class="question-item__status ${statusCls}">${q.status}</span>
      </div>
      <div class="question-item__text">${escapeHtml(q.question)}</div>
      ${answerHtml}
    </div>
  `;
}

function renderActivity(activity) {
  if (!activity || activity.length === 0) return '';
  return `
    <details class="detail-section">
      <summary>활동 로그 <span class="detail-section__count">${activity.length}</span></summary>
      <div class="detail-section__body">
        ${activity.map(a => `
          <div class="activity-item">
            <span class="activity-item__time">${a.timestamp || ''}</span>
            <span class="activity-item__role">${a.role || ''}</span>
            ${a.result ? `<span class="activity-item__role">${a.result}</span>` : ''}
            <div class="activity-item__summary">${escapeHtml(a.summary || '')}</div>
            ${a.statusChange ? `<div class="activity-item__detail">상태: ${escapeHtml(a.statusChange)}</div>` : ''}
            ${a.detail ? `<div class="activity-item__detail">${escapeHtml(a.detail)}</div>` : ''}
          </div>
        `).join('')}
      </div>
    </details>
  `;
}

function renderRelatedFiles(files) {
  if (!files || files.length === 0) return '';
  return `
    <details class="detail-section">
      <summary>관련 파일 <span class="detail-section__count">${files.length}</span></summary>
      <div class="detail-section__body">
        <ul class="file-list">
          ${files.map(f => `<li>${escapeHtml(typeof f === 'string' ? f : f.path)}</li>`).join('')}
        </ul>
      </div>
    </details>
  `;
}

function attachDetailHandlers(spec) {
  // User test Pass/Fail buttons
  document.querySelectorAll('[data-result]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const specId = btn.dataset.specId;
      const testId = btn.dataset.testId;
      const result = btn.dataset.result;

      if (result === 'fail') {
        const textarea = document.querySelector(`.test-comment[data-test-id="${testId}"]`);
        if (textarea && textarea.style.display === 'none') {
          textarea.style.display = 'block';
          textarea.focus();
          return; // Show textarea first, submit on second click
        }
        const comment = textarea ? textarea.value : '';
        try {
          await API.submitTestResult(specId, testId, 'fail', comment);
          showToast('테스트 결과 저장: Fail', 'success');
          openDetail(specId); // Refresh
        } catch (err) {
          showToast(`저장 실패: ${err.message}`, 'error');
        }
      } else {
        try {
          await API.submitTestResult(specId, testId, 'pass', '');
          showToast('테스트 결과 저장: Pass', 'success');
          openDetail(specId);
        } catch (err) {
          showToast(`저장 실패: ${err.message}`, 'error');
        }
      }
    });
  });

  // Question answer buttons
  document.querySelectorAll('[data-action="answer"]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const specId = btn.dataset.specId;
      const qId = btn.dataset.questionId;
      const input = document.querySelector(`.answer-input[data-question-id="${qId}"]`);
      const answer = input ? input.value.trim() : '';
      if (!answer) return;

      try {
        await API.answerQuestion(specId, qId, answer);
        showToast('답변 저장 완료', 'success');
        openDetail(specId);
      } catch (err) {
        showToast(`답변 저장 실패: ${err.message}`, 'error');
      }
    });
  });
}
