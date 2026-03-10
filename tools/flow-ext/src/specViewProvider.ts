/**
 * SpecViewProvider - 스펙을 읽기 쉬운 마크다운 문서 형태로 렌더링하는 Webview 패널
 *
 * - 선택된 스펙 없음: 전체 스펙을 문서로 렌더링
 * - 선택된 스펙 있음: 해당 스펙 + 부모 스펙(1단계) + 하위 스펙(직계)만 렌더링
 */
import * as vscode from 'vscode';
import { SpecLoader } from './specLoader';
import { Spec, Condition, SpecStatus, STATUS_COLORS, GitHubRef, DocLink } from './types';
import { describeReviewDisposition, getConditionManualVerificationItems, getSpecReviewState, getUserFeedbackState } from './reviewState';
import { saveQuestionAnswer } from './feedbackStore';
import { renderSpecActivitySection, resolveSpecViewSelection } from './specViewActivity';

export class SpecViewProvider {
    public static currentPanel: SpecViewProvider | undefined;
    private static readonly viewType = 'specGraph.specView';

    private readonly panel: vscode.WebviewPanel;
    private disposables: vscode.Disposable[] = [];
    private currentSpecId: string | null = null;
    /** 저장 전 드래프트 답변: specId → questionId → text */
    private readonly draftAnswers = new Map<string, Map<string, string>>();

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
        this.loader.onDidStateChange(() => this.patchStatuses());
    }

    /** 싱글톤 패널 생성 또는 포커스 */
    static createOrShow(
        extensionUri: vscode.Uri,
        loader: SpecLoader,
        workspaceRoot: string,
        preferColumn?: vscode.ViewColumn,
    ): SpecViewProvider {
        const column = preferColumn ?? vscode.ViewColumn.One;

        if (SpecViewProvider.currentPanel) {
            SpecViewProvider.currentPanel.panel.reveal(column);
            return SpecViewProvider.currentPanel;
        }

        const panel = vscode.window.createWebviewPanel(
            SpecViewProvider.viewType,
            'Spec View',
            column,
            {
                enableScripts: true,
                retainContextWhenHidden: true,
                localResourceRoots: [extensionUri],
            },
        );

        SpecViewProvider.currentPanel = new SpecViewProvider(panel, extensionUri, loader, workspaceRoot);
        SpecViewProvider.currentPanel.update();
        return SpecViewProvider.currentPanel;
    }

    /** 특정 스펙으로 포커스 */
    focusSpec(specId: string | null): void {
        this.currentSpecId = specId;
        this.panel.reveal();
        this.update();
    }

    /** 전체 보기로 전환 */
    showAll(): void {
        this.currentSpecId = null;
        this.update();
    }

    /** 렌더링 갱신 */
    async update(): Promise<void> {
        try {
            const graph = await this.loader.getGraph();
            const specs = graph.specs;
            const selection = resolveSpecViewSelection(specs, this.currentSpecId);
            const focusedId = selection.focusedSpec?.id ?? null;
            this.panel.title = focusedId ? `Spec View: ${focusedId}` : 'Spec View: All';
            this.panel.webview.html = this.getHtml(selection.specs, focusedId);
        } catch (e) {
            this.panel.webview.html = this.getErrorHtml(`스펙 로드 실패: ${String(e)}`);
        }
    }

    /** 상태 배지만 패치 (HTML 전체 재빌드 없이 스펙 상태 변경 반영) */
    private patchStatuses(): void {
        if (!this.panel.visible) { return; }
        const specs = this.loader.getSpecs();
        const statuses: Record<string, { status: string; color: string }> = {};
        for (const spec of specs) {
            statuses[spec.id] = { status: spec.status, color: STATUS_COLORS[spec.status] || '#888' };
        }
        void this.panel.webview.postMessage({ type: 'patchStatuses', statuses });
    }

    /** 선택된 스펙 + 부모(1단계) + 하위 스펙 추출 */
    private getRelatedSpecs(focusedSpec: Spec, allSpecs: Spec[]): Spec[] {
        const result: Spec[] = [];
        const addedIds = new Set<string>();

        // 1. 부모 스펙 (1단계)
        if (focusedSpec.parent) {
            const parent = allSpecs.find(s => s.id === focusedSpec.parent);
            if (parent && !addedIds.has(parent.id)) {
                result.push(parent);
                addedIds.add(parent.id);
            }
        }

        // 2. 선택된 스펙
        if (!addedIds.has(focusedSpec.id)) {
            result.push(focusedSpec);
            addedIds.add(focusedSpec.id);
        }

        // 3. 하위 스펙 (직계 children)
        const children = allSpecs.filter(s => s.parent === focusedSpec.id);
        for (const child of children.sort((a, b) => a.id.localeCompare(b.id))) {
            if (!addedIds.has(child.id)) {
                result.push(child);
                addedIds.add(child.id);
            }
        }

        return result;
    }

    /** 스펙 계층 구조로 정렬 (루트부터 재귀 정렬) */
    private sortByHierarchy(specs: Spec[]): Spec[] {
        const specMap = new Map<string, Spec>();
        for (const s of specs) { specMap.set(s.id, s); }

        const childrenMap = new Map<string | null, Spec[]>();
        for (const s of specs) {
            const parentKey = s.parent || null;
            if (!childrenMap.has(parentKey)) { childrenMap.set(parentKey, []); }
            childrenMap.get(parentKey)!.push(s);
        }

        // 각 그룹 내 정렬
        for (const [, children] of childrenMap) {
            children.sort((a, b) => a.id.localeCompare(b.id));
        }

        const result: Spec[] = [];
        const visited = new Set<string>();

        const walk = (parentId: string | null, depth: number) => {
            const children = childrenMap.get(parentId) || [];
            for (const child of children) {
                if (visited.has(child.id)) { continue; }
                visited.add(child.id);
                result.push(child);
                walk(child.id, depth + 1);
            }
        };

        // 루트 스펙부터 시작
        walk(null, 0);

        // parent가 표시 대상이 아닌 스펙 추가 (고아 노드)
        for (const s of specs) {
            if (!visited.has(s.id)) {
                result.push(s);
            }
        }

        return result;
    }

    /** 스펙의 계층 깊이 계산 */
    private getDepth(spec: Spec, allSpecs: Spec[]): number {
        let depth = 0;
        let current: Spec | undefined = spec;
        const visited = new Set<string>();
        while (current?.parent) {
            if (visited.has(current.id)) { break; }
            visited.add(current.id);
            current = allSpecs.find(s => s.id === current!.parent);
            if (current) { depth++; }
        }
        return depth;
    }

    /** 전체 HTML 생성 */
    private getHtml(specs: Spec[], focusedId: string | null): string {
        const nonce = getNonce();
        const sortedSpecs = this.sortByHierarchy(specs);
        const allSpecs = this.loader.getSpecs();

        // 마크다운 문서 콘텐츠 생성
        let contentHtml = '';

        if (focusedId) {
            contentHtml += `<div class="nav-bar">
                <button class="nav-btn" id="btnShowAll">← 전체 스펙 보기</button>
            </div>`;
        }

        // 요약 테이블
        contentHtml += this.renderSummaryTable(sortedSpecs, focusedId);

        // 각 스펙 렌더링
        for (const spec of sortedSpecs) {
            const depth = this.getDepth(spec, allSpecs);
            const isFocused = spec.id === focusedId;
            contentHtml += this.renderSpec(spec, depth, isFocused, allSpecs, this.draftAnswers.get(spec.id));
        }

        return /*html*/ `<!DOCTYPE html>
<html lang="ko">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta http-equiv="Content-Security-Policy"
          content="default-src 'none';
                   style-src 'unsafe-inline';
                   script-src 'nonce-${nonce}';
                   img-src data:;">
    <title>Spec View</title>
    <style>
        :root {
            --border-color: var(--vscode-panel-border, #3c3c3c);
            --bg-primary: var(--vscode-editor-background, #1e1e1e);
            --bg-secondary: var(--vscode-sideBar-background, #252526);
            --fg-primary: var(--vscode-editor-foreground, #d4d4d4);
            --fg-secondary: var(--vscode-descriptionForeground, #888);
            --link-color: var(--vscode-textLink-foreground, #3794ff);
            --badge-bg: var(--vscode-badge-background, #4d4d4d);
            --badge-fg: var(--vscode-badge-foreground, #ccc);
        }

        * { margin: 0; padding: 0; box-sizing: border-box; }

        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', sans-serif;
            background: var(--bg-primary);
            color: var(--fg-primary);
            line-height: 1.7;
            padding: 0;
        }

        .container {
            max-width: 900px;
            margin: 0 auto;
            padding: 32px 40px 80px;
        }

        /* 내비게이션 바 */
        .nav-bar {
            margin-bottom: 24px;
            padding-bottom: 12px;
            border-bottom: 1px solid var(--border-color);
        }
        .nav-btn {
            background: none;
            border: 1px solid var(--border-color);
            color: var(--link-color);
            padding: 4px 12px;
            border-radius: 4px;
            cursor: pointer;
            font-size: 13px;
        }
        .nav-btn:hover {
            background: var(--bg-secondary);
        }

        /* 요약 테이블 */
        .summary-section {
            margin-bottom: 40px;
        }
        .summary-section h2 {
            font-size: 20px;
            font-weight: 600;
            margin-bottom: 16px;
            padding-bottom: 8px;
            border-bottom: 2px solid var(--border-color);
        }
        .summary-table {
            width: 100%;
            border-collapse: collapse;
            font-size: 13px;
        }
        .summary-table th {
            text-align: left;
            padding: 8px 12px;
            background: var(--bg-secondary);
            border-bottom: 2px solid var(--border-color);
            font-weight: 600;
            font-size: 11px;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            color: var(--fg-secondary);
        }
        .summary-table td {
            padding: 6px 12px;
            border-bottom: 1px solid var(--border-color);
            vertical-align: top;
        }
        .summary-table tr:hover {
            background: var(--bg-secondary);
        }
        .summary-table .spec-link {
            color: var(--link-color);
            cursor: pointer;
            text-decoration: none;
        }
        .summary-table .spec-link:hover {
            text-decoration: underline;
        }

        /* 스펙 카드 */
        .spec-card {
            margin-bottom: 40px;
            padding-bottom: 32px;
            border-bottom: 1px solid var(--border-color);
        }
        .spec-card:last-child {
            border-bottom: none;
        }
        .spec-card.focused {
            border-left: 4px solid var(--link-color);
            padding-left: 20px;
        }

        .spec-header {
            display: flex;
            align-items: baseline;
            gap: 12px;
            margin-bottom: 8px;
            flex-wrap: wrap;
        }
        .spec-id {
            font-size: 13px;
            font-weight: 600;
            color: var(--fg-secondary);
            cursor: pointer;
        }
        .spec-id:hover {
            color: var(--link-color);
            text-decoration: underline;
        }
        .spec-title {
            font-weight: 700;
            margin: 0;
        }
        h2.spec-title { font-size: 22px; }
        h3.spec-title { font-size: 18px; }
        h4.spec-title { font-size: 16px; }

        .spec-meta {
            display: flex;
            align-items: center;
            gap: 10px;
            margin-bottom: 12px;
            flex-wrap: wrap;
        }

        .status-badge {
            display: inline-block;
            padding: 2px 10px;
            border-radius: 12px;
            font-size: 11px;
            font-weight: 700;
            color: #fff;
            letter-spacing: 0.3px;
        }
        .tag {
            display: inline-block;
            padding: 1px 8px;
            border-radius: 3px;
            font-size: 11px;
            background: var(--badge-bg);
            color: var(--badge-fg);
        }
        .meta-date {
            font-size: 11px;
            color: var(--fg-secondary);
        }

        .spec-description {
            font-size: 14px;
            line-height: 1.8;
            margin-bottom: 16px;
            color: var(--fg-primary);
        }

        /* 섹션 */
        .section-title {
            font-size: 13px;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            color: var(--fg-secondary);
            margin: 16px 0 8px 0;
            padding-bottom: 4px;
            border-bottom: 1px dashed var(--border-color);
        }

        /* 조건 리스트 */
        .conditions-list {
            list-style: none;
            padding: 0;
        }
        .condition-item {
            display: flex;
            align-items: flex-start;
            gap: 10px;
            padding: 8px 12px;
            margin-bottom: 4px;
            border-radius: 6px;
            background: var(--bg-secondary);
            font-size: 13px;
        }
        .condition-status-dot {
            flex-shrink: 0;
            width: 10px;
            height: 10px;
            border-radius: 50%;
            margin-top: 5px;
        }
        .condition-body {
            flex: 1;
        }
        .condition-id {
            font-weight: 600;
            font-size: 12px;
            color: var(--fg-secondary);
            margin-right: 8px;
        }
        .condition-desc {
            font-size: 13px;
        }
        .condition-refs-details {
            margin-top: 4px;
        }
        .condition-refs-summary {
            font-size: 11px;
            color: var(--fg-secondary);
            cursor: pointer;
            user-select: none;
            list-style: none;
            display: inline-flex;
            align-items: center;
            gap: 4px;
        }
        .condition-refs-summary::before {
            content: '▶';
            font-size: 9px;
            display: inline-block;
            transition: transform 0.15s;
        }
        .condition-refs-details[open] > .condition-refs-summary::before {
            transform: rotate(90deg);
        }
        .condition-refs {
            margin-top: 4px;
        }
        .condition-refs a {
            font-size: 11px;
            color: var(--link-color);
            cursor: pointer;
            margin-right: 12px;
            text-decoration: none;
        }
        .condition-refs a:hover {
            text-decoration: underline;
        }
        .activity-list {
            list-style: none;
            padding: 0;
            display: grid;
            gap: 10px;
            margin-bottom: 16px;
        }
        .activity-item {
            border: 1px solid var(--border-color);
            border-radius: 8px;
            padding: 10px 12px;
            background: var(--bg-secondary);
        }
        .activity-head {
            display: flex;
            justify-content: space-between;
            align-items: center;
            gap: 12px;
            flex-wrap: wrap;
            margin-bottom: 6px;
        }
        .activity-kind {
            font-size: 12px;
            font-weight: 700;
            letter-spacing: 0.3px;
        }
        .activity-time {
            font-size: 11px;
            color: var(--fg-secondary);
        }
        .activity-summary {
            font-size: 13px;
            line-height: 1.6;
        }
        .activity-meta {
            display: flex;
            flex-wrap: wrap;
            gap: 8px;
            margin-top: 8px;
        }
        .activity-meta-item {
            font-size: 11px;
            padding: 2px 8px;
            border-radius: 999px;
            background: rgba(255, 255, 255, 0.06);
            color: var(--fg-secondary);
        }
        .review-callout {
            margin: 14px 0 16px;
            padding: 10px 12px;
            border-radius: 8px;
            border: 1px solid var(--border-color);
            background: var(--bg-secondary);
        }
        .review-callout.manual {
            border-color: rgba(255, 152, 0, 0.45);
            background: rgba(255, 152, 0, 0.08);
        }
        .review-callout.eligible {
            border-color: rgba(76, 175, 80, 0.35);
            background: rgba(76, 175, 80, 0.08);
        }
        .review-callout-title {
            font-size: 11px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            color: var(--fg-secondary);
            margin-bottom: 6px;
        }
        .review-callout-items {
            margin: 8px 0 0 18px;
            font-size: 12px;
        }
        .condition-manual {
            margin-top: 4px;
            font-size: 11px;
            color: #ffb74d;
        }

        /* 사용자 피드백 패널 */
        .feedback-callout {
            margin: 14px 0 16px;
            padding: 12px 14px;
            border-radius: 8px;
            border: 1px solid rgba(233, 30, 99, 0.45);
            background: rgba(233, 30, 99, 0.07);
        }
        .feedback-callout-title {
            font-size: 11px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            color: #f48fb1;
            margin-bottom: 8px;
        }
        .feedback-last-answered {
            font-size: 11px;
            color: var(--fg-secondary);
            margin-bottom: 8px;
        }
        .feedback-question {
            padding: 8px 0;
            border-bottom: 1px solid rgba(233, 30, 99, 0.2);
            margin-bottom: 6px;
        }
        .feedback-question:last-child { border-bottom: none; margin-bottom: 0; }
        .feedback-q-head {
            display: flex;
            align-items: baseline;
            gap: 6px;
            margin-bottom: 4px;
        }
        .feedback-type {
            display: inline-block;
            padding: 1px 6px;
            border-radius: 999px;
            font-size: 10px;
            background: rgba(233, 30, 99, 0.18);
            color: #f8bbd0;
            flex-shrink: 0;
        }
        .feedback-q-text {
            font-size: 13px;
            font-weight: 500;
        }
        .feedback-why {
            font-size: 12px;
            color: var(--fg-secondary);
            margin-bottom: 4px;
        }
        .feedback-suggestions {
            display: flex;
            flex-wrap: wrap;
            gap: 6px;
            margin: 6px 0;
        }
        .feedback-suggestion-btn {
            border: 1px solid rgba(233, 30, 99, 0.35);
            border-radius: 999px;
            background: rgba(233, 30, 99, 0.1);
            color: var(--fg-primary);
            padding: 3px 12px;
            font-size: 12px;
            cursor: pointer;
        }
        .feedback-suggestion-btn:hover { background: rgba(233, 30, 99, 0.2); }
        .feedback-answer-area { margin-top: 6px; }
        .feedback-answer-input {
            width: 100%;
            min-height: 60px;
            background: var(--vscode-input-background, #3c3c3c);
            color: var(--fg-primary);
            border: 1px solid rgba(233, 30, 99, 0.3);
            border-radius: 4px;
            font-size: 12px;
            padding: 6px;
            resize: vertical;
            font-family: inherit;
        }
        .feedback-actions {
            display: flex;
            justify-content: flex-end;
            margin-top: 6px;
        }
        .feedback-save-btn {
            background: var(--vscode-button-background);
            color: var(--vscode-button-foreground);
            border: none;
            border-radius: 4px;
            padding: 4px 14px;
            font-size: 12px;
            cursor: pointer;
        }
        .feedback-save-btn:hover { opacity: 0.9; }
        .feedback-no-questions {
            font-size: 12px;
            color: var(--fg-secondary);
        }

        /* 코드 참조 */
        .code-refs-details {
            margin: 4px 0;
        }
        .code-refs-summary {
            font-size: 13px;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            color: var(--fg-secondary);
            cursor: pointer;
            user-select: none;
            padding: 4px 0;
            border-bottom: 1px dashed var(--border-color);
            list-style: none;
        }
        .code-refs-summary::-webkit-details-marker { display: none; }
        .code-refs-summary::before {
            content: '▶ ';
            font-size: 9px;
            vertical-align: middle;
        }
        .code-refs-details[open] .code-refs-summary::before {
            content: '▼ ';
        }
        .code-refs {
            list-style: none;
            padding: 4px 0 0 0;
        }
        .code-refs li {
            padding: 2px 0;
        }
        .code-ref-link {
            font-size: 13px;
            font-family: 'Cascadia Code', 'Fira Code', Consolas, monospace;
            color: var(--link-color);
            cursor: pointer;
            text-decoration: none;
        }
        .code-ref-link:hover {
            text-decoration: underline;
        }

        /* GitHub Refs */
        .github-refs {
            display: flex;
            gap: 8px;
            flex-wrap: wrap;
        }
        .github-ref-badge {
            display: inline-flex;
            align-items: center;
            gap: 4px;
            padding: 2px 10px;
            border-radius: 12px;
            font-size: 12px;
            cursor: pointer;
            background: var(--badge-bg);
            color: var(--badge-fg);
            border: 1px solid var(--border-color);
            text-decoration: none;
        }
        .github-ref-badge:hover {
            opacity: 0.8;
        }

        /* Doc Links */
        .doc-links {
            list-style: none;
            padding: 0;
        }
        .doc-links li {
            padding: 2px 0;
        }
        .doc-link-item {
            font-size: 13px;
            color: var(--link-color);
            cursor: pointer;
            text-decoration: none;
        }
        .doc-link-item:hover {
            text-decoration: underline;
        }

        /* 진행률 바 */
        .progress-bar-container {
            width: 100%;
            height: 6px;
            background: var(--border-color);
            border-radius: 3px;
            overflow: hidden;
            margin: 4px 0;
        }
        .progress-bar {
            height: 100%;
            border-radius: 3px;
            transition: width 0.3s;
        }
        .progress-text {
            font-size: 11px;
            color: var(--fg-secondary);
        }

        /* 부모 링크 */
        .parent-link {
            font-size: 12px;
            color: var(--fg-secondary);
            margin-bottom: 4px;
        }
        .parent-link a {
            color: var(--link-color);
            cursor: pointer;
            text-decoration: none;
        }
        .parent-link a:hover {
            text-decoration: underline;
        }

        /* 하위 스펙 네비게이션 */
        .children-nav {
            display: flex;
            flex-wrap: wrap;
            gap: 6px;
        }
        .child-nav-link {
            display: inline-block;
            padding: 3px 10px;
            border-radius: 4px;
            font-size: 12px;
            background: var(--bg-secondary);
            border: 1px solid var(--border-color);
            color: var(--link-color);
            cursor: pointer;
            text-decoration: none;
        }
        .child-nav-link:hover {
            background: var(--border-color);
        }
    </style>
</head>
<body>
    <div class="container">
        ${contentHtml}
    </div>
    <script nonce="${nonce}">
        const vscode = acquireVsCodeApi();

        // 스펙 링크 클릭 → 해당 스펙으로 포커스
        document.querySelectorAll('[data-focus-spec]').forEach(el => {
            el.addEventListener('click', (e) => {
                e.preventDefault();
                vscode.postMessage({ type: 'focusSpec', specId: el.dataset.focusSpec });
            });
        });

        // 코드 참조 클릭
        document.querySelectorAll('[data-code-ref]').forEach(el => {
            el.addEventListener('click', (e) => {
                e.preventDefault();
                vscode.postMessage({ type: 'openCodeRef', codeRef: el.dataset.codeRef });
            });
        });

        // 스펙 파일 열기
        document.querySelectorAll('[data-open-spec]').forEach(el => {
            el.addEventListener('click', (e) => {
                e.preventDefault();
                vscode.postMessage({ type: 'openSpec', specId: el.dataset.openSpec });
            });
        });

        // 전체 보기 버튼
        const btnShowAll = document.getElementById('btnShowAll');
        if (btnShowAll) {
            btnShowAll.addEventListener('click', () => {
                vscode.postMessage({ type: 'showAll' });
            });
        }

        // 외부 링크
        document.querySelectorAll('[data-external-url]').forEach(el => {
            el.addEventListener('click', (e) => {
                e.preventDefault();
                vscode.postMessage({ type: 'openExternal', url: el.dataset.externalUrl });
            });
        });

        // 문서 링크
        document.querySelectorAll('[data-doc-path]').forEach(el => {
            el.addEventListener('click', (e) => {
                e.preventDefault();
                vscode.postMessage({ type: 'openDocLink', path: el.dataset.docPath });
            });
        });

        // 앵커 링크 스크롤
        document.querySelectorAll('a[href^="#"]').forEach(el => {
            el.addEventListener('click', (e) => {
                e.preventDefault();
                const targetId = el.getAttribute('href')?.slice(1);
                if (targetId) {
                    const target = document.getElementById(targetId);
                    if (target) {
                        target.scrollIntoView({ behavior: 'smooth', block: 'start' });
                    }
                }
            });
        });

        // textarea 입력 시 draft 저장
        function sendDraftAnswer(input) {
            const qId = input.dataset.qId;
            const specCard = input.closest('[data-spec-id]');
            const specId = specCard?.dataset.specId;
            if (specId && qId) {
                vscode.postMessage({ type: 'draftAnswer', specId, questionId: qId, text: input.value });
            }
        }
        document.querySelectorAll('.feedback-answer-input').forEach(el => {
            el.addEventListener('input', () => sendDraftAnswer(el));
        });

        // 제안 답변 버튼 클릭 → textarea에 삽입 + draft 저장
        document.querySelectorAll('.feedback-suggestion-btn').forEach(el => {
            el.addEventListener('click', () => {
                const area = el.closest('.feedback-answer-area') || el.closest('.feedback-question')?.querySelector('.feedback-answer-area');
                const input = area?.querySelector('.feedback-answer-input');
                if (input) {
                    input.value = el.dataset.answer || '';
                    input.focus();
                    sendDraftAnswer(input);
                }
            });
        });

        // 상태 패치 메시지 수신 (HTML 재빌드 없이 상태 배지만 업데이트)
        window.addEventListener('message', event => {
            const msg = event.data;
            if (msg.type === 'patchStatuses') {
                for (const [specId, info] of Object.entries(msg.statuses)) {
                    const badge = document.querySelector('[data-status-badge-for="' + specId + '"]');
                    if (badge) {
                        badge.textContent = info.status;
                        badge.style.background = info.color;
                    }
                }
            }
        });

        // 답변 저장 버튼
        document.querySelectorAll('.feedback-save-btn').forEach(el => {
            el.addEventListener('click', () => {
                const question = el.closest('.feedback-question');
                const input = question?.querySelector('.feedback-answer-input');
                const answer = input?.value?.trim() || '';
                if (!answer) { return; }
                el.disabled = true;
                vscode.postMessage({
                    type: 'answerQuestion',
                    specId: el.dataset.specId,
                    questionId: el.dataset.qId,
                    questionText: el.dataset.question,
                    answer,
                });
            });
        });
    </script>
</body>
</html>`;
    }

    /** 요약 테이블 렌더링 */
    private renderSummaryTable(specs: Spec[], focusedId: string | null): string {
        const title = focusedId ? '관련 스펙' : '전체 스펙 문서';
        const count = specs.length;

        // 상태별 카운트
        const statusCounts: Record<string, number> = {};
        for (const s of specs) {
            statusCounts[s.status] = (statusCounts[s.status] || 0) + 1;
        }

        const statusSummary = Object.entries(statusCounts)
            .map(([status, cnt]) => {
                const color = STATUS_COLORS[status as SpecStatus] || '#888';
                return `<span class="status-badge" style="background:${color}">${status}: ${cnt}</span>`;
            })
            .join(' ');

        return `
        <div class="summary-section">
            <h2>${esc(title)}</h2>
            <p style="margin-bottom:12px;">
                총 <strong>${count}</strong>개 스펙 &nbsp; ${statusSummary}
            </p>
            <table class="summary-table">
                <thead>
                    <tr>
                        <th>ID</th>
                        <th>제목</th>
                        <th>상태</th>
                        <th>조건 달성률</th>
                        <th>태그</th>
                    </tr>
                </thead>
                <tbody>
                    ${specs.map(s => {
                        const color = STATUS_COLORS[s.status] || '#888';
                        const review = getSpecReviewState(s);
                        const tags = s.tags.map(t => `<span class="tag">${esc(t)}</span>`).join(' ');
                        return `<tr>
                            <td><a class="spec-link" href="#spec-${esc(s.id)}">${esc(s.id)}</a></td>
                            <td>${esc(s.title)}</td>
                            <td><span class="status-badge" style="background:${color};font-size:10px">${s.status}</span></td>
                            <td>
                                ${review.totalConditions > 0
                                    ? `<div class="progress-bar-container"><div class="progress-bar" style="width:${review.progressPercent}%;background:${review.progressPercent === 100 ? '#4caf50' : '#2196f3'}"></div></div>
                                       <span class="progress-text">${review.verifiedConditions}/${review.totalConditions} (${review.progressPercent}%)</span>
                                       ${review.requiresManualVerification ? '<br><span class="progress-text" style="color:#ffb74d">수동 검증 필요</span>' : ''}`
                                    : '<span class="progress-text">조건 없음</span>'
                                }
                            </td>
                            <td>${tags}</td>
                        </tr>`;
                    }).join('')}
                </tbody>
            </table>
        </div>`;
    }

    /** 개별 스펙 카드 렌더링 */
    private renderSpec(spec: Spec, depth: number, isFocused: boolean, allSpecs: Spec[], drafts?: Map<string, string>): string {
        const color = STATUS_COLORS[spec.status] || '#888';
        const headingTag = depth === 0 ? 'h2' : depth === 1 ? 'h3' : 'h4';
        const focusedClass = isFocused ? ' focused' : '';
        const review = getSpecReviewState(spec);
        const feedback = getUserFeedbackState(spec);

        // 조건 달성률
        const totalCond = review.totalConditions;
        const verifiedCond = review.verifiedConditions;
        const pct = review.progressPercent;

        // 날짜 포맷
        const updatedAt = spec.updatedAt ? formatDate(spec.updatedAt) : '';

        // 부모 링크
        let parentHtml = '';
        if (spec.parent) {
            const parentSpec = allSpecs.find(s => s.id === spec.parent);
            const parentTitle = parentSpec ? parentSpec.title : spec.parent;
            parentHtml = `<div class="parent-link">상위 스펙: <a data-focus-spec="${escAttr(spec.parent)}">${esc(spec.parent)}: ${esc(parentTitle)}</a></div>`;
        }

        // 하위 스펙 네비게이션
        const children = allSpecs.filter(s => s.parent === spec.id);
        let childrenHtml = '';
        if (children.length > 0) {
            childrenHtml = `
            <div class="section-title">하위 스펙 (${children.length})</div>
            <div class="children-nav">
                ${children.sort((a, b) => a.id.localeCompare(b.id)).map(c => {
                    const cColor = STATUS_COLORS[c.status] || '#888';
                    return `<a class="child-nav-link" data-focus-spec="${escAttr(c.id)}">
                        <span style="color:${cColor}">●</span> ${esc(c.id)}: ${esc(c.title)}
                    </a>`;
                }).join('')}
            </div>`;
        }

        // 조건 렌더링
        let conditionsHtml = '';
        if (spec.conditions.length > 0) {
            conditionsHtml = `
            <div class="section-title">수락 조건 (${verifiedCond}/${totalCond} 달성)</div>
            <div class="progress-bar-container"><div class="progress-bar" style="width:${pct}%;background:${pct === 100 ? '#4caf50' : '#2196f3'}"></div></div>
            <ul class="conditions-list">
                ${spec.conditions.map(c => this.renderCondition(c)).join('')}
            </ul>`;
        }

        let reviewHtml = '';
        if (review.requiresManualVerification || review.autoVerifyEligible || review.blockedByOpenQuestions) {
            const reviewText = review.blockedByOpenQuestions
                ? review.statusSummary
                : review.requiresManualVerification
                    ? `수동 검증 ${review.manualVerificationItems.length}건 필요`
                    : review.statusSummary;
            reviewHtml = `
            <div class="review-callout ${review.blockedByOpenQuestions ? 'blocked' : review.requiresManualVerification ? 'manual' : 'eligible'}">
                <div class="review-callout-title">검증 상태</div>
                <div>${esc(reviewText)}</div>
                ${feedback.reviewDisposition ? `<div class="review-callout-reason">사유: ${esc(describeReviewDisposition(feedback.reviewDisposition) ?? feedback.reviewDisposition)}</div>` : ''}
                ${review.requiresManualVerification ? `<ul class="review-callout-items">${review.manualVerificationItems.map((item) => `<li><strong>${esc(item.label)}</strong>${item.reason ? ` - ${esc(item.reason)}` : ''}</li>`).join('')}</ul>` : ''}
            </div>`;
        }

        const activityHtml = renderSpecActivitySection(spec);

        let feedbackHtml = '';
        if (feedback.openQuestionCount > 0) {
            const lastAnsweredRow = feedback.lastAnsweredAt
                ? `<div class="feedback-last-answered">🕐 마지막 답변: ${esc(feedback.lastAnsweredAt)}</div>`
                : '';
            const questionsHtml = feedback.openQuestions.map((q, idx) => {
                const typeHtml = q.type ? `<span class="feedback-type">${esc(q.type)}</span>` : '';
                const whyHtml = q.why ? `<div class="feedback-why">${esc(q.why)}</div>` : '';
                const suggestionsHtml = q.answerSuggestions && q.answerSuggestions.length > 0
                    ? `<div class="feedback-suggestions">${q.answerSuggestions.map(s => `<button class="feedback-suggestion-btn" data-answer="${escAttr(s)}">${esc(s)}</button>`).join('')}</div>`
                    : '';
                const saveLabel = q.type === 'user-decision' ? '결정 저장' : '답변 저장';
                const qId = q.id || String(idx);
                return `<div class="feedback-question">
                    <div class="feedback-q-head">${typeHtml}<div class="feedback-q-text">❓ ${esc(q.question)}</div></div>
                    ${whyHtml}
                    ${suggestionsHtml}
                    <div class="feedback-answer-area">
                        <textarea class="feedback-answer-input" data-q-id="${escAttr(qId)}" data-question="${escAttr(q.question)}" placeholder="여기서 바로 답변을 입력하거나 제안 답변을 선택하세요.">${esc(drafts?.get(qId) ?? q.answer ?? '')}</textarea>
                        <div class="feedback-actions"><button class="feedback-save-btn" data-spec-id="${escAttr(spec.id)}" data-q-id="${escAttr(qId)}" data-question="${escAttr(q.question)}">${saveLabel}</button></div>
                    </div>
                </div>`;
            }).join('');
            feedbackHtml = `
            <div class="feedback-callout">
                <div class="feedback-callout-title">❓ 사용자 판단 필요</div>
                ${feedback.reviewDisposition ? `<div class="feedback-review-reason">현재 상태: ${esc(describeReviewDisposition(feedback.reviewDisposition) ?? feedback.reviewDisposition)}</div>` : ''}
                ${lastAnsweredRow}
                ${questionsHtml || '<div class="feedback-no-questions">대기 중인 미해결 질문이 없습니다.</div>'}
            </div>`;
        }

        // 코드 참조
        let codeRefsHtml = '';
        if (spec.codeRefs.length > 0) {
            codeRefsHtml = `
            <details class="code-refs-details">
                <summary class="code-refs-summary">코드 참조 (${spec.codeRefs.length})</summary>
                <ul class="code-refs">
                    ${spec.codeRefs.map(r =>
                        `<li><a class="code-ref-link" data-code-ref="${escAttr(r)}">📄 ${esc(r)}</a></li>`
                    ).join('')}
                </ul>
            </details>`;
        }

        // GitHub Refs
        let githubRefsHtml = '';
        if (spec.githubRefs && spec.githubRefs.length > 0) {
            githubRefsHtml = `
            <div class="section-title">GitHub</div>
            <div class="github-refs">
                ${spec.githubRefs.map((ref: GitHubRef) => {
                    const icon = ref.type === 'issue' ? '⚑' : ref.type === 'pr' ? '⟳' : '◉';
                    const label = ref.title ? `#${ref.number} ${ref.title}` : `#${ref.number}`;
                    if (ref.url) {
                        return `<a class="github-ref-badge" data-external-url="${escAttr(ref.url)}">${icon} ${esc(label)}</a>`;
                    }
                    return `<span class="github-ref-badge">${icon} ${esc(label)}</span>`;
                }).join('')}
            </div>`;
        }

        // Doc Links
        let docLinksHtml = '';
        if (spec.docLinks && spec.docLinks.length > 0) {
            docLinksHtml = `
            <div class="section-title">관련 문서</div>
            <ul class="doc-links">
                ${spec.docLinks.map((link: DocLink) => {
                    const icon = link.type === 'doc' ? '📄' : link.type === 'reference' ? '📚' : '🔗';
                    if (link.path) {
                        return `<li><a class="doc-link-item" data-doc-path="${escAttr(link.path)}">${icon} ${esc(link.title)}</a></li>`;
                    } else if (link.url) {
                        return `<li><a class="doc-link-item" data-external-url="${escAttr(link.url)}">${icon} ${esc(link.title)}</a></li>`;
                    }
                    return `<li><span class="doc-link-item">${icon} ${esc(link.title)}</span></li>`;
                }).join('')}
            </ul>`;
        }

        // 태그
        const tagsHtml = spec.tags.map(t => `<span class="tag">${esc(t)}</span>`).join(' ');

        return `
        <div class="spec-card${focusedClass}" id="spec-${esc(spec.id)}" data-spec-id="${escAttr(spec.id)}">
            ${parentHtml}
            <div class="spec-header">
                <span class="spec-id" data-open-spec="${escAttr(spec.id)}">${esc(spec.id)}</span>
                <${headingTag} class="spec-title">${esc(spec.title)}</${headingTag}>
            </div>
            <div class="spec-meta">
                <span class="status-badge" style="background:${color}" data-status-badge-for="${escAttr(spec.id)}">${spec.status}</span>
                ${tagsHtml}
                ${updatedAt ? `<span class="meta-date">업데이트: ${updatedAt}</span>` : ''}
            </div>
            <div class="spec-description">${esc(spec.description)}</div>
            ${feedbackHtml}
            ${reviewHtml}
            ${activityHtml}
            ${conditionsHtml}
            ${codeRefsHtml}
            ${githubRefsHtml}
            ${docLinksHtml}
            ${childrenHtml}
        </div>`;
    }

    /** 조건 항목 렌더링 */
    private renderCondition(cond: Condition): string {
        const color = STATUS_COLORS[cond.status] || '#888';
        const manualVerificationItems = getConditionManualVerificationItems(cond);

        let refsHtml = '';
        if (cond.codeRefs.length > 0) {
            refsHtml = `<details class="condition-refs-details">
                <summary class="condition-refs-summary">코드 참조 (${cond.codeRefs.length})</summary>
                <div class="condition-refs">
                    ${cond.codeRefs.map(r =>
                        `<a data-code-ref="${escAttr(r)}">📄 ${esc(r)}</a>`
                    ).join('')}
                </div>
            </details>`;
        }

        return `
        <li class="condition-item">
            <div class="condition-status-dot" style="background:${color}" title="${cond.status}"></div>
            <div class="condition-body">
                <span class="condition-id">${esc(cond.id)}</span>
                <span class="condition-desc">${esc(cond.description)}</span>
                ${manualVerificationItems.length > 0 ? `<div class="condition-manual">⚠ 수동 검증${manualVerificationItems[0]?.reason ? ` - ${esc(manualVerificationItems[0].reason)}` : ''}</div>` : ''}
                ${refsHtml}
            </div>
        </li>`;
    }

    /** 에러 HTML */
    private getErrorHtml(message: string): string {
        return /*html*/ `<!DOCTYPE html>
