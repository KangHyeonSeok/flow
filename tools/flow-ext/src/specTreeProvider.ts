/**
 * SpecTreeProvider - 스펙 계층 트리뷰 (Activity Bar 사이드바)
 *
 * Feature → Condition 계층 구조를 TreeView로 표현
 * 상태별 아이콘/색상 표시, 필터링 지원
 */
import * as vscode from 'vscode';
import * as path from 'path';
import { SpecLoader } from './specLoader';
import { Spec, Condition, SpecStatus, STATUS_ICONS } from './types';
import { BrokenSpecDiagRecord } from './brokenSpecDiag';
import { getUserFeedbackState } from './reviewState';

export class SpecTreeProvider implements vscode.TreeDataProvider<SpecTreeItem> {
    private _onDidChangeTreeData = new vscode.EventEmitter<SpecTreeItem | undefined | void>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    private statusFilter: SpecStatus | null = null;
    private tagFilter: string | null = null;

    constructor(private loader: SpecLoader) {
        loader.onDidChange(() => this.refresh());
        loader.onDidStateChange(() => this.refresh());
    }

    refresh(): void {
        this._onDidChangeTreeData.fire();
    }

    setStatusFilter(status: SpecStatus | null): void {
        this.statusFilter = status;
        this.refresh();
    }

    setTagFilter(tag: string | null): void {
        this.tagFilter = tag;
        this.refresh();
    }

    getTreeItem(element: SpecTreeItem): vscode.TreeItem {
        return element;
    }

    async getChildren(element?: SpecTreeItem): Promise<SpecTreeItem[]> {
        const graph = await this.loader.getGraph();
        const specs = graph.specs;
        const visibleSpecs = this.applyFilters(specs);
        const visibleSpecIds = new Set(visibleSpecs.map(spec => spec.id));

        if (!element) {
            const items: SpecTreeItem[] = [];

            // 손상 스펙 항목을 트리 최상단에 표시 (F-026)
            const brokenSpecs = this.loader.getBrokenSpecs();
            for (const broken of brokenSpecs) {
                items.push(new BrokenSpecTreeItem(broken));
            }

            // 루트: parent가 없거나, 현재 표시 집합에 parent가 없는 스펙들
            const rootSpecs = visibleSpecs.filter(spec => !spec.parent || !visibleSpecIds.has(spec.parent));
            for (const s of rootSpecs.sort((a, b) => a.id.localeCompare(b.id))) {
                items.push(new SpecTreeItem(s));
            }

            return items;
        }

        if (element.spec) {
            const items: SpecTreeItem[] = [];

            // 하위 스펙 (children)
            const childSpecs = visibleSpecs.filter(s => s.parent === element.spec!.id);
            for (const child of childSpecs.sort((a, b) => a.id.localeCompare(b.id))) {
                items.push(new SpecTreeItem(child));
            }

            // 조건 노드
            for (const cond of element.spec.conditions) {
                items.push(new SpecTreeItem(undefined, cond, element.spec.id));
            }

            return items;
        }

        return [];
    }

    private applyFilters(specs: Spec[]): Spec[] {
        let filtered = specs;

        if (this.statusFilter) {
            filtered = filtered.filter(s => s.status === this.statusFilter);
        }

        if (this.tagFilter) {
            filtered = filtered.filter(s => s.tags.includes(this.tagFilter!));
        }

        return filtered;
    }

    dispose(): void {
        this._onDidChangeTreeData.dispose();
    }
}

export class SpecTreeItem extends vscode.TreeItem {
    constructor(
        public readonly spec?: Spec,
        public readonly condition?: Condition,
        public readonly parentSpecId?: string,
    ) {
        const label = spec
            ? `${spec.id}: ${spec.title}`
            : condition
                ? condition.id
                : 'Unknown';

        const hasChildren = spec
            ? true // specs can have children or conditions
            : false;

        super(
            label,
            hasChildren
                ? vscode.TreeItemCollapsibleState.Collapsed
                : vscode.TreeItemCollapsibleState.None,
        );

        const status = spec?.status ?? condition?.status ?? 'draft';
        const nodeType = spec ? 'feature' : 'condition';

        // 설명: 상태 + 태그 + 미해결 질문 수 (C2)
        if (spec) {
            const feedback = getUserFeedbackState(spec);
            const questionBadge = feedback.openQuestionCount > 0
                ? ` 질문 ${feedback.openQuestionCount}개`
                : '';
            this.description = `[${status}]${spec.tags.length > 0 ? ' ' + spec.tags.join(', ') : ''}${questionBadge}`;
        } else if (condition) {
            this.description = `[${status}]`;
            this.tooltip = new vscode.MarkdownString(condition.description);
        }

        // 상태 아이콘 (C2: 사용자 입력 대기 시 별도 아이콘)
        const isWaitingUserInput = spec
            ? (() => { const fb = getUserFeedbackState(spec); return fb.openQuestionCount > 0; })()
            : false;

        if (isWaitingUserInput) {
            this.iconPath = new vscode.ThemeIcon(
                'comment-discussion',
                new vscode.ThemeColor('inputValidation.errorBorder'),
            );
        } else {
            this.iconPath = new vscode.ThemeIcon(
                STATUS_ICONS[status as SpecStatus] || 'circle-outline',
                new vscode.ThemeColor(this.getStatusThemeColor(status as SpecStatus)),
            );
        }

        // 컨텍스트
        this.contextValue = nodeType;

        // 클릭 시 상세 보기 + 그래프 포커스
        const id = spec?.id ?? condition?.id;
        if (id) {
            this.command = {
                command: 'specGraph.focusNode',
                title: '노드 포커스',
                arguments: [id],
            };
        }
    }

    private getStatusThemeColor(status: SpecStatus): string {
        switch (status) {
            case 'verified': return 'testing.iconPassed';
            case 'working': return 'charts.blue';
            case 'needs-review': return 'editorWarning.foreground';
            case 'deprecated': return 'testing.iconFailed';
            case 'draft':
            default: return 'disabledForeground';
        }
    }
}

/** 손상 스펙 진단 트리 항목 (F-026) */
export class BrokenSpecTreeItem extends SpecTreeItem {
    constructor(public readonly record: BrokenSpecDiagRecord) {
        super(); // no spec/condition — renders as 'Unknown' base, then we override below

        this.label = record.specId;
        const locInfo = record.line != null
            ? ` (line ${record.line}${record.column != null ? `:${record.column}` : ''})`
            : '';
        this.description = `JSON 파싱 오류${locInfo}`;
        this.tooltip = new vscode.MarkdownString(
            `**${record.specId}** — 손상 스펙\n\n` +
            `**오류**: ${record.errorMessage}\n\n` +
            `**파일**: ${record.filePath}\n\n` +
            `**감지**: ${record.detectedAt}`
        );
        this.iconPath = new vscode.ThemeIcon('error', new vscode.ThemeColor('errorForeground'));
        this.contextValue = 'brokenSpec';
        this.collapsibleState = vscode.TreeItemCollapsibleState.None;
        this.command = {
            command: 'specGraph.openSpec',
            title: '파일 열기',
            arguments: [record.specId],
        };
    }
}
