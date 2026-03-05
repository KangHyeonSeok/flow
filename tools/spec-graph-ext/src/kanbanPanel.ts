/**
 * KanbanPanel - 스펙 상태별 칸반 보드 Webview 패널
 *
 * 스펙을 상태(draft / active / needs-review / verified / deprecated)에 따라
 * 칸반 컬럼으로 구분하여 표시한다. 카드 클릭 시 상세 패널 열림.
 */
import * as vscode from 'vscode';
import { SpecLoader } from './specLoader';
import { Spec, SpecStatus, STATUS_COLORS } from './types';

const COLUMNS: { status: SpecStatus; label: string; icon: string }[] = [
    { status: 'draft',             label: 'Draft',             icon: '○' },
    { status: 'requested',         label: '개발 요청',          icon: '→' },
    { status: 'context-gathering', label: '정보 수집',          icon: '🔍' },
    { status: 'plan',              label: '계획',              icon: '📝' },
    { status: 'active',            label: 'Active',            icon: '●' },
    { status: 'needs-review',      label: 'Needs Review',      icon: '⚠' },
    { status: 'verified',          label: 'Verified',          icon: '✔' },
    { status: 'deprecated',        label: 'Deprecated',        icon: '✕' },
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
                const { specId, newStatus } = msg as { type: string; specId: string; newStatus: SpecStatus };
                await this.changeSpecStatus(specId, newStatus);
                break;
            }
        }
    }

    /** 스펙 상태 변경 */
    private async changeSpecStatus(specId: string, newStatus: SpecStatus): Promise<void> {
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
            vscode.window.showInformationMessage(`"${specId}" 상태를 "${newStatus}"으로 변경했습니다.`);
        } catch (err) {
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
            'requested': [],
            'context-gathering': [],
            'plan': [],
            'active': [],
            'needs-review': [],
            'verified': [],
            'deprecated': [],
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

// ── 카드 클릭 → 상세 보기
document.getElementById('board').addEventListener('click', (e) => {
  const card = e.target.closest('.card');
  if (!card) { return; }
  // 버튼 클릭이면 무시 (버튼은 자체 handler)
  if (e.target.closest('.action-btn') || e.target.closest('.status-select')) { return; }
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
  } else if (action === 'detail') {
    vscode.postMessage({ type: 'selectSpec', specId: card.dataset.id });
  }
  e.stopPropagation();
});

// ── 상태 변경 select
document.getElementById('board').addEventListener('change', (e) => {
  const sel = e.target.closest('.status-select');
  if (!sel) { return; }
  const card = sel.closest('.card');
  if (!card) { return; }
  vscode.postMessage({ type: 'changeStatus', specId: card.dataset.id, newStatus: sel.value });
  e.stopPropagation();
});

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
        const tagsHtml = spec.tags && spec.tags.length > 0
            ? `<div class="card-tags">${spec.tags.map(t => `<span class="tag">${this.esc(t)}</span>`).join('')}</div>`
            : '';
        const date = spec.updatedAt ? spec.updatedAt.slice(0, 10) : '';
        const title = spec.title || spec.id;

        const statusOptions = COLUMNS.map(c =>
            `<option value="${c.status}"${c.status === spec.status ? ' selected' : ''}>${c.label}</option>`
        ).join('');

        return /* html */`
<div class="card"
     data-id="${this.esc(spec.id)}"
     data-title="${this.esc(title)}"
     data-tags="${this.esc((spec.tags || []).join(' '))}"
     style="border-left: 3px solid ${color};">
  <div class="card-id">${this.esc(spec.id)}</div>
  <div class="card-title">${this.esc(title)}</div>
  ${tagsHtml}
  <div class="card-footer">
    <span class="card-date">${date}</span>
    <div class="card-actions">
      <button class="action-btn" data-action="open" title="파일 열기">↗</button>
      <button class="action-btn" data-action="detail" title="상세 보기">☰</button>
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
