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

export class SpecTreeProvider implements vscode.TreeDataProvider<SpecTreeItem> {
    private _onDidChangeTreeData = new vscode.EventEmitter<SpecTreeItem | undefined | void>();
    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

    private statusFilter: SpecStatus | null = null;
    private tagFilter: string | null = null;

    constructor(private loader: SpecLoader) {
        loader.onDidChange(() => this.refresh());
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

        if (!element) {
            // 루트: parent가 null인 스펙들
            let rootSpecs = specs.filter(s => !s.parent);

            // 필터 적용
            rootSpecs = this.applyFilters(rootSpecs);

            return rootSpecs
                .sort((a, b) => a.id.localeCompare(b.id))
                .map(s => new SpecTreeItem(s));
        }

        if (element.spec) {
            const items: SpecTreeItem[] = [];

            // 하위 스펙 (children)
            let childSpecs = specs.filter(s => s.parent === element.spec!.id);
            childSpecs = this.applyFilters(childSpecs);
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

        // 설명: 상태 + 태그
        if (spec) {
            this.description = `[${status}]${spec.tags.length > 0 ? ' ' + spec.tags.join(', ') : ''}`;
        } else if (condition) {
            this.description = `[${status}]`;
            this.tooltip = new vscode.MarkdownString(condition.description);
        }

        // 상태 아이콘
        this.iconPath = new vscode.ThemeIcon(
            STATUS_ICONS[status as SpecStatus] || 'circle-outline',
            new vscode.ThemeColor(this.getStatusThemeColor(status as SpecStatus)),
        );

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
            case 'active': return 'charts.blue';
            case 'needs-review': return 'editorWarning.foreground';
            case 'deprecated': return 'testing.iconFailed';
            case 'draft':
            default: return 'disabledForeground';
        }
    }
}
