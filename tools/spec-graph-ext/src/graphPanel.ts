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

        GraphPanel.currentPanel = new GraphPanel(panel, extensionUri, loader, workspaceRoot);
        GraphPanel.currentPanel.update();
        return GraphPanel.currentPanel;
    }

    /** 특정 노드에 포커스 */
    focusNode(nodeId: string): void {
        this.panel.reveal();
        this.panel.webview.postMessage({ type: 'focusNode', nodeId });
    }

    /** 그래프 데이터 갱신 */
    async update(): Promise<void> {
        const graph = await this.loader.getGraph();
        this.panel.webview.html = this.getHtml(graph);
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
            require('path').join(this.workspaceRoot, 'docs', 'specs', `${specId}.json`)
        );
        try {
            const doc = await vscode.workspace.openTextDocument(filePath);
            await vscode.window.showTextDocument(doc, { preview: true });
        } catch {
            vscode.window.showWarningMessage(`스펙 파일을 찾을 수 없습니다: ${specId}.json`);
        }
    }

    /** Webview HTML 생성 */
    private getHtml(graph: SpecGraph): string {
        const nonce = getNonce();

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
                   script-src 'nonce-${nonce}' https://cdnjs.cloudflare.com;
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
            <option value="">All</option>
            <option value="draft">Draft</option>
            <option value="active">Active</option>
            <option value="needs-review">Needs Review</option>
            <option value="verified">Verified</option>
            <option value="deprecated">Deprecated</option>
        </select>
        <div class="separator"></div>
        <label>
            <input type="checkbox" id="chkConditions" checked> 조건 표시
        </label>
        <div class="separator"></div>
        <div class="legend">
            <div class="legend-item"><div class="legend-dot" style="background:#4caf50"></div> verified</div>
            <div class="legend-item"><div class="legend-dot" style="background:#2196f3"></div> active</div>
            <div class="legend-item"><div class="legend-dot" style="background:#ff9800"></div> needs-review</div>
            <div class="legend-item"><div class="legend-dot" style="background:#9e9e9e"></div> draft</div>
            <div class="legend-item"><div class="legend-dot" style="background:#f44336"></div> deprecated</div>
        </div>
    </div>

    <div class="main">
        <div id="cy"></div>
        <div class="detail-panel" id="detailPanel">
            <button class="close-btn" id="btnCloseDetail">✕</button>
            <div id="detailContent"></div>
        </div>
    </div>

    <script src="https://cdnjs.cloudflare.com/ajax/libs/cytoscape/3.28.1/cytoscape.min.js"></script>
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
        const cy = cytoscape({
            container: document.getElementById('cy'),
            elements: elements,
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
            layout: {
                name: 'cose',
                animate: true,
                animationDuration: 500,
                nodeRepulsion: function() { return 8000; },
                idealEdgeLength: function() { return 80; },
                gravity: 0.3,
                padding: 30,
            },
            minZoom: 0.2,
            maxZoom: 4,
            wheelSensitivity: 0.3,
        });

        // ─── 노드 클릭 이벤트 ───
        cy.on('tap', 'node', function(evt) {
            const node = evt.target;
            const id = node.data('id');
            showDetail(id);
            vscode.postMessage({ type: 'selectNode', nodeId: id });
        });

        // 빈 공간 클릭 시 상세 패널 닫기
        cy.on('tap', function(evt) {
            if (evt.target === cy) {
                hideDetail();
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
            const isFeature = nodeData.nodeType === 'feature';

            let html = '<h2>' + escapeHtml(nodeData.id + (isFeature ? ': ' + nodeData.label : '')) + '</h2>';

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
                    html += '<a class="code-ref" onclick="openCodeRef(\'' + escapeHtml(ref) + '\')">'
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
                            html += '<br><a class="code-ref" onclick="openCodeRef(\'' + escapeHtml(ref) + '\')">'
                                  + escapeHtml(ref) + '</a>';
                        }
                    }
                    html += '</div>';
                }
            }

            // 스펙 파일 열기 버튼
            const fileId = isFeature ? nodeId : nodeData.featureId;
            if (fileId) {
                html += '<button class="btn-open-file" onclick="openSpecFile(\'' + escapeHtml(fileId) + '\')">'
                      + '📄 ' + fileId + '.json 열기</button>';
            }

            content.innerHTML = html;
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

        // ─── Extension → Webview 메시지 수신 ───
        window.addEventListener('message', function(event) {
            const msg = event.data;
            if (msg.type === 'focusNode') {
                const node = cy.getElementById(msg.nodeId);
                if (node && node.length > 0) {
                    // 기존 하이라이트 제거
                    cy.elements().removeClass('highlighted');
                    node.addClass('highlighted');
                    cy.animate({
                        center: { eles: node },
                        zoom: 2,
                    }, { duration: 400 });
                    showDetail(msg.nodeId);
                }
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
    </script>
</body>
</html>`;
    }

    /** Spec 그래프 데이터를 Cytoscape elements로 변환 */
    private buildCytoscapeElements(graph: SpecGraph): object[] {
        const elements: object[] = [];

        for (const node of graph.nodes) {
            const color = STATUS_COLORS[node.status] || '#888';
            const borderColor = node.nodeType === 'feature' ? color : this.lighten(color, 0.3);

            elements.push({
                group: 'nodes',
                data: {
                    id: node.id,
                    label: node.nodeType === 'feature' ? `${node.id}\n${node.label}` : node.id,
                    shortLabel: node.id.split('-').pop() || node.id,
                    nodeType: node.nodeType,
                    status: node.status,
                    color: node.nodeType === 'feature' ? color : this.lighten(color, 0.5),
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

    dispose(): void {
        GraphPanel.currentPanel = undefined;
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
