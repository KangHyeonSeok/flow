/**
 * KanbanPanel - 스펙 상태별 칸반 보드 Webview 패널
 *
 * 스펙을 작업 상태별로 칸반 컬럼에 표시한다.
 * 칸반 컬럼으로 구분하여 표시한다. 카드 클릭 시 상세 패널 열림.
 */
import * as vscode from 'vscode';
import { SpecLoader } from './specLoader';
import { Spec, SpecStatus, STATUS_COLORS, isValidStatus, VALID_STATUSES } from './types';
import { getSpecReviewState, getUserFeedbackState } from './reviewState';

const COLUMNS: { status: SpecStatus; label: string; icon: string }[] = [
    { status: 'draft',             label: '초안',              icon: '○' },
  { status: 'queued',            label: '대기중',            icon: '→' },
  { status: 'working',           label: '작업중',            icon: '●' },
  { status: 'needs-review',      label: '검토 대기',          icon: '⚠' },
  { status: 'verified',          label: '검증 완료',          icon: '✔' },
  { status: 'deprecated',        label: '폐기',              icon: '✕' },
  { status: 'done',              label: '완료',              icon: '✔✔' },
];

export class KanbanPanel {
    public static currentPanel: KanbanPanel | undefined;
    private static readonly viewType = 'specGraph.kanbanView';

    private readonly panel: vscode.WebviewPanel;
    private disposables: vscode.Disposable[] = [];

    private constructor(
        panel: vscode.WebviewPanel,
        private extensionUri: vscode.Uri,
        private loader: SpecLoader,
        private workspaceRoot: string,
    ) {
        this.panel = panel;

        this.panel.onDidDispose(() => this.dispose(), null, this.disposables);

        this.panel.webview.onDidReceiveMessage(
            (msg) => this.handleMessage(msg),
            null,
            this.disposables,
        );

        this.loader.onDidChange(() => this.update());
    }

    /** 싱글톤 패널 생성 또는 포커스 */
    static createOrShow(
        extensionUri: vscode.Uri,
        loader: SpecLoader,
        workspaceRoot: string,
    ): KanbanPanel {
        const column = vscode.ViewColumn.One;

        if (KanbanPanel.currentPanel) {
            KanbanPanel.currentPanel.panel.reveal(column);
            return KanbanPanel.currentPanel;
        }

        const panel = vscode.window.createWebviewPanel(
            KanbanPanel.viewType,
            '칸반 보드',
            column,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [extensionUri],
            },
        );

