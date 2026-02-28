/**
 * Spec Graph VSCode Extension
 *
 * 기능 의존성 그래프 시각화
 * - 사이드바 트리뷰: 스펙 계층 구조
 * - Webview 그래프: Cytoscape.js 기반 DAG 렌더링
 * - 상세 패널: 노드 선택 시 상세 정보
 * - 코드 참조 이동: codeRefs 클릭 시 에디터에서 열기
 */
import * as vscode from 'vscode';
import { SpecLoader } from './specLoader';
import { SpecTreeProvider } from './specTreeProvider';
import { GraphPanel } from './graphPanel';
import { DetailViewProvider } from './detailViewProvider';
import { SpecStatus } from './types';

let specLoader: SpecLoader;

export function activate(context: vscode.ExtensionContext): void {
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (!workspaceFolders || workspaceFolders.length === 0) {
        return;
    }

    const workspaceRoot = workspaceFolders[0].uri.fsPath;

    // 1. SpecLoader 초기화
    specLoader = new SpecLoader(workspaceRoot);
    context.subscriptions.push({ dispose: () => specLoader.dispose() });

    // 2. TreeView Provider 등록
    const treeProvider = new SpecTreeProvider(specLoader);
    const treeView = vscode.window.createTreeView('specTree', {
        treeDataProvider: treeProvider,
        showCollapseAll: true,
    });
    context.subscriptions.push(treeView);

    // 3. Detail Webview Provider 등록
    const detailProvider = new DetailViewProvider(
        context.extensionUri,
        specLoader,
        workspaceRoot,
    );
    context.subscriptions.push(
        vscode.window.registerWebviewViewProvider(
            DetailViewProvider.viewType,
            detailProvider,
        ),
    );

    // 4. 명령어 등록

    // 그래프 열기
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.openGraph', () => {
            GraphPanel.createOrShow(context.extensionUri, specLoader, workspaceRoot);
        }),
    );

    // 새로고침
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.refresh', async () => {
            await specLoader.reload();
            vscode.window.showInformationMessage('Spec Graph 새로고침 완료');
        }),
    );

    // 노드 포커스 (트리 클릭 → 그래프 포커스 + 상세 패널)
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.focusNode', (nodeId: string) => {
            if (GraphPanel.currentPanel) {
                GraphPanel.currentPanel.focusNode(nodeId);
            }
            detailProvider.showNode(nodeId);
        }),
    );

    // 상세 표시 (그래프에서 호출)
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.showDetail', (nodeId: string) => {
            detailProvider.showNode(nodeId);
        }),
    );

    // 스펙 파일 열기
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.openSpec', async (item: any) => {
            const specId = item?.spec?.id || item;
            if (!specId) { return; }

            const filePath = vscode.Uri.file(
                require('path').join(workspaceRoot, 'docs', 'specs', `${specId}.json`)
            );
            try {
                const doc = await vscode.workspace.openTextDocument(filePath);
                await vscode.window.showTextDocument(doc);
            } catch {
                vscode.window.showWarningMessage(`스펙 파일을 찾을 수 없습니다: ${specId}.json`);
            }
        }),
    );

    // 상태별 필터
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.filterByStatus', async () => {
            const statuses: (SpecStatus | 'all')[] = ['all', 'draft', 'active', 'needs-review', 'verified', 'deprecated'];
            const items = statuses.map(s => ({
                label: s === 'all' ? '$(list-flat) 전체' : `$(${getStatusIcon(s as SpecStatus)}) ${s}`,
                value: s,
            }));

            const picked = await vscode.window.showQuickPick(items, {
                placeHolder: '표시할 상태를 선택하세요',
            });

            if (picked) {
                const value = (picked as any).value;
                treeProvider.setStatusFilter(value === 'all' ? null : value);
            }
        }),
    );

    // 태그별 필터
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.filterByTag', async () => {
            const tags = specLoader.getAllTags();
            const items = [
                { label: '$(list-flat) 전체', value: 'all' },
                ...tags.map(t => ({ label: `$(tag) ${t}`, value: t })),
            ];

            const picked = await vscode.window.showQuickPick(items, {
                placeHolder: '표시할 태그를 선택하세요',
            });

            if (picked) {
                const value = (picked as any).value;
                treeProvider.setTagFilter(value === 'all' ? null : value);
            }
        }),
    );

    // 5. 초기 로드
    specLoader.load().then(() => {
        vscode.window.setStatusBarMessage('$(type-hierarchy) Spec Graph 로드 완료', 3000);
    });
}

function getStatusIcon(status: SpecStatus): string {
    const map: Record<SpecStatus, string> = {
        'draft': 'circle-outline',
        'active': 'circle-filled',
        'needs-review': 'warning',
        'verified': 'check',
        'deprecated': 'close',
    };
    return map[status] || 'circle-outline';
}

export function deactivate(): void {
    // cleanup handled by disposables
}
