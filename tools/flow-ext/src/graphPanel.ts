/**
 * GraphPanel - Cytoscape.js 기반 스펙 그래프 Webview 패널
 *
 * Feature/Condition 노드를 시각화하고, 의존성/계층 엣지를 렌더링
 * 노드 클릭 시 상세 정보 표시 및 코드 참조 이동 지원
 */
import * as vscode from 'vscode';
import { SpecLoader } from './specLoader';
import { SpecGraph, GraphNode, STATUS_COLORS } from './types';

export class GraphPanel {
    public static currentPanel: GraphPanel | undefined;
    private static readonly viewType = 'specGraph.graphView';

    private readonly panel: vscode.WebviewPanel;
    private readonly extensionUri: vscode.Uri;
    private disposables: vscode.Disposable[] = [];

    private constructor(
        panel: vscode.WebviewPanel,
        extensionUri: vscode.Uri,
        private loader: SpecLoader,
        private workspaceRoot: string,
        private readonly isPrimaryPanel: boolean,
    ) {
        this.panel = panel;
        this.extensionUri = extensionUri;

        // 패널이 닫힐 때 정리
        this.panel.onDidDispose(() => this.dispose(), null, this.disposables);

        // Webview 메시지 수신
        this.panel.webview.onDidReceiveMessage(
            (msg) => this.handleMessage(msg),
            null,
            this.disposables,
        );

        // 스펙 변경 시 자동 새로고침
        this.loader.onDidChange(() => this.update());
    }

    /** 싱글톤 패널 생성 또는 포커스 */
    static createOrShow(
        extensionUri: vscode.Uri,
        loader: SpecLoader,
        workspaceRoot: string,
    ): GraphPanel {
        const column = vscode.ViewColumn.One;

        if (GraphPanel.currentPanel) {
            GraphPanel.currentPanel.panel.reveal(column);
            return GraphPanel.currentPanel;
        }

        const panel = vscode.window.createWebviewPanel(
            GraphPanel.viewType,
            'Spec Graph',
            column,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [extensionUri],
            },
        );