        KanbanPanel.currentPanel = new KanbanPanel(panel, extensionUri, loader, workspaceRoot);
        KanbanPanel.currentPanel.update();
        return KanbanPanel.currentPanel;
    }

    /** 렌더링 갱신 */
    async update(): Promise<void> {
        try {
            const graph = await this.loader.getGraph();
            this.panel.webview.html = this.getHtml(graph.specs);
        } catch (e) {
            this.panel.webview.html = this.getErrorHtml(`칸반 보드 로드 실패: ${String(e)}`);
        }
    }

    /** Webview → Extension 메시지 핸들링 */
    private async handleMessage(msg: { type: string; [key: string]: unknown }): Promise<void> {
        switch (msg.type) {
            case 'selectSpec': {
                const specId = msg.specId as string;
                vscode.commands.executeCommand('specGraph.showDetail', specId);
                break;
            }
            case 'openSpec': {
                const specId = msg.specId as string;
                const filePath = vscode.Uri.file(
                    require('path').join(this.loader.specsDirectory, `${specId}.json`),
                );
                try {
                    const doc = await vscode.workspace.openTextDocument(filePath);
                    await vscode.window.showTextDocument(doc, vscode.ViewColumn.Beside);
                } catch {
                    vscode.window.showWarningMessage(`스펙 파일을 찾을 수 없습니다: ${specId}.json`);
                }
                break;
            }
            case 'changeStatus': {
                const { specId, newStatus } = msg as { type: string; specId: string; newStatus: string };
                await this.changeSpecStatus(specId, newStatus);
                break;
            }
        }
    }

    /** 스펙 상태 변경 — F-090-C1: 유효하지 않은 상태값 거부 */
    private async changeSpecStatus(specId: string, newStatus: string): Promise<void> {
        // F-090-C1: 유효성 검사
        if (!isValidStatus(newStatus)) {
            vscode.window.showErrorMessage(
                `유효하지 않은 상태값: '${newStatus}'. 허용 상태: ${VALID_STATUSES.join(', ')}`
            );
            return;
        }

        const path = require('path') as typeof import('path');
        const fs = require('fs') as typeof import('fs');
        const filePath = path.join(this.loader.specsDirectory, `${specId}.json`);

        try {
            const raw = fs.readFileSync(filePath, 'utf-8');
            const obj = JSON.parse(raw);
            obj.status = newStatus;
            obj.updatedAt = new Date().toISOString();
            fs.writeFileSync(filePath, JSON.stringify(obj, null, 2), 'utf-8');
            await this.loader.reload();
          await this.panel.webview.postMessage({
            type: 'statusChanged',
            specId,
            newStatus,
          });
            vscode.window.showInformationMessage(`"${specId}" 상태를 "${newStatus}"으로 변경했습니다.`);
        } catch (err) {
          await this.panel.webview.postMessage({
            type: 'statusChangeFailed',
            specId,
            newStatus,
          });
            vscode.window.showErrorMessage(`상태 변경 실패: ${String(err)}`);
        }
    }

    /** 정리 */
    dispose(): void {
        KanbanPanel.currentPanel = undefined;
        this.panel.dispose();
        while (this.disposables.length) {
            const d = this.disposables.pop();
            if (d) { d.dispose(); }
        }
    }

    // ─── HTML 생성 ───────────────────────────────────────────────────────────

    private getHtml(specs: Spec[]): string {
        const byStatus: Record<SpecStatus, Spec[]> = {
            'draft': [],
          'queued': [],
          'working': [],
            'needs-review': [],
            'verified': [],
            'deprecated': [],
            'done': [],
        };

        for (const spec of specs) {
            const bucket = byStatus[spec.status];
            if (bucket) { bucket.push(spec); }
        }

        const columnsHtml = COLUMNS.map(col => {
            const items = byStatus[col.status];
            const color = STATUS_COLORS[col.status];
            const cardsHtml = items.length === 0
                ? '<div class="empty-col">스펙 없음</div>'
                : items.map(s => this.renderCard(s)).join('');

            return /* html */`
<div class="column" data-status="${col.status}">
  <div class="col-header" style="border-top: 3px solid ${color};">
    <span class="col-icon" style="color:${color};">${col.icon}</span>
    <span class="col-title">${col.label}</span>
    <span class="col-count">${items.length}</span>
  </div>
  <div class="col-body" id="col-${col.status}">
    ${cardsHtml}
  </div>
</div>`;
        }).join('');

        const statusOptions = COLUMNS.map(c =>
            `<option value="${c.status}">${c.label}</option>`
        ).join('');

        return /* html */`<!DOCTYPE html>
<html lang="ko">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1.0">
<title>칸반 보드</title>
<style>
  :root {
    --bg: var(--vscode-editor-background);
    --fg: var(--vscode-editor-foreground);
    --border: var(--vscode-panel-border, #3c3c3c);
    --card-bg: var(--vscode-editorWidget-background, #252526);
    --card-hover: var(--vscode-list-hoverBackground, #2a2d2e);
    --badge-bg: var(--vscode-badge-background, #4d4d4d);
    --badge-fg: var(--vscode-badge-foreground, #ffffff);
    --tag-bg: var(--vscode-editorInlayHint-background, #373737);
    --tag-fg: var(--vscode-editorInlayHint-foreground, #9cdcfe);
    --input-bg: var(--vscode-input-background, #3c3c3c);
    --input-fg: var(--vscode-input-foreground, #cccccc);
    --input-border: var(--vscode-input-border, #555);
    --btn-bg: var(--vscode-button-background, #0e639c);
    --btn-fg: var(--vscode-button-foreground, #ffffff);
  }
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body {
    background: var(--bg);
    color: var(--fg);
    font-family: var(--vscode-font-family, 'Segoe UI', sans-serif);
    font-size: 13px;
    display: flex;
    flex-direction: column;
    height: 100vh;
    overflow: hidden;
  }
  /* ── 도구바 ── */
  .toolbar {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 8px 12px;
    border-bottom: 1px solid var(--border);
    flex-shrink: 0;
    flex-wrap: wrap;
  }
  .toolbar-title {
    font-size: 14px;
    font-weight: 600;
    margin-right: 8px;
  }
  .toolbar input[type="text"] {
    background: var(--input-bg);
    color: var(--input-fg);
    border: 1px solid var(--input-border);
    border-radius: 3px;
    padding: 3px 8px;
    font-size: 12px;
    width: 180px;
    outline: none;
  }
  .toolbar input[type="text"]:focus {
    border-color: var(--btn-bg);
  }
  .total-count {
    margin-left: auto;
    font-size: 12px;
    opacity: 0.6;
  }
  /* ── 보드 ── */
  .board {
    display: flex;
    gap: 10px;
    padding: 12px;
    overflow-x: auto;
    flex: 1;
    align-items: flex-start;
  }
  .column {
    background: var(--card-bg);
    border: 1px solid var(--border);
    border-radius: 6px;
    min-width: 220px;
    max-width: 280px;
    flex: 1;
    display: flex;
    flex-direction: column;
    max-height: 100%;
  }
  .col-header {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 10px 12px 8px;
    border-radius: 6px 6px 0 0;
    flex-shrink: 0;
  }
  .col-icon { font-size: 14px; }
  .col-title { font-weight: 600; flex: 1; }
  .col-count {
    background: var(--badge-bg);
    color: var(--badge-fg);
    border-radius: 10px;
    padding: 0 7px;
    font-size: 11px;
    min-width: 20px;
    text-align: center;
  }
  .col-body {
    padding: 8px;
    overflow-y: auto;
    flex: 1;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .empty-col {
    color: var(--fg);
    opacity: 0.35;
    font-size: 12px;
    text-align: center;
    padding: 20px 0;
  }
  /* ── 카드 ── */
  .card {
    background: var(--bg);
    border: 1px solid var(--border);
    border-radius: 5px;
    padding: 9px 10px;
    cursor: pointer;
    transition: background 0.12s, border-color 0.12s, transform 0.08s;
    position: relative;
  }
  .card:hover {
    background: var(--card-hover);
    border-color: var(--btn-bg);
    transform: translateY(-1px);
  }
  .card:focus {
    outline: 2px solid var(--btn-bg);
    outline-offset: 1px;
  }
  .card.selected {
    border-color: var(--btn-bg);
    box-shadow: 0 0 0 2px var(--btn-bg);
  }
  .card-id {
    font-size: 10px;
    opacity: 0.55;
    margin-bottom: 3px;
    font-family: var(--vscode-editor-font-family, monospace);
  }
  .card-title {
    font-size: 12px;
    font-weight: 500;
    line-height: 1.4;
    margin-bottom: 5px;
    word-break: break-word;
  }
  .card-progress {
    margin-top: 8px;
  }
  .card-progress-meta {
    display: flex;
    justify-content: space-between;
    gap: 8px;
    margin-bottom: 4px;
    font-size: 10px;
    opacity: 0.72;
  }
  .progress-track {
    width: 100%;
    height: 5px;
    border-radius: 999px;
    overflow: hidden;
    background: var(--border);
  }
  .progress-fill {
    height: 100%;
  }
  .card-tags {
    display: flex;
    flex-wrap: wrap;
    gap: 3px;
    margin-bottom: 5px;
  }
  .tag {
    background: var(--tag-bg);
    color: var(--tag-fg);
    border-radius: 3px;
    padding: 1px 5px;
    font-size: 10px;
  }
  .card-footer {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-top: 4px;
  }
  .card-date {
    font-size: 10px;
    opacity: 0.45;
  }
  .review-badge {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    margin-top: 8px;
    padding: 3px 7px;
    border-radius: 999px;
    font-size: 10px;
    font-weight: 600;
    border: 1px solid rgba(255, 152, 0, 0.45);
    background: rgba(255, 152, 0, 0.14);
    color: #ffb74d;
  }
  .user-input-badge {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    margin-top: 8px;
    padding: 3px 7px;
    border-radius: 999px;
    font-size: 10px;
    font-weight: 700;
    border: 1px solid rgba(233, 30, 99, 0.5);
    background: rgba(233, 30, 99, 0.15);
    color: #f48fb1;
  }
  .question-count {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    font-size: 10px;
    padding: 1px 5px;
    border-radius: 999px;
    background: rgba(233, 30, 99, 0.15);
    color: #f48fb1;
    border: 1px solid rgba(233, 30, 99, 0.3);
    margin-left: 4px;
  }
  .card.user-input-required {
    border-left-color: #e91e63 !important;
  }
  .review-note {
    margin-top: 6px;
    font-size: 10px;
    line-height: 1.45;
    opacity: 0.8;
  }
  .card-actions {
    display: flex;
    gap: 4px;
    opacity: 0;
    transition: opacity 0.1s;
  }
  .card:hover .card-actions { opacity: 1; }
  .action-btn {
    background: transparent;
    border: 1px solid var(--border);
    color: var(--fg);
    border-radius: 3px;
    padding: 2px 6px;
    font-size: 10px;
    cursor: pointer;
    transition: background 0.1s;
  }
  .action-btn:hover { background: var(--card-hover); }
  /* ── 상태 변경 드롭다운 ── */
  .status-select {
    background: var(--input-bg);
    color: var(--input-fg);
    border: 1px solid var(--input-border);
    border-radius: 3px;
    font-size: 10px;
    padding: 2px 4px;
    cursor: pointer;
    outline: none;
  }
  /* ── 컨텍스트 메뉴 ── */
  .ctx-menu {
    position: fixed;
    background: var(--card-bg);
    border: 1px solid var(--border);
    border-radius: 5px;
    padding: 4px 0;
    z-index: 9999;
    min-width: 160px;
    box-shadow: 0 4px 12px rgba(0,0,0,0.3);
    display: none;
  }
  .ctx-menu.visible { display: block; }
  .ctx-item {
    padding: 6px 14px;
    cursor: pointer;
    font-size: 12px;
  }
  .ctx-item:hover { background: var(--card-hover); }
  .ctx-separator {
    border: none;
    border-top: 1px solid var(--border);
    margin: 3px 0;
  }
</style>
</head>
<body>
<div class="toolbar">
  <span class="toolbar-title">📋 칸반 보드</span>
  <input type="text" id="searchInput" placeholder="스펙 검색..." oninput="filterCards(this.value)">
  <span class="total-count" id="totalCount">총 ${specs.length}개</span>
</div>
<div class="board" id="board">
  ${columnsHtml}
</div>

<!-- 컨텍스트 메뉴 -->
<div class="ctx-menu" id="ctxMenu">
  <div class="ctx-item" id="ctxOpen">파일 열기</div>
  <div class="ctx-item" id="ctxDetail">상세 보기</div>
  <hr class="ctx-separator">
  <div class="ctx-item ctx-status-group" style="font-size:11px;opacity:0.6;cursor:default;">상태 변경</div>
  ${COLUMNS.map(c => `<div class="ctx-item ctx-status" data-status="${c.status}">${c.icon} ${c.label}</div>`).join('')}
</div>

<script>
const vscode = acquireVsCodeApi();
let ctxSpecId = null;

const STATUS_COLORS = {
  'draft': '#9e9e9e',
  'queued': '#9c27b0',
  'working': '#2196f3',
  'needs-review': '#ff9800',
  'verified': '#4caf50',
  'deprecated': '#f44336',
  'done': '#795548'
};

window.addEventListener('message', (event) => {
  const message = event.data;
  if (!message || typeof message !== 'object') { return; }

  if (message.type === 'statusChanged') {
    moveCardToStatus(message.specId, message.newStatus);
  }

  if (message.type === 'statusChangeFailed') {
    resetCardStatusSelect(message.specId);
  }
});

function setSelectedCard(card) {
  document.querySelectorAll('.card.selected').forEach(c => c.classList.remove('selected'));
  card.classList.add('selected');
}

// ── 카드 클릭 → 상세 보기
document.getElementById('board').addEventListener('click', (e) => {
  const card = e.target.closest('.card');
  if (!card) { return; }
  // status-select나 컨텍스트 메뉴 컨트롤 클릭이면 무시
  if (e.target.closest('.action-btn') || e.target.closest('.status-select')) { return; }
  setSelectedCard(card);
  vscode.postMessage({ type: 'selectSpec', specId: card.dataset.id });
});

// ── 카드 키보드 탐색 → Enter/Space로 상세 보기 (F-093-C3)
document.getElementById('board').addEventListener('keydown', (e) => {
  if (e.key !== 'Enter' && e.key !== ' ') { return; }
  const card = e.target.closest('.card');
  if (!card) { return; }
  e.preventDefault();
  setSelectedCard(card);
  vscode.postMessage({ type: 'selectSpec', specId: card.dataset.id });
});

// ── 카드 우클릭 → 컨텍스트 메뉴
document.getElementById('board').addEventListener('contextmenu', (e) => {
  const card = e.target.closest('.card');
  if (!card) { return; }
  e.preventDefault();
  ctxSpecId = card.dataset.id;
  showCtxMenu(e.clientX, e.clientY);
});

function showCtxMenu(x, y) {
  const menu = document.getElementById('ctxMenu');
  menu.style.left = x + 'px';
  menu.style.top = y + 'px';
  menu.classList.add('visible');
}

document.addEventListener('click', () => {
  document.getElementById('ctxMenu').classList.remove('visible');
});

document.getElementById('ctxOpen').addEventListener('click', () => {
  if (ctxSpecId) { vscode.postMessage({ type: 'openSpec', specId: ctxSpecId }); }
});
document.getElementById('ctxDetail').addEventListener('click', () => {
  if (ctxSpecId) { vscode.postMessage({ type: 'selectSpec', specId: ctxSpecId }); }
});
document.querySelectorAll('.ctx-status').forEach(el => {
  el.addEventListener('click', () => {
    if (ctxSpecId) {
      vscode.postMessage({ type: 'changeStatus', specId: ctxSpecId, newStatus: el.dataset.status });
    }
  });
});

// ── 파일 열기 버튼
document.getElementById('board').addEventListener('click', (e) => {
  const btn = e.target.closest('.action-btn');
  if (!btn) { return; }
  const card = btn.closest('.card');
  if (!card) { return; }
  const action = btn.dataset.action;
  if (action === 'open') {
    vscode.postMessage({ type: 'openSpec', specId: card.dataset.id });
  }
  e.stopPropagation();
});

// ── 상태 변경 select
document.getElementById('board').addEventListener('change', (e) => {
  const sel = e.target.closest('.status-select');
  if (!sel) { return; }
  const card = sel.closest('.card');
  if (!card) { return; }
  card.dataset.pendingStatus = sel.value;
  vscode.postMessage({ type: 'changeStatus', specId: card.dataset.id, newStatus: sel.value });
  e.stopPropagation();
});

function moveCardToStatus(specId, newStatus) {
  const card = document.querySelector('.card[data-id="' + cssEscape(specId) + '"]');
  if (!card) { return; }

  const targetColumn = document.getElementById('col-' + newStatus);
  if (!targetColumn) { return; }

  const currentColumn = card.closest('.col-body');
  const select = card.querySelector('.status-select');
  if (select) {
    select.value = newStatus;
  }

  delete card.dataset.pendingStatus;
  card.style.borderLeftColor = STATUS_COLORS[newStatus] || '#888';

  if (currentColumn !== targetColumn) {
    const targetEmpty = targetColumn.querySelector('.empty-col');
    if (targetEmpty) {
      targetEmpty.remove();
    }
    targetColumn.prepend(card);
    syncEmptyState(currentColumn);
  }

  syncEmptyState(targetColumn);
  syncColumnCounts();
}

function resetCardStatusSelect(specId) {
  const card = document.querySelector('.card[data-id="' + cssEscape(specId) + '"]');
  if (!card) { return; }

  const select = card.querySelector('.status-select');
  if (select) {
    const originalStatus = card.closest('.column')?.dataset.status;
    if (originalStatus) {
      select.value = originalStatus;
    }
  }

  delete card.dataset.pendingStatus;
}

function syncEmptyState(columnBody) {
  if (!columnBody) { return; }

  const visibleCards = Array.from(columnBody.querySelectorAll('.card')).filter((card) => card.style.display !== 'none');
  let empty = columnBody.querySelector('.empty-col');

  if (visibleCards.length === 0) {
    if (!empty) {
      empty = document.createElement('div');
      empty.className = 'empty-col';
      empty.textContent = '스펙 없음';
      columnBody.appendChild(empty);
    } else {
      empty.style.display = '';
      empty.textContent = '스펙 없음';
    }
    return;
  }

  if (empty) {
    empty.remove();
  }
}

function syncColumnCounts() {
  document.querySelectorAll('.column').forEach((column) => {
    const count = Array.from(column.querySelectorAll('.card')).filter((card) => card.style.display !== 'none').length;
    const badge = column.querySelector('.col-count');
    if (badge) {
      badge.textContent = String(count);
    }
  });
}

function cssEscape(value) {
  if (window.CSS && typeof window.CSS.escape === 'function') {
    return window.CSS.escape(value);
  }
  return String(value).replace(/(["\\#.;?+*~':!^$\[\]()=>|/@])/g, '\\$1');
}

// ── 검색 필터
function filterCards(query) {
  const q = query.toLowerCase().trim();
  const cards = document.querySelectorAll('.card');
  let visible = 0;
  cards.forEach(card => {
    const text = (card.dataset.id + ' ' + card.dataset.title + ' ' + card.dataset.tags).toLowerCase();
    const show = !q || text.includes(q);
    card.style.display = show ? '' : 'none';
    if (show) { visible++; }
  });
  document.getElementById('totalCount').textContent = q ? \`\${visible} / ${specs.length}개\` : \`총 ${specs.length}개\`;

  // 컬럼 빈 상태 표시
  document.querySelectorAll('.col-body').forEach(col => {
    const anyVisible = Array.from(col.querySelectorAll('.card')).some(c => c.style.display !== 'none');
    let empty = col.querySelector('.empty-col');
    if (!anyVisible) {
      if (!empty) {
        empty = document.createElement('div');
        empty.className = 'empty-col';
        empty.textContent = '검색 결과 없음';
        col.appendChild(empty);
      } else {
        empty.style.display = '';
      }
    } else if (empty) {
      empty.style.display = 'none';
    }
  });

  // 컬럼 카운트 업데이트
  document.querySelectorAll('.column').forEach(col => {
    const status = col.dataset.status;
    const count = Array.from(col.querySelectorAll('.card')).filter(c => c.style.display !== 'none').length;
    const badge = col.querySelector('.col-count');
    if (badge) { badge.textContent = count; }
  });
}
</script>
</body>
</html>`;
    }

    private renderCard(spec: Spec): string {
        const color = STATUS_COLORS[spec.status];
        const review = getSpecReviewState(spec);
        const feedback = getUserFeedbackState(spec);
        // C6: 태그 목록 기본 렌더링에서 제거 — 다음 행동 결정 정보만 표시
        const date = spec.updatedAt ? spec.updatedAt.slice(0, 10) : '';
        const title = spec.title || spec.id;
        const progressHtml = review.totalConditions > 0
            ? `<div class="card-progress">
    <div class="card-progress-meta">
      <span>조건 ${review.verifiedConditions}/${review.totalConditions}</span>
      <span>${review.progressPercent}%</span>
    </div>
    <div class="progress-track"><div class="progress-fill" style="width:${review.progressPercent}%;background:${review.progressPercent === 100 ? '#4caf50' : '#2196f3'}"></div></div>
    </div>`
            : '';

        // C1: 사용자 판단 필요 배지 (requiresUserInput 또는 questionStatus='waiting-user-input')
        const userInputBadge = feedback.requiresUserInput
            ? `<div class="user-input-badge">❓ 사용자 판단 필요${feedback.openQuestionCount > 0 ? ` (${feedback.openQuestionCount}건)` : ''}</div>`
            : '';

        // C6: 미해결 질문 수 표시 (requiresUserInput 아니더라도 open 질문 있으면 표시)
        const openQuestionInfo = !feedback.requiresUserInput && feedback.openQuestionCount > 0
            ? `<div class="question-count">❓ 미해결 질문 ${feedback.openQuestionCount}건</div>`
            : '';

        const reviewBadge = review.requiresManualVerification && spec.status !== 'verified'
            ? `<div class="review-badge">⚠ 수동 검증 ${review.manualVerificationItems.length}건</div>`
            : '';
        const reviewNote = review.requiresManualVerification && spec.status !== 'verified'
            ? `<div class="review-note">${this.esc(review.manualVerificationItems[0]?.reason || review.manualVerificationItems[0]?.label || '수동 검증 항목 확인 필요')}</div>`
            : '';

        const statusOptions = COLUMNS.map(c =>
            `<option value="${c.status}"${c.status === spec.status ? ' selected' : ''}>${c.label}</option>`
        ).join('');

        // C5: 날짜를 카드 헤더(ID 옆 같은 줄)에 배치, 하단 중복 날짜 제거
        // user-input-required 클래스로 카드 테두리 색 차별화
        const userInputClass = feedback.requiresUserInput ? ' user-input-required' : '';
        const borderColor = feedback.requiresUserInput ? '#e91e63' : color;

        return /* html */`
<div class="card${userInputClass}"
     data-id="${this.esc(spec.id)}"
     data-title="${this.esc(title)}"
     data-tags="${this.esc((spec.tags || []).join(' '))}"
     style="border-left: 3px solid ${borderColor};"
     tabindex="0"
     role="button">
  <div class="card-id">${this.esc(spec.id)}<span style="margin-left:6px;font-size:10px;opacity:0.45;">${date}</span></div>
  <div class="card-title">${this.esc(title)}</div>
  ${userInputBadge}
  ${openQuestionInfo}
  ${progressHtml}
  ${reviewBadge}
  ${reviewNote}
  <div class="card-footer">
    <div class="card-actions">
      <button class="action-btn" data-action="open" title="파일 열기">↗</button>
    </div>
  </div>
  <div style="margin-top:6px;">
    <select class="status-select" title="상태 변경">
      ${statusOptions}
    </select>
  </div>
</div>`;
    }

    private esc(s: string): string {
        return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    private getErrorHtml(msg: string): string {
        return `<!DOCTYPE html><html><body style="background:var(--vscode-editor-background);color:var(--vscode-editor-foreground);padding:20px;">
<h3>오류</h3><p>${msg}</p></body></html>`;
    }
}