<html><head><style>
body {
    font-family: var(--vscode-font-family);
    color: var(--vscode-errorForeground, #f48771);
    padding: 32px;
    font-size: 14px;
}
</style></head><body>
<p>${esc(message)}</p>
</body></html>`;
    }

    /** 메시지 핸들링 */
    private async handleMessage(msg: { type: string; [key: string]: unknown }): Promise<void> {
        switch (msg.type) {
            case 'focusSpec':
                this.focusSpec(msg.specId as string);
                break;
            case 'showAll':
                this.showAll();
                break;
            case 'openCodeRef':
                await this.openCodeRef(msg.codeRef as string);
                break;
            case 'openSpec':
                await this.openSpecFile(msg.specId as string);
                break;
            case 'openExternal':
                await vscode.env.openExternal(vscode.Uri.parse(msg.url as string));
                break;
            case 'openDocLink':
                await this.openDocLink(msg.path as string);
                break;
            case 'draftAnswer': {
                const specId = msg.specId as string;
                const qId = msg.questionId as string;
                const text = msg.text as string;
                if (!this.draftAnswers.has(specId)) {
                    this.draftAnswers.set(specId, new Map());
                }
                if (text) {
                    this.draftAnswers.get(specId)!.set(qId, text);
                } else {
                    this.draftAnswers.get(specId)!.delete(qId);
                }
                break;
            }
            case 'answerQuestion':
                await this.answerQuestion(
                    msg.specId as string,
                    msg.questionId as string,
                    msg.questionText as string,
                    msg.answer as string,
                );
                break;
        }
    }

    private async answerQuestion(specId: string, questionId: string, questionText: string, answer: string): Promise<void> {
        if (!specId) {
            vscode.window.showWarningMessage('답변을 저장할 스펙을 찾을 수 없습니다.');
            return;
        }

        const graph = await this.loader.getGraph();
        const spec = graph.specs.find(s => s.id === specId);
        if (!spec) {
            vscode.window.showWarningMessage(`스펙을 찾을 수 없습니다: ${specId}`);
            return;
        }

        const feedback = getUserFeedbackState(spec);
        const question = feedback.questions.find(q => (questionId && q.id === questionId) || q.question === questionText);
        if (!question) {
            vscode.window.showWarningMessage('저장할 질문을 찾을 수 없습니다. 새로고침 후 다시 시도하세요.');
            return;
        }

        await saveQuestionAnswer(this.loader.specsDirectory, specId, question, answer);
        // 저장 성공 후 드래프트 제거
        this.draftAnswers.get(specId)?.delete(question.id || questionId);
        await this.loader.reload();
        vscode.window.showInformationMessage(`질문 응답을 저장했습니다: ${specId}`);
    }

    private async openCodeRef(codeRef: string): Promise<void> {
        const pathMod = require('path');
        const parts = codeRef.split('#');
        const filePath = parts[0];
        const lineRange = parts[1];

        const fullPath = vscode.Uri.file(pathMod.join(this.workspaceRoot, filePath));

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
                viewColumn: vscode.ViewColumn.One,
            });
        } catch {
            vscode.window.showWarningMessage(`파일을 찾을 수 없습니다: ${filePath}`);
        }
    }

    private async openDocLink(relativePath: string): Promise<void> {
        const pathMod = require('path');
        const fullPath = vscode.Uri.file(pathMod.join(this.workspaceRoot, relativePath));
        try {
            const doc = await vscode.workspace.openTextDocument(fullPath);
            await vscode.window.showTextDocument(doc, { preview: true });
        } catch {
            vscode.window.showWarningMessage(`문서를 찾을 수 없습니다: ${relativePath}`);
        }
    }

    private async openSpecFile(specId: string): Promise<void> {
        const pathMod = require('path');
        const filePath = vscode.Uri.file(
            pathMod.join(this.loader.specsDirectory, `${specId}.json`)
        );
        try {
            const doc = await vscode.workspace.openTextDocument(filePath);
            await vscode.window.showTextDocument(doc, { preview: true });
        } catch {
            vscode.window.showWarningMessage(`스펙 파일을 찾을 수 없습니다: ${specId}.json`);
        }
    }

    dispose(): void {
        SpecViewProvider.currentPanel = undefined;

        this.panel.dispose();

        while (this.disposables.length) {
            const d = this.disposables.pop();
            if (d) { d.dispose(); }
        }
    }
}

/** HTML 이스케이프 */
function esc(str: string): string {
    if (!str) { return ''; }
    return str.replace(/&/g, '&amp;')
              .replace(/</g, '&lt;')
              .replace(/>/g, '&gt;')
              .replace(/"/g, '&quot;');
}

/** 속성 이스케이프 */
function escAttr(str: string): string {
    return esc(str).replace(/'/g, '&#039;');
}

/** Nonce 생성 */
function getNonce(): string {
    let text = '';
    const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    for (let i = 0; i < 32; i++) {
        text += chars.charAt(Math.floor(Math.random() * chars.length));
    }
    return text;
}

/** 날짜 포맷 */
function formatDate(isoStr: string): string {
    try {
        const date = new Date(isoStr);
        return date.toLocaleDateString('ko-KR', {
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
        });
    } catch {
        return isoStr;
    }
}