        GraphPanel.currentPanel = new GraphPanel(panel, extensionUri, loader, workspaceRoot, true);
        GraphPanel.currentPanel.update();
        return GraphPanel.currentPanel;
    }

    /** 별도 웹 렌더링 프리뷰 패널 생성 */
    static openPreview(
        extensionUri: vscode.Uri,
        loader: SpecLoader,
        workspaceRoot: string,
    ): GraphPanel {
        const panel = vscode.window.createWebviewPanel(
            `${GraphPanel.viewType}.preview`,
            'Spec Graph Web Preview',
            vscode.ViewColumn.Beside,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [extensionUri],
            },
        );

        const previewPanel = new GraphPanel(panel, extensionUri, loader, workspaceRoot, false);
        previewPanel.update();
        return previewPanel;
    }

    /** 특정 노드에 포커스 */
    focusNode(nodeId: string): void {
        this.panel.reveal();
        this.panel.webview.postMessage({ type: 'focusNode', nodeId });
    }

    /** 그래프 데이터 갱신 */
    async update(): Promise<void> {
        try {
            const graph = await this.loader.getGraph();
            this.panel.webview.html = this.getHtml(graph, !this.isPrimaryPanel);
        } catch (e) {
            this.panel.webview.html = this.getErrorHtml(`그래프 로드 실패: ${String(e)}`);
        }
    }

    /** Webview → Extension 메시지 핸들링 */
    private async handleMessage(msg: { type: string; [key: string]: unknown }): Promise<void> {
        switch (msg.type) {
            case 'openCodeRef': {
                const ref = msg.codeRef as string;
                await this.openCodeRef(ref);
                break;
            }
            case 'selectNode': {
                const nodeId = msg.nodeId as string;
                // 상세 패널 업데이트
                vscode.commands.executeCommand('specGraph.showDetail', nodeId);
                break;
            }
            case 'openSpec': {
                const specId = msg.specId as string;
                await this.openSpecFile(specId);
                break;
            }
            case 'saveEdits': {
                // C4: 편집 내용을 스펙 파일에 저장
                const ops = msg.ops as Array<{type: string; [key: string]: unknown}>;
                const result = await this.applyEditOps(ops);
                this.panel.webview.postMessage({ type: 'saveEditsResult', ok: result.ok, errors: result.errors });
                break;
            }
        }
    }

    /** codeRef 문자열을 에디터에서 열기 */
    private async openCodeRef(codeRef: string): Promise<void> {
        // "tools/flow-cli/Services/AuthService.cs#L20-L30" 파싱
        const parts = codeRef.split('#');
        const filePath = parts[0];
        const lineRange = parts[1]; // "L20-L30"

        const fullPath = vscode.Uri.file(
            require('path').join(this.workspaceRoot, filePath)
        );

        try {
            const doc = await vscode.workspace.openTextDocument(fullPath);
            let selection: vscode.Range | undefined;

            if (lineRange) {
                const match = lineRange.match(/L(\d+)(?:-L?(\d+))?/);
                if (match) {
                    const startLine = parseInt(match[1]) - 1;
                    const endLine = match[2] ? parseInt(match[2]) - 1 : startLine;
                    selection = new vscode.Range(startLine, 0, endLine, 0);
                }
            }

            await vscode.window.showTextDocument(doc, {
                selection,
                preview: true,
            });
        } catch {
            vscode.window.showWarningMessage(`파일을 찾을 수 없습니다: ${filePath}`);
        }
    }

    /** 스펙 JSON 파일 열기 */
    private async openSpecFile(specId: string): Promise<void> {
        const filePath = vscode.Uri.file(
            require('path').join(this.loader.specsDirectory, `${specId}.json`)
        );
        try {
            const doc = await vscode.workspace.openTextDocument(filePath);
            await vscode.window.showTextDocument(doc, { preview: true });
        } catch {
            vscode.window.showWarningMessage(`스펙 파일을 찾을 수 없습니다: ${specId}.json`);
        }
    }

    /** Webview HTML 생성 */
    private getHtml(graph: SpecGraph, isPreview: boolean): string {
        const nonce = getNonce();

        // Cytoscape.js 로컬 파일 URI
        const cytoscapeUri = this.panel.webview.asWebviewUri(
            vscode.Uri.joinPath(this.extensionUri, 'dist', 'cytoscape.min.js')
        );

        // Cytoscape.js 노드/엣지 데이터 구성
        const elements = this.buildCytoscapeElements(graph);

        return /*html*/ `<!DOCTYPE html>
<html lang="ko">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta http-equiv="Content-Security-Policy"
          content="default-src 'none';
                   style-src 'unsafe-inline';
                   script-src 'nonce-${nonce}' ${this.panel.webview.cspSource};
                   img-src ${this.panel.webview.cspSource} data:;
                   font-src ${this.panel.webview.cspSource};">
    <title>Spec Graph</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: var(--vscode-font-family, sans-serif);
            background: var(--vscode-editor-background, #1e1e1e);
            color: var(--vscode-editor-foreground, #d4d4d4);
            overflow: hidden;
            height: 100vh;
            display: flex;
            flex-direction: column;
        }

        /* 툴바 */
        .toolbar {
            display: flex;
            align-items: center;
            gap: 8px;
            padding: 8px 12px;
            background: var(--vscode-sideBar-background, #252526);
            border-bottom: 1px solid var(--vscode-panel-border, #3c3c3c);
            flex-shrink: 0;
        }
        .toolbar button {
            background: var(--vscode-button-background, #0e639c);
            color: var(--vscode-button-foreground, #fff);
            border: none;
            padding: 4px 10px;
            border-radius: 3px;
            cursor: pointer;
            font-size: 12px;
        }
        .toolbar button:hover {
            background: var(--vscode-button-hoverBackground, #1177bb);
        }
        .toolbar button.secondary {
            background: var(--vscode-button-secondaryBackground, #3a3d41);
            color: var(--vscode-button-secondaryForeground, #ccc);
        }
        .toolbar select {
            background: var(--vscode-input-background, #3c3c3c);
            color: var(--vscode-input-foreground, #ccc);
            border: 1px solid var(--vscode-input-border, #555);
            padding: 3px 6px;
            border-radius: 3px;
            font-size: 12px;
        }
        .toolbar .separator {
            width: 1px;
            height: 20px;
            background: var(--vscode-panel-border, #3c3c3c);
        }
        .toolbar label {
            font-size: 12px;
            color: var(--vscode-descriptionForeground, #888);
        }

        /* 메인 컨텐츠 */
        .main {
            display: flex;
            flex: 1;
            overflow: hidden;
        }

        /* 그래프 영역 */
        #cy {
            flex: 1;
            min-width: 0;
        }

        /* 상세 패널 */
        .detail-panel {
            width: 320px;
            background: var(--vscode-sideBar-background, #252526);
            border-left: 1px solid var(--vscode-panel-border, #3c3c3c);
            overflow-y: auto;
            padding: 12px;
            flex-shrink: 0;
            display: none;
        }
        .detail-panel.visible { display: block; }

        .detail-panel h2 {
            font-size: 14px;
            margin-bottom: 8px;
            color: var(--vscode-foreground, #e0e0e0);
        }
        .detail-panel h3 {
            font-size: 12px;
            margin: 12px 0 4px 0;
            color: var(--vscode-descriptionForeground, #888);
            text-transform: uppercase;
            letter-spacing: 0.5px;
        }
        .detail-panel .status-badge {
            display: inline-block;
            padding: 2px 8px;
            border-radius: 10px;
            font-size: 11px;
            font-weight: 600;
            margin-bottom: 8px;
        }
        .detail-panel .description {
            font-size: 13px;
            line-height: 1.5;
            margin-bottom: 8px;
        }
        .detail-panel .code-ref {
            display: block;
            font-size: 12px;
            color: var(--vscode-textLink-foreground, #3794ff);
            cursor: pointer;
            padding: 2px 0;
            text-decoration: none;
        }
        .detail-panel .code-ref:hover {
            text-decoration: underline;
        }
        .detail-panel .tag {
            display: inline-block;
            padding: 1px 6px;
            border-radius: 3px;
            font-size: 11px;
            margin-right: 4px;
            background: var(--vscode-badge-background, #4d4d4d);
            color: var(--vscode-badge-foreground, #ccc);
        }
        .detail-panel .condition-item {
            padding: 6px 8px;
            margin: 4px 0;
            border-radius: 4px;
            background: var(--vscode-editor-background, #1e1e1e);
            font-size: 12px;
        }
        .detail-panel .condition-status {
            font-size: 10px;
            font-weight: 600;
            margin-right: 4px;
        }
        .detail-panel .close-btn {
            float: right;
            background: none;
            border: none;
            color: var(--vscode-foreground, #ccc);
            cursor: pointer;
            font-size: 16px;
        }
        .detail-panel .btn-open-file {
            display: inline-block;
            margin-top: 8px;
            padding: 3px 10px;
            background: var(--vscode-button-secondaryBackground, #3a3d41);
            color: var(--vscode-button-secondaryForeground, #ccc);
            border: none;
            border-radius: 3px;
            cursor: pointer;
            font-size: 12px;
        }
        /* F-021-C5: 관계 요약 스타일 */
        .detail-panel .relation-summary {
            background: var(--vscode-editor-background, #1e1e1e);
            border-radius: 4px;
            padding: 6px 8px;
            margin-bottom: 8px;
        }
        .detail-panel .relation-row {
            font-size: 12px;
            margin: 3px 0;
        }
        .detail-panel .rel-label {
            display: inline-block;
            min-width: 100px;
            font-weight: 600;
        }
        .detail-panel .supersedes-row .rel-label { color: #f44336; }
        .detail-panel .supersededby-row .rel-label { color: #ff7043; }
        .detail-panel .mutates-row .rel-label { color: #ff9800; }
        .detail-panel .mutatedby-row .rel-label { color: #ffc107; }
        .detail-panel .spec-link {
            display: inline;
            color: var(--vscode-textLink-foreground, #3794ff);
            cursor: pointer;
            text-decoration: underline;
        }
        /* F-021-C1: 변경 이력 스타일 */
        .detail-panel .changelog-list {
            font-size: 11px;
        }
        .detail-panel .changelog-entry {
            padding: 4px 0;
            border-bottom: 1px solid var(--vscode-widget-border, #333);
        }
        .detail-panel .changelog-type {
            display: inline-block;
            padding: 1px 5px;
            border-radius: 3px;
            font-size: 10px;
            font-weight: 600;
        }
        .detail-panel .changelog-type-create { background: #1b5e20; color: #a5d6a7; }
        .detail-panel .changelog-type-mutate { background: #e65100; color: #ffccbc; }
        .detail-panel .changelog-type-supersede { background: #b71c1c; color: #ffcdd2; }
        .detail-panel .changelog-type-deprecate { background: #4e342e; color: #d7ccc8; }
        .detail-panel .changelog-type-restore { background: #1a237e; color: #c5cae9; }

        /* 범례 */
        .legend {
            display: flex;
            gap: 12px;
            align-items: center;
        }
        .legend-item {
            display: flex;
            align-items: center;
            gap: 4px;
            font-size: 11px;
        }
        .legend-dot {
            width: 10px;
            height: 10px;
            border-radius: 50%;
        }
        /* C4/C5: 편집 모드 스타일 */
        .toolbar button.edit-active {
            background: var(--vscode-inputValidation-warningBorder, #d7a85d);
            color: #fff;
        }
        .edit-mode-indicator {
            display: none;
            font-size: 11px;
            color: var(--vscode-inputValidation-warningBorder, #d7a85d);
            font-weight: 600;
        }
        /* Modal overlay */
        .edit-modal-overlay {
            display: none;
            position: fixed;
            top: 0; left: 0; right: 0; bottom: 0;
            background: rgba(0,0,0,0.55);
            z-index: 1000;
            align-items: center;
            justify-content: center;
        }
        .edit-modal-overlay.visible { display: flex; }
        .edit-modal {
            background: var(--vscode-sideBar-background, #252526);
            border: 1px solid var(--vscode-panel-border, #555);
            border-radius: 6px;
            padding: 16px;
            min-width: 320px;
            max-width: 480px;
            box-shadow: 0 4px 20px rgba(0,0,0,0.5);
        }
        .edit-modal h3 {
            font-size: 14px;
            margin: 0 0 12px 0;
            color: var(--vscode-foreground, #d4d4d4);
        }
        .edit-modal label {
            display: block;
            font-size: 12px;
            margin-bottom: 8px;
            color: var(--vscode-descriptionForeground, #888);
        }
        .edit-modal input, .edit-modal select, .edit-modal textarea {
            width: 100%;
            background: var(--vscode-input-background, #3c3c3c);
            color: var(--vscode-input-foreground, #ccc);
            border: 1px solid var(--vscode-input-border, #555);
            padding: 4px 8px;
            border-radius: 3px;
            font-size: 12px;
            margin-top: 3px;
            box-sizing: border-box;
        }
        .edit-modal textarea { min-height: 60px; resize: vertical; font-family: inherit; }
        .edit-modal .modal-actions {
            display: flex;
            gap: 8px;
            justify-content: flex-end;
            margin-top: 12px;
        }
        .edit-modal .btn-primary {
            background: var(--vscode-button-background, #0e639c);
            color: var(--vscode-button-foreground, #fff);
            border: none;
            padding: 5px 12px;
            border-radius: 3px;
            cursor: pointer;
            font-size: 12px;
        }
        .edit-modal .btn-cancel {
            background: var(--vscode-button-secondaryBackground, #3a3d41);
            color: var(--vscode-button-secondaryForeground, #ccc);
            border: none;
            padding: 5px 12px;
            border-radius: 3px;
            cursor: pointer;
            font-size: 12px;
        }
        /* edge-source highlight */
        .edge-source-highlight {
            border-width: 4px !important;
            border-color: #ffeb3b !important;
        }
    </style>
</head>
<body>
    <div class="toolbar">
        <button id="btnFit" title="전체 보기">⊞ Fit</button>
        <button id="btnZoomIn" class="secondary" title="확대">+</button>
        <button id="btnZoomOut" class="secondary" title="축소">−</button>
        <div class="separator"></div>
        <label for="selLayout">레이아웃:</label>
        <select id="selLayout">
            <option value="cose" selected>CoSE</option>
            <option value="breadthfirst">Breadthfirst</option>
            <option value="dagre">Dagre (계층)</option>
            <option value="circle">Circle</option>
            <option value="concentric">Concentric</option>
        </select>
        <div class="separator"></div>
        <label for="selStatus">상태:</label>
        <select id="selStatus">
            <option value="">전체</option>
            <option value="draft">초안</option>
            <option value="queued">대기중</option>
            <option value="working">작업중</option>
            <option value="needs-review">검토 대기</option>
            <option value="verified">검증 완료</option>
            <option value="deprecated">폐기</option>
            <option value="done">완료</option>
        </select>
        <div class="separator"></div>
        <label>
            <input type="checkbox" id="chkConditions" checked> 조건 표시
        </label>
        <div class="separator"></div>
        ${isPreview ? '<span style="font-size:11px;color:var(--vscode-descriptionForeground,#888)">Web Preview</span><div class="separator"></div>' : ''}
        <span style="font-size:11px;color:var(--vscode-descriptionForeground,#888)">nodes: ${graph.nodes.length}, edges: ${graph.edges.length}</span>
        <div class="separator"></div>
        <div class="legend">
            <div class="legend-item"><div class="legend-dot" style="background:#9e9e9e"></div> 초안</div>
            <div class="legend-item"><div class="legend-dot" style="background:#9c27b0"></div> 대기중</div>
            <div class="legend-item"><div class="legend-dot" style="background:#2196f3"></div> 작업중</div>
            <div class="legend-item"><div class="legend-dot" style="background:#ff9800"></div> 검토 대기</div>
            <div class="legend-item"><div class="legend-dot" style="background:#4caf50"></div> 검증 완료</div>
            <div class="legend-item"><div class="legend-dot" style="background:#795548"></div> 완료</div>
            <div class="legend-item"><div class="legend-dot" style="background:#f44336"></div> 폐기</div>
        </div>
        <div class="separator"></div>
        <span class="edit-mode-indicator" id="editModeLabel" style="display:none">✏ 편집 모드</span>
        <button id="btnToggleEdit" class="secondary" title="편집 모드 전환 (C4)">✏</button>
        <button id="btnUndo" class="secondary" title="실행 취소 Ctrl+Z (C5)" disabled style="display:none">↩ Undo</button>
        <button id="btnRedo" class="secondary" title="다시 실행 Ctrl+Y (C5)" disabled style="display:none">↪ Redo</button>
        <button id="btnAddNode" class="secondary" title="노드 추가" style="display:none">+ 노드</button>
        <button id="btnDeleteNode" class="secondary" title="선택 노드 삭제 (Delete)" style="display:none">🗑</button>
        <button id="btnAddEdge" class="secondary" title="선택 노드→클릭 노드 의존성 추가" style="display:none">→ 의존</button>
        <button id="btnSaveEdits" class="secondary" title="편집 내용을 스펙 파일에 저장" style="display:none">💾 저장</button>
    </div>

    <div class="main">
        <div id="cy"></div>
        <div class="detail-panel" id="detailPanel">
            <button class="close-btn" id="btnCloseDetail">✕</button>
            <div id="detailContent"></div>
        </div>
    </div>

    <script src="${cytoscapeUri}"></script>
    <script nonce="${nonce}">
        const vscode = acquireVsCodeApi();

        // ─── 그래프 데이터 ───
        const elements = ${JSON.stringify(elements)};
        const graphData = ${JSON.stringify({
            nodes: graph.nodes,
            specs: graph.specs,
        })};

        const statusColors = ${JSON.stringify(STATUS_COLORS)};

        // ─── Cytoscape 초기화 ───
        const cyContainer = document.getElementById('cy');
        function renderGraphError(message) {
            cyContainer.innerHTML = '<div style="padding:16px;color:var(--vscode-errorForeground,#f48771);font-size:12px;line-height:1.5">'
                + message + '<br><span style="color:var(--vscode-descriptionForeground,#888)">개발자 도구 콘솔에서 상세 로그를 확인하세요.</span></div>';
        }

        if (typeof cytoscape !== 'function') {
            renderGraphError('Cytoscape 로딩에 실패했습니다. dist/cytoscape.min.js 포함 여부를 확인하세요.');
            throw new Error('cytoscape is not available in webview runtime');
        }

        // ─── C6: 대규모 그래프 청크 로딩 (500+ 노드 지원) ───
        const _CHUNK = 50;
        const _totalElems = elements.length;
        const _firstChunk = _totalElems > _CHUNK ? elements.slice(0, _CHUNK) : elements;
        const _restElems  = _totalElems > _CHUNK ? elements.slice(_CHUNK) : [];

        let cy;
        try {
            cy = cytoscape({
                container: cyContainer,
                elements: _firstChunk,
                style: [
                // Feature 노드
                {
                    selector: 'node[nodeType="feature"]',
                    style: {
                        'label': 'data(label)',
                        'text-valign': 'bottom',
                        'text-halign': 'center',
                        'font-size': '11px',
                        'color': '#d4d4d4',
                        'text-margin-y': 6,
                        'background-color': 'data(color)',
                        'border-width': 3,
                        'border-color': 'data(borderColor)',
                        'width': 50,
                        'height': 50,
                        'shape': 'round-rectangle',
                        'text-wrap': 'ellipsis',
                        'text-max-width': '100px',
                    }
                },
                // Condition 노드
                {
                    selector: 'node[nodeType="condition"]',
                    style: {
                        'label': 'data(shortLabel)',
                        'text-valign': 'bottom',
                        'text-halign': 'center',
                        'font-size': '9px',
                        'color': '#999',
                        'text-margin-y': 4,
                        'background-color': 'data(color)',
                        'border-width': 1,
                        'border-color': 'data(borderColor)',
                        'width': 24,
                        'height': 24,
                        'shape': 'ellipse',
                    }
                },
                // Parent 엣지 (실선)
                {
                    selector: 'edge[type="parent"]',
                    style: {
                        'line-color': '#555',
                        'target-arrow-color': '#555',
                        'target-arrow-shape': 'triangle',
                        'curve-style': 'bezier',
                        'width': 2,
                        'arrow-scale': 0.8,
                    }
                },
                // Dependency 엣지 (점선, 파란색)
                {
                    selector: 'edge[type="dependency"]',
                    style: {
                        'line-color': '#2196f3',
                        'target-arrow-color': '#2196f3',
                        'target-arrow-shape': 'triangle',
                        'curve-style': 'bezier',
                        'line-style': 'dashed',
                        'width': 2,
                        'arrow-scale': 0.8,
                    }
                },
                // Condition 엣지 (가는 실선)
                {
                    selector: 'edge[type="condition"]',
                    style: {
                        'line-color': '#444',
                        'target-arrow-color': '#444',
                        'target-arrow-shape': 'none',
                        'curve-style': 'bezier',
                        'width': 1,
                    }
                },
                // F-021-C5: Supersedes 엣지 (빨간 점선 — 대체 관계)
                {
                    selector: 'edge[type="supersedes"]',
                    style: {
                        'line-color': '#f44336',
                        'target-arrow-color': '#f44336',
                        'target-arrow-shape': 'triangle',
                        'curve-style': 'bezier',
                        'line-style': 'dashed',
                        'width': 2,
                        'arrow-scale': 0.9,
                        'label': 'supersedes',
                        'font-size': '9px',
                        'color': '#f44336',
                        'text-background-color': '#1e1e1e',
                        'text-background-opacity': 0.7,
                        'text-background-padding': '2px',
                    } as Record<string, unknown>
                },
                // F-021-C5: Mutates 엣지 (주황 점선 — 변형 관계)
                {
                    selector: 'edge[type="mutates"]',
                    style: {
                        'line-color': '#ff9800',
                        'target-arrow-color': '#ff9800',
                        'target-arrow-shape': 'vee',
                        'curve-style': 'bezier',
                        'line-style': 'dotted',
                        'width': 2,
                        'arrow-scale': 0.9,
                        'label': 'mutates',
                        'font-size': '9px',
                        'color': '#ff9800',
                        'text-background-color': '#1e1e1e',
                        'text-background-opacity': 0.7,
                        'text-background-padding': '2px',
                    } as Record<string, unknown>
                },
                // 선택됨
                {
                    selector: ':selected',
                    style: {
                        'overlay-color': '#fff',
                        'overlay-padding': 4,
                        'overlay-opacity': 0.2,
                    }
                },
                // 강조 (검색/포커스)
                {
                    selector: '.highlighted',
                    style: {
                        'border-width': 4,
                        'border-color': '#ffeb3b',
                        'overlay-color': '#ffeb3b',
                        'overlay-padding': 6,
                        'overlay-opacity': 0.15,
                        'z-index': 999,
                    }
                },
                // 흐림 (필터링 시 비관련 노드)
                {
                    selector: '.dimmed',
                    style: {
                        'opacity': 0.2,
                    }
                },
            ],
                layout: { name: 'preset' },
                minZoom: 0.2,
                maxZoom: 4,
                wheelSensitivity: 0.45,
                textureOnViewport: _totalElems > 100,
            });
        } catch (e) {
            console.error('[SpecGraph] Cytoscape init failed', e);
            renderGraphError('그래프 초기화에 실패했습니다. 데이터 포맷과 레이아웃 설정을 확인하세요.');
            throw e;
        }

        // C6: 청크 로딩 완료 후 레이아웃 실행
        function runInitialLayout() {
            const selLayout = document.getElementById('selLayout');
            const layoutName = selLayout ? selLayout.value || 'cose' : 'cose';
            cy.layout(getLayoutOptions(layoutName)).run();
        }
        if (_restElems.length > 0) {
            let _chunkIdx = 0;
            function _loadNextChunk() {
                const start = _chunkIdx * _CHUNK;
                const end = Math.min(start + _CHUNK, _restElems.length);
                const chunk = _restElems.slice(start, end);
                if (chunk.length > 0) {
                    cy.batch(function() { cy.add(chunk); });
                    _chunkIdx++;
                }
                if (_chunkIdx * _CHUNK < _restElems.length) {
                    requestAnimationFrame(_loadNextChunk);
                } else {
                    runInitialLayout();
                }
            }
            requestAnimationFrame(_loadNextChunk);
        } else {
            runInitialLayout();
        }

        // ─── 노드 클릭 이벤트 ───
        cy.on('tap', 'node', function(evt) {
            const node = evt.target;
            const id = node.data('id');
            // C4: 의존성 드래그&드롭 — 엣지 그리기 모드
            if (window._editEdgeSource) {
                if (id !== window._editEdgeSource && window._onEditEdgeTarget) {
                    window._onEditEdgeTarget(window._editEdgeSource, id);
                }
                window._clearEditEdgeSource && window._clearEditEdgeSource();
                return;
            }
            showDetail(id);
            vscode.postMessage({ type: 'selectNode', nodeId: id });
        });

        // 빈 공간 클릭 시 상세 패널 닫기 + 필터 해제
        cy.on('tap', function(evt) {
            if (evt.target === cy) {
                hideDetail();
                clearFocusFilter();
            }
        });

        // ─── 상세 패널 ───
        function showDetail(nodeId) {
            const nodeData = graphData.nodes.find(n => n.id === nodeId);
            if (!nodeData) return;

            const panel = document.getElementById('detailPanel');
            const content = document.getElementById('detailContent');
            panel.classList.add('visible');

            const spec = graphData.specs.find(s => s.id === nodeId);
            const isSpecNode = nodeData.nodeType === 'feature' || nodeData.nodeType === 'task';

            let html = '<h2>' + escapeHtml(nodeData.id + (isSpecNode ? ': ' + nodeData.label : '')) + '</h2>';

            // 상태 배지
            const color = statusColors[nodeData.status] || '#888';
            html += '<span class="status-badge" style="background:' + color + ';color:#fff">'
                  + nodeData.status + '</span>';

            // 설명
            html += '<div class="description">' + escapeHtml(nodeData.description) + '</div>';

            // 태그
            if (nodeData.tags && nodeData.tags.length > 0) {
                html += '<h3>Tags</h3>';
                html += nodeData.tags.map(t => '<span class="tag">' + escapeHtml(t) + '</span>').join('');
            }

            // codeRefs
            if (nodeData.codeRefs && nodeData.codeRefs.length > 0) {
                html += '<h3>Code References</h3>';
                for (const ref of nodeData.codeRefs) {
                    html += '<a class="code-ref" data-ref="' + escapeAttr(ref) + '">'
                          + escapeHtml(ref) + '</a>';
                }
            }

            // Conditions (feature only)
            if (spec && spec.conditions && spec.conditions.length > 0) {
                html += '<h3>Conditions (' + spec.conditions.length + ')</h3>';
                for (const c of spec.conditions) {
                    const cColor = statusColors[c.status] || '#888';
                    html += '<div class="condition-item">'
                          + '<span class="condition-status" style="color:' + cColor + '">● ' + c.status + '</span> '
                          + '<strong>' + escapeHtml(c.id) + '</strong><br>'
                          + '<span style="font-size:11px;color:#aaa">' + escapeHtml(c.description) + '</span>';
                    if (c.codeRefs && c.codeRefs.length > 0) {
                        for (const ref of c.codeRefs) {
                            html += '<br><a class="code-ref" data-ref="' + escapeAttr(ref) + '">'
                                  + escapeHtml(ref) + '</a>';
                        }
                    }
                    html += '</div>';
                }
            }

            // F-021-C5: 관계 요약 (supersedes/mutates/supersededBy/mutatedBy)
            if (spec) {
                const hasRelations = (spec.supersedes && spec.supersedes.length > 0)
                    || (spec.supersededBy && spec.supersededBy.length > 0)
                    || (spec.mutates && spec.mutates.length > 0)
                    || (spec.mutatedBy && spec.mutatedBy.length > 0);
                if (hasRelations) {
                    html += '<h3>🔗 Relationships</h3>';
                    html += '<div class="relation-summary">';
                    if (spec.supersedes && spec.supersedes.length > 0) {
                        html += '<div class="relation-row supersedes-row">'
                              + '<span class="rel-label">↠ supersedes</span> '
                              + spec.supersedes.map(id => '<a class="spec-link" data-spec-id="' + escapeAttr(id) + '">' + escapeHtml(id) + '</a>').join(', ')
                              + '</div>';
                    }
                    if (spec.supersededBy && spec.supersededBy.length > 0) {
                        html += '<div class="relation-row supersededby-row">'
                              + '<span class="rel-label">⇝ supersededBy</span> '
                              + spec.supersededBy.map(id => '<a class="spec-link" data-spec-id="' + escapeAttr(id) + '">' + escapeHtml(id) + '</a>').join(', ')
                              + ' <span style="color:#f44336;font-size:10px">⚠ 이 스펙은 대체됨</span>'
                              + '</div>';
                    }
                    if (spec.mutates && spec.mutates.length > 0) {
                        html += '<div class="relation-row mutates-row">'
                              + '<span class="rel-label">⟳ mutates</span> '
                              + spec.mutates.map(id => '<a class="spec-link" data-spec-id="' + escapeAttr(id) + '">' + escapeHtml(id) + '</a>').join(', ')
                              + '</div>';
                    }
                    if (spec.mutatedBy && spec.mutatedBy.length > 0) {
                        html += '<div class="relation-row mutatedby-row">'
                              + '<span class="rel-label">⟲ mutatedBy</span> '
                              + spec.mutatedBy.map(id => '<a class="spec-link" data-spec-id="' + escapeAttr(id) + '">' + escapeHtml(id) + '</a>').join(', ')
                              + '</div>';
                    }
                    // 권장 후속 조치
                    if (spec.supersededBy && spec.supersededBy.length > 0 && spec.status !== 'deprecated') {
                        html += '<div style="margin-top:6px;color:#ff9800;font-size:11px">'
                              + '권장: 대체 스펙 검토 후 deprecated 전환 고려</div>';
                    }
                    html += '</div>';
                }

                // 변경 이력 (최근 3개)
                if (spec.changeLog && spec.changeLog.length > 0) {
                    html += '<h3>📋 Change Log</h3>';
                    html += '<div class="changelog-list">';
                    const recent = spec.changeLog.slice(-3);
                    for (const entry of recent) {
                        const at = entry.at ? entry.at.substring(0, 10) : '';
                        html += '<div class="changelog-entry">'
                              + '<span class="changelog-type changelog-type-' + escapeAttr(entry.type) + '">' + escapeHtml(entry.type) + '</span> '
                              + '<span style="color:#888;font-size:10px">' + escapeHtml(at) + ' · ' + escapeHtml(entry.author) + '</span><br>'
                              + '<span style="font-size:11px">' + escapeHtml(entry.summary) + '</span>'
                              + '</div>';
                    }
                    if (spec.changeLog.length > 3) {
                        html += '<div style="color:#888;font-size:10px">... 및 ' + (spec.changeLog.length - 3) + '개 이전 항목</div>';
                    }
                    html += '</div>';
                }
            }

            // 스펙 파일 열기 버튼
            const fileId = isSpecNode ? nodeId : nodeData.featureId;
            if (fileId) {
                html += '<button class="btn-open-file" data-spec-id="' + escapeAttr(fileId) + '">'
                      + '📄 ' + fileId + '.json 열기</button>';
            }

            content.innerHTML = html;

            content.querySelectorAll('.code-ref').forEach(el => {
                el.addEventListener('click', () => {
                    const ref = el.getAttribute('data-ref');
                    if (ref) {
                        openCodeRef(ref);
                    }
                });
            });

            content.querySelectorAll('.spec-link').forEach(el => {
                el.addEventListener('click', () => {
                    const specId = el.getAttribute('data-spec-id');
                    if (specId) {
                        // 그래프에서 해당 노드로 포커스 이동
                        const node = cy.getElementById(specId);
                        if (node && node.length > 0) {
                            cy.fit(node, 80);
                            node.select();
                            showDetail(specId);
                        }
                    }
                });
            });

            const openBtn = content.querySelector('.btn-open-file');
            if (openBtn) {
                openBtn.addEventListener('click', () => {
                    const specId = openBtn.getAttribute('data-spec-id');
                    if (specId) {
                        openSpecFile(specId);
                    }
                });
            }
        }

        function hideDetail() {
            document.getElementById('detailPanel').classList.remove('visible');
        }

        document.getElementById('btnCloseDetail').addEventListener('click', hideDetail);

        // ─── 코드 참조 열기 ───
        function openCodeRef(ref) {
            vscode.postMessage({ type: 'openCodeRef', codeRef: ref });
        }
        // 전역 함수로 등록 (onclick에서 사용)
        window.openCodeRef = openCodeRef;

        function openSpecFile(specId) {
            vscode.postMessage({ type: 'openSpec', specId: specId });
        }
        window.openSpecFile = openSpecFile;

        // ─── 툴바 이벤트 ───
        document.getElementById('btnFit').addEventListener('click', () => {
            cy.fit(undefined, 30);
        });

        document.getElementById('btnZoomIn').addEventListener('click', () => {
            cy.zoom({ level: cy.zoom() * 1.3, renderedPosition: { x: cy.width() / 2, y: cy.height() / 2 } });
        });

        document.getElementById('btnZoomOut').addEventListener('click', () => {
            cy.zoom({ level: cy.zoom() / 1.3, renderedPosition: { x: cy.width() / 2, y: cy.height() / 2 } });
        });

        // 레이아웃 변경
        document.getElementById('selLayout').addEventListener('change', function() {
            const layoutName = this.value;
            const layoutOpts = getLayoutOptions(layoutName);
            cy.layout(layoutOpts).run();
        });

        function getLayoutOptions(name) {
            const base = { animate: true, animationDuration: 500, padding: 30 };
            switch (name) {
                case 'cose':
                    return { ...base, name: 'cose', nodeRepulsion: () => 8000, idealEdgeLength: () => 80, gravity: 0.3 };
                case 'breadthfirst':
                    return { ...base, name: 'breadthfirst', directed: true, spacingFactor: 1.2 };
                case 'circle':
                    return { ...base, name: 'circle' };
                case 'concentric':
                    return { ...base, name: 'concentric', concentric: (n) => n.data('nodeType') === 'feature' ? 2 : 1, levelWidth: () => 1 };
                case 'dagre':
                    // Fallback to breadthfirst with hierarchy feel
                    return { ...base, name: 'breadthfirst', directed: true, spacingFactor: 1.5, avoidOverlap: true };
                default:
                    return { ...base, name: name };
            }
        }

        // 상태 필터
        document.getElementById('selStatus').addEventListener('change', function() {
            const status = this.value;
            if (!status) {
                cy.nodes().removeClass('dimmed');
            } else {
                cy.nodes().forEach(n => {
                    if (n.data('status') === status) {
                        n.removeClass('dimmed');
                    } else {
                        n.addClass('dimmed');
                    }
                });
            }
        });

        // 조건 표시 토글
        document.getElementById('chkConditions').addEventListener('change', function() {
            const show = this.checked;
            cy.nodes('[nodeType="condition"]').forEach(n => {
                n.style('display', show ? 'element' : 'none');
            });
            cy.edges('[type="condition"]').forEach(e => {
                e.style('display', show ? 'element' : 'none');
            });
        });

        // ─── 포커스 필터 상태 ───
        let _focusActive = false;

        function applyFocusFilter(nodeId) {
            _focusActive = true;
            const focused = cy.getElementById(nodeId);
            if (!focused || focused.length === 0) return;

            const parentEdges = cy.edges('[type="parent"]');

            // 선택 노드가 target인 엣지의 source = 상위 스펙
            const parentNodes = parentEdges.filter(e => e.target().id() === nodeId).sources();
            // 선택 노드가 source인 엣지의 target = 하위 스펙
            const childNodes  = parentEdges.filter(e => e.source().id() === nodeId).targets();

            // 각 feature 노드의 condition 노드
            function getConditionNodes(featureCol) {
                return cy.edges('[type="condition"]')
                    .filter(e => featureCol.map(n => n.id()).includes(e.source().id()))
                    .targets();
            }

            const visibleNodes = cy.collection()
                .union(focused)
                .union(parentNodes)
                .union(childNodes)
                .union(getConditionNodes(focused))
                .union(getConditionNodes(parentNodes))
                .union(getConditionNodes(childNodes));

            const visibleEdges = visibleNodes.edgesWith(visibleNodes);

            cy.elements().hide();
            visibleNodes.show();
            visibleEdges.show();

            cy.fit(visibleNodes.union(visibleEdges), 40);

            // 하이라이트
            cy.elements().removeClass('highlighted');
            focused.addClass('highlighted');
        }

        function clearFocusFilter() {
            if (!_focusActive) return;
            _focusActive = false;
            cy.elements().show();
            // 조건 표시 체크박스 상태 반영
            const showConditions = document.getElementById('chkConditions').checked;
            if (!showConditions) {
                cy.nodes('[nodeType="condition"]').hide();
                cy.edges('[type="condition"]').hide();
            }
            cy.elements().removeClass('highlighted');
        }

        // ─── Extension → Webview 메시지 수신 ───
        window.addEventListener('message', function(event) {
            const msg = event.data;
            if (msg.type === 'focusNode') {
                applyFocusFilter(msg.nodeId);
                showDetail(msg.nodeId);
            } else if (msg.type === 'saveEditsResult') {
                // C4: 저장 결과 처리
                if (msg.ok) {
                    if (window._editSession) {
                        window._editSession.pendingOps = [];
                        window._editSession.undoStack = [];
                        window._editSession.redoStack = [];
                        if (window._updateUndoRedoButtons) { window._updateUndoRedoButtons(); }
                    }
                    alert('저장 완료. 스펙 파일이 업데이트되었습니다.');
                } else {
                    alert('저장 실패:\n' + (msg.errors || []).join('\n'));
                }
            }
        });

        // ─── F키: 선택된 노드에 포커싱 ───
        document.addEventListener('keydown', function(e) {
            if (e.key !== 'f' && e.key !== 'F') return;
            // 입력 필드에서는 무시
            const tag = document.activeElement && document.activeElement.tagName;
            if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') return;

            const selected = cy.$(':selected');
            if (selected.length > 0) {
                cy.animate({
                    fit: { eles: selected, padding: 80 },
                }, { duration: 350 });
            }
        });

        // ─── 유틸 ───
        function escapeHtml(str) {
            if (!str) return '';
            return str.replace(/&/g, '&amp;')
                      .replace(/</g, '&lt;')
                      .replace(/>/g, '&gt;')
                      .replace(/"/g, '&quot;')
                      .replace(/'/g, '&#039;');
        }

        function escapeAttr(str) {
            return escapeHtml(str);
        }

        // ════════════════════════════════════════════════════════
        // C4: 편집 모드 - 노드 추가/수정/삭제, 의존성 드래그&드롭
        // C5: Command 스택 기반 Undo/Redo
        // ════════════════════════════════════════════════════════

        // 편집 세션 상태
        const _editSession = {
            active: false,
            pendingOps: [],
            undoStack: [],
            redoStack: [],
        };
        window._editSession = _editSession;
        window._editEdgeSource = null;

        // ─── C5: Command 클래스 ───
        function AddNodeCmd(spec) {
            this.spec = spec;
            this.execute = function() {
                const color = statusColors[spec.status] || '#9e9e9e';
                cy.add({
                    group: 'nodes',
                    data: {
                        id: spec.id,
                        label: spec.id + '\n' + spec.title,
                        shortLabel: (spec.id.split('-').pop()) || spec.id,
                        nodeType: spec.nodeType,
                        status: spec.status,
                        color: color,
                        borderColor: color,
                        description: spec.description || '',
                        featureId: spec.id,
                    },
                    position: { x: (cy.width() / 2) + (Math.random() - 0.5) * 200, y: (cy.height() / 2) + (Math.random() - 0.5) * 200 },
                });
                if (spec.parent && cy.getElementById(spec.parent).length > 0) {
                    cy.add({ group: 'edges', data: { source: spec.parent, target: spec.id, type: 'parent' } });
                }
            };
            this.undo = function() {
                cy.remove(cy.getElementById(spec.id));
            };
            this.toOp = function() { return { type: 'addNode', spec: spec }; };
        }

        function EditNodeCmd(id, oldData, newData) {
            this.id = id;
            this.execute = function() {
                const node = cy.getElementById(id);
                if (!node.length) { return; }
                const color = statusColors[newData.status] || '#9e9e9e';
                node.data({ status: newData.status, nodeType: newData.nodeType, description: newData.description || '', color: color, borderColor: color, label: id + '\n' + newData.title });
            };
            this.undo = function() {
                const node = cy.getElementById(id);
                if (!node.length) { return; }
                const color = statusColors[oldData.status] || '#9e9e9e';
                node.data({ status: oldData.status, nodeType: oldData.nodeType, description: oldData.description || '', color: color, borderColor: color, label: id + '\n' + oldData.title });
            };
            this.toOp = function() { return { type: 'editNode', id: id, changes: newData }; };
        }

        function DeleteNodeCmd(id) {
            this.id = id;
            this._removed = null;
            this.execute = function() {
                this._removed = cy.getElementById(id).remove();
            };
            this.undo = function() {
                if (this._removed) { cy.add(this._removed); }
            };
            this.toOp = function() { return { type: 'deleteNode', id: id }; };
        }

        function AddEdgeCmd(source, target) {
            this.execute = function() {
                cy.add({ group: 'edges', data: { source: source, target: target, type: 'dependency' } });
            };
            this.undo = function() {
                cy.edges('[source="' + source + '"][target="' + target + '"][type="dependency"]').remove();
            };
            this.toOp = function() { return { type: 'addEdge', source: source, target: target }; };
        }

        // ─── C5: Command 실행/Undo/Redo ───
        function executeEditCmd(cmd) {
            cmd.execute();
            _editSession.undoStack.push(cmd);
            _editSession.redoStack = [];
            _editSession.pendingOps.push(cmd.toOp());
            _updateUndoRedoButtons();
        }

        function undoEditCmd() {
            if (!_editSession.undoStack.length) { return; }
            const cmd = _editSession.undoStack.pop();
            cmd.undo();
            _editSession.redoStack.push(cmd);
            _editSession.pendingOps = _editSession.undoStack.map(function(c) { return c.toOp(); });
            _updateUndoRedoButtons();
        }

        function redoEditCmd() {
            if (!_editSession.redoStack.length) { return; }
            const cmd = _editSession.redoStack.pop();
            cmd.execute();
            _editSession.undoStack.push(cmd);
            _editSession.pendingOps.push(cmd.toOp());
            _updateUndoRedoButtons();
        }

        function _updateUndoRedoButtons() {
            const u = document.getElementById('btnUndo');
            const r = document.getElementById('btnRedo');
            if (u) { u.disabled = _editSession.undoStack.length === 0; }
            if (r) { r.disabled = _editSession.redoStack.length === 0; }
        }
        window._updateUndoRedoButtons = _updateUndoRedoButtons;

        // ─── Edge Draw Mode (C4: 의존성 드래그&드롭) ───
        window._onEditEdgeTarget = function(source, target) {
            executeEditCmd(new AddEdgeCmd(source, target));
        };
        window._clearEditEdgeSource = function() {
            if (window._editEdgeSource) {
                cy.getElementById(window._editEdgeSource).style({
                    'border-width': '',
                    'border-color': '',
                });
            }
            window._editEdgeSource = null;
            document.getElementById('cy').style.cursor = 'default';
        };

        function enterEdgeDrawMode(nodeId) {
            window._clearEditEdgeSource();
            window._editEdgeSource = nodeId;
            cy.getElementById(nodeId).style({ 'border-width': 4, 'border-color': '#ffeb3b' });
            document.getElementById('cy').style.cursor = 'crosshair';
        }

        // ─── C5: Keyboard Undo/Redo + Delete ───
        document.addEventListener('keydown', function(e) {
            if (!_editSession.active) { return; }
            const tag = document.activeElement && document.activeElement.tagName;
            if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') { return; }
            if (e.ctrlKey && !e.shiftKey && e.key === 'z') {
                undoEditCmd(); e.preventDefault();
            } else if (e.ctrlKey && (e.key === 'y' || (e.shiftKey && e.key === 'z'))) {
                redoEditCmd(); e.preventDefault();
            } else if (e.key === 'Delete' || e.key === 'Backspace') {
                const sel = cy.$(':selected');
                if (sel.length === 1 && sel.isNode()) {
                    if (confirm('노드 "' + sel.data('id') + '"을(를) 삭제하시겠습니까?')) {
                        executeEditCmd(new DeleteNodeCmd(sel.data('id')));
                    }
                    e.preventDefault();
                }
            }
        });

        // ─── Double-tap: Add / Edit node ───
        cy.on('dbltap', 'node', function(evt) {
            if (!_editSession.active) { return; }
            const node = evt.target;
            const id = node.data('id');
            const titlePart = (node.data('label') || '').split('\n');
            const title = titlePart.length > 1 ? titlePart.slice(1).join(' ') : '';
            document.getElementById('modalTitle').textContent = '스펙 노드 편집';
            document.getElementById('modalNodeId').value = id;
            document.getElementById('modalNodeId').disabled = true;
            document.getElementById('modalNodeTitle').value = title;
            document.getElementById('modalNodeStatus').value = node.data('status') || 'draft';
            document.getElementById('modalNodeType').value = node.data('nodeType') || 'feature';
            document.getElementById('modalNodeDesc').value = node.data('description') || '';
            document.getElementById('modalNodeParent').value = '';
            const modal = document.getElementById('editModal');
            modal.dataset.mode = 'edit';
            modal.dataset.editId = id;
            modal.classList.add('visible');
        });

        cy.on('dbltap', function(evt) {
            if (!_editSession.active) { return; }
            if (evt.target !== cy) { return; }
            // Add new node
            document.getElementById('modalTitle').textContent = '새 스펙 노드 추가';
            document.getElementById('modalNodeId').value = 'F-' + String(Date.now()).slice(-4);
            document.getElementById('modalNodeId').disabled = false;
            document.getElementById('modalNodeTitle').value = '';
            document.getElementById('modalNodeStatus').value = 'draft';
            document.getElementById('modalNodeType').value = 'feature';
            document.getElementById('modalNodeDesc').value = '';
            document.getElementById('modalNodeParent').value = '';
            const modal = document.getElementById('editModal');
            modal.dataset.mode = 'add';
            modal.dataset.editId = '';
            modal.classList.add('visible');
        });

        // ─── Modal submit/cancel ───
        document.getElementById('modalCancelBtn').addEventListener('click', function() {
            document.getElementById('editModal').classList.remove('visible');
            document.getElementById('modalNodeId').disabled = false;
        });

        document.getElementById('modalSubmitBtn').addEventListener('click', function() {
            const modal = document.getElementById('editModal');
            const mode = modal.dataset.mode;
            const id = document.getElementById('modalNodeId').value.trim();
            const title = document.getElementById('modalNodeTitle').value.trim();
            const status = document.getElementById('modalNodeStatus').value;
            const nodeType = document.getElementById('modalNodeType').value;
            const description = document.getElementById('modalNodeDesc').value.trim();
            const parent = document.getElementById('modalNodeParent').value.trim() || null;
            if (!id || !title) { alert('ID와 제목을 입력하세요'); return; }

            if (mode === 'add') {
                if (cy.getElementById(id).length > 0) { alert('이미 존재하는 ID: ' + id); return; }
                executeEditCmd(new AddNodeCmd({ id: id, title: title, status: status, nodeType: nodeType, description: description, parent: parent, dependencies: [] }));
            } else {
                const editId = modal.dataset.editId;
                const node = cy.getElementById(editId);
                if (!node.length) { return; }
                const curLabel = (node.data('label') || '').split('\n');
                const oldTitle = curLabel.length > 1 ? curLabel.slice(1).join(' ') : '';
                const oldData = { title: oldTitle, status: node.data('status'), nodeType: node.data('nodeType'), description: node.data('description') || '' };
                executeEditCmd(new EditNodeCmd(editId, oldData, { title: title, status: status, nodeType: nodeType, description: description }));
            }
            modal.classList.remove('visible');
            document.getElementById('modalNodeId').disabled = false;
        });

        // ─── Edit Mode Toggle ───
        document.getElementById('btnToggleEdit').addEventListener('click', function() {
            _editSession.active = !_editSession.active;
            const btn = document.getElementById('btnToggleEdit');
            const editOnlyIds = ['btnUndo', 'btnRedo', 'btnAddNode', 'btnDeleteNode', 'btnAddEdge', 'btnSaveEdits'];
            const label = document.getElementById('editModeLabel');
            if (_editSession.active) {
                btn.classList.add('edit-active');
                btn.title = '편집 모드 종료';
                editOnlyIds.forEach(function(id) { const el = document.getElementById(id); if (el) { el.style.display = ''; } });
                if (label) { label.style.display = 'inline'; }
            } else {
                btn.classList.remove('edit-active');
                btn.title = '편집 모드 전환';
                editOnlyIds.forEach(function(id) { const el = document.getElementById(id); if (el) { el.style.display = 'none'; } });
                if (label) { label.style.display = 'none'; }
                window._clearEditEdgeSource();
            }
        });

        document.getElementById('btnUndo').addEventListener('click', undoEditCmd);
        document.getElementById('btnRedo').addEventListener('click', redoEditCmd);

        document.getElementById('btnAddNode').addEventListener('click', function() {
            document.getElementById('modalTitle').textContent = '새 스펙 노드 추가';
            document.getElementById('modalNodeId').value = 'F-' + String(Date.now()).slice(-4);
            document.getElementById('modalNodeId').disabled = false;
            document.getElementById('modalNodeTitle').value = '';
            document.getElementById('modalNodeStatus').value = 'draft';
            document.getElementById('modalNodeType').value = 'feature';
            document.getElementById('modalNodeDesc').value = '';
            document.getElementById('modalNodeParent').value = '';
            const modal = document.getElementById('editModal');
            modal.dataset.mode = 'add';
            modal.dataset.editId = '';
            modal.classList.add('visible');
        });

        document.getElementById('btnDeleteNode').addEventListener('click', function() {
            const sel = cy.$(':selected');
            if (sel.length !== 1 || !sel.isNode()) { alert('삭제할 노드를 먼저 선택하세요'); return; }
            if (confirm('노드 "' + sel.data('id') + '"을(를) 삭제하시겠습니까?')) {
                executeEditCmd(new DeleteNodeCmd(sel.data('id')));
            }
        });

        document.getElementById('btnAddEdge').addEventListener('click', function() {
            const sel = cy.$(':selected');
            if (sel.length !== 1 || !sel.isNode()) { alert('소스 노드를 먼저 선택한 후 클릭하세요'); return; }
            enterEdgeDrawMode(sel.data('id'));
            alert('타깃 노드를 클릭하면 의존성 엣지가 추가됩니다 (ESC로 취소)');
        });

        document.getElementById('btnSaveEdits').addEventListener('click', function() {
            if (_editSession.pendingOps.length === 0) { alert('변경 사항이 없습니다'); return; }
            vscode.postMessage({ type: 'saveEdits', ops: _editSession.pendingOps });
        });

        document.addEventListener('keydown', function(e) {
            if (e.key === 'Escape' && window._editEdgeSource) {
                window._clearEditEdgeSource();
            }
        });

    </script>

    <!-- C4: 편집 모드 모달 -->
    <div id="editModal" class="edit-modal-overlay">
        <div class="edit-modal">
            <h3 id="modalTitle">노드 추가/편집</h3>
            <label>ID<input id="modalNodeId" type="text" placeholder="F-999"></label>
            <label>제목<input id="modalNodeTitle" type="text" placeholder="기능 제목"></label>
            <label>상태<select id="modalNodeStatus">
                <option value="draft">draft</option>
                <option value="queued">queued</option>
                <option value="working">working</option>
                <option value="needs-review">needs-review</option>
                <option value="verified">verified</option>
                <option value="deprecated">deprecated</option>
                <option value="done">done</option>
            </select></label>
            <label>타입<select id="modalNodeType">
                <option value="feature">feature</option>
                <option value="task">task</option>
            </select></label>
            <label>설명<textarea id="modalNodeDesc" placeholder="기능 설명 (선택)"></textarea></label>
            <label>부모 ID<input id="modalNodeParent" type="text" placeholder="F-001 (선택)"></label>
            <div class="modal-actions">
                <button class="btn-cancel" id="modalCancelBtn">취소</button>
                <button class="btn-primary" id="modalSubmitBtn">확인</button>
            </div>
        </div>
    </div>
</body>
</html>`;
    }

    private getErrorHtml(message: string): string {
        return `<!DOCTYPE html>
<html lang="ko">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <style>
        body {
            font-family: var(--vscode-font-family, sans-serif);
            background: var(--vscode-editor-background, #1e1e1e);
            color: var(--vscode-editor-foreground, #d4d4d4);
            padding: 16px;
            font-size: 13px;
            line-height: 1.5;
        }
        .error {
            color: var(--vscode-errorForeground, #f48771);
            margin-bottom: 10px;
        }
        .hint {
            color: var(--vscode-descriptionForeground, #999);
        }
    </style>
</head>
<body>
    <div class="error">${escapeHtmlMessage(message)}</div>
    <div class="hint">명령 팔레트에서 "Spec Graph: 디버그 정보"를 실행해 스펙 로딩 상태를 확인하세요.</div>
</body>
</html>`;
    }

    /** Spec 그래프 데이터를 Cytoscape elements로 변환 */
    private buildCytoscapeElements(graph: SpecGraph): object[] {
        const elements: object[] = [];

        for (const node of graph.nodes) {
            const color = STATUS_COLORS[node.status] || '#888';
            const isSpecNode = node.nodeType === 'feature' || node.nodeType === 'task';
            const borderColor = isSpecNode ? color : this.lighten(color, 0.3);

            elements.push({
                group: 'nodes',
                data: {
                    id: node.id,
                    label: isSpecNode ? `${node.id}\n${node.label}` : node.id,
                    shortLabel: node.id.split('-').pop() || node.id,
                    nodeType: node.nodeType,
                    status: node.status,
                    color: isSpecNode ? color : this.lighten(color, 0.5),
                    borderColor: borderColor,
                    featureId: node.featureId || node.id,
                },
            });
        }

        for (const edge of graph.edges) {
            elements.push({
                group: 'edges',
                data: {
                    source: edge.source,
                    target: edge.target,
                    type: edge.type,
                },
            });
        }

        return elements;
    }

    /** 색상을 밝게 조정 */
    private lighten(hex: string, amount: number): string {
        const r = parseInt(hex.slice(1, 3), 16);
        const g = parseInt(hex.slice(3, 5), 16);
        const b = parseInt(hex.slice(5, 7), 16);
        const nr = Math.min(255, Math.round(r + (255 - r) * amount));
        const ng = Math.min(255, Math.round(g + (255 - g) * amount));
        const nb = Math.min(255, Math.round(b + (255 - b) * amount));
        return `#${nr.toString(16).padStart(2, '0')}${ng.toString(16).padStart(2, '0')}${nb.toString(16).padStart(2, '0')}`;
    }

    /**
     * C4: 편집 ops를 스펙 파일에 적용한다.
     * draft 상태 메모리 변경을 일괄 검증 후 저장.
     */
    private async applyEditOps(
        ops: Array<{type: string; [key: string]: unknown}>,
    ): Promise<{ok: boolean; errors: string[]}> {
        const fs = require('fs') as typeof import('fs');
        const pathMod = require('path') as typeof import('path');
        const errors: string[] = [];
        const specsDir = this.loader.specsDirectory;

        // 유효성 검사 (저장 전)
        for (const op of ops) {
            if (op.type === 'addNode') {
                const spec = op.spec as Record<string, unknown>;
                if (!spec.id || typeof spec.id !== 'string') { errors.push('addNode: ID 없음'); continue; }
                if (!spec.title || typeof spec.title !== 'string') { errors.push(`addNode ${spec.id}: title 없음`); }
            }
        }
        if (errors.length > 0) { return { ok: false, errors }; }

        // 적용
        for (const op of ops) {
            try {
                if (op.type === 'addNode') {
                    const spec = op.spec as Record<string, unknown>;
                    const id = spec.id as string;
                    const filePath = pathMod.join(specsDir, `${id}.json`);
                    if (fs.existsSync(filePath)) { errors.push(`addNode: ${id} 이미 존재함`); continue; }
                    const newSpec = {
                        schemaVersion: 2,
                        id,
                        nodeType: spec.nodeType || 'feature',
                        title: spec.title || id,
                        description: spec.description || '',
                        status: spec.status || 'draft',
                        parent: spec.parent || null,
                        dependencies: (spec.dependencies as string[]) || [],
                        conditions: [],
                        codeRefs: [],
                        evidence: [],
                        tags: [],
                        metadata: {},
                        createdAt: new Date().toISOString(),
                        updatedAt: new Date().toISOString(),
                    };
                    fs.writeFileSync(filePath, JSON.stringify(newSpec, null, 2), 'utf-8');

                } else if (op.type === 'editNode') {
                    const id = op.id as string;
                    const changes = op.changes as Record<string, unknown>;
                    const filePath = pathMod.join(specsDir, `${id}.json`);
                    if (!fs.existsSync(filePath)) { errors.push(`editNode: ${id} 없음`); continue; }
                    const existing = JSON.parse(fs.readFileSync(filePath, 'utf-8')) as Record<string, unknown>;
                    const updated = { ...existing, ...changes, updatedAt: new Date().toISOString() };
                    fs.writeFileSync(filePath, JSON.stringify(updated, null, 2), 'utf-8');

                } else if (op.type === 'deleteNode') {
                    const id = op.id as string;
                    const filePath = pathMod.join(specsDir, `${id}.json`);
                    if (fs.existsSync(filePath)) { fs.unlinkSync(filePath); }

                } else if (op.type === 'addEdge') {
                    // 소스 spec의 dependencies에 target 추가
                    const source = op.source as string;
                    const target = op.target as string;
                    const filePath = pathMod.join(specsDir, `${source}.json`);
                    if (!fs.existsSync(filePath)) { errors.push(`addEdge: 소스 ${source} 없음`); continue; }
                    const existing = JSON.parse(fs.readFileSync(filePath, 'utf-8')) as Record<string, unknown>;
                    const deps = (existing.dependencies as string[]) || [];
                    if (!deps.includes(target)) {
                        existing.dependencies = [...deps, target];
                        existing.updatedAt = new Date().toISOString();
                        fs.writeFileSync(filePath, JSON.stringify(existing, null, 2), 'utf-8');
                    }

                } else if (op.type === 'removeEdge') {
                    const source = op.source as string;
                    const target = op.target as string;
                    const filePath = pathMod.join(specsDir, `${source}.json`);
                    if (fs.existsSync(filePath)) {
                        const existing = JSON.parse(fs.readFileSync(filePath, 'utf-8')) as Record<string, unknown>;
                        existing.dependencies = ((existing.dependencies as string[]) || []).filter((d: string) => d !== target);
                        existing.updatedAt = new Date().toISOString();
                        fs.writeFileSync(filePath, JSON.stringify(existing, null, 2), 'utf-8');
                    }
                }
            } catch (err) {
                errors.push(`${op.type} ${String(op.id || op.source || '')}: ${String(err)}`);
            }
        }

        if (errors.length === 0) {
            await this.loader.reload();
        }
        return { ok: errors.length === 0, errors };
    }

    dispose(): void {
        if (this.isPrimaryPanel) {
            GraphPanel.currentPanel = undefined;
        }
        this.panel.dispose();
        while (this.disposables.length) {
            const d = this.disposables.pop();
            d?.dispose();
        }
    }
}

function getNonce(): string {
    let text = '';
    const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    for (let i = 0; i < 32; i++) {
        text += possible.charAt(Math.floor(Math.random() * possible.length));
    }
    return text;
}

function escapeHtmlMessage(input: string): string {
    return input.replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
}
