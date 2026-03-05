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
import * as cp from 'child_process';
import { SpecLoader } from './specLoader';
import { SpecTreeProvider } from './specTreeProvider';
import { GraphPanel } from './graphPanel';
import { DetailViewProvider } from './detailViewProvider';
import { SpecViewProvider } from './specViewProvider';
import { KanbanPanel } from './kanbanPanel';
import { SpecStatus } from './types';

let specLoader: SpecLoader;
let output: vscode.OutputChannel;

export function activate(context: vscode.ExtensionContext): void {
    output = vscode.window.createOutputChannel('Spec Graph');
    context.subscriptions.push(output);
    output.appendLine('[activate] Spec Graph extension activated');

    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (!workspaceFolders || workspaceFolders.length === 0) {
        output.appendLine('[activate] No workspace folder found');
        return;
    }

    const workspaceRoot = workspaceFolders[0].uri.fsPath;
    output.appendLine(`[activate] workspaceRoot=${workspaceRoot}`);

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
            output.appendLine('[command] specGraph.openGraph');
            GraphPanel.createOrShow(context.extensionUri, specLoader, workspaceRoot);
        }),
    );

    // 웹 렌더링 프리뷰 (별도 패널)
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.openGraphPreview', () => {
            output.appendLine('[command] specGraph.openGraphPreview');
            GraphPanel.openPreview(context.extensionUri, specLoader, workspaceRoot);
        }),
    );

    // 디버그 정보 출력
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.debugInfo', async () => {
            const graph = await specLoader.getGraph();
            const msg = [
                `[debug] specsDirectory=${specLoader.specsDirectory}`,
                `[debug] specs=${graph.specs.length}`,
                `[debug] nodes=${graph.nodes.length}`,
                `[debug] edges=${graph.edges.length}`,
            ].join('\n');
            output.appendLine(msg);
            output.show(true);
            vscode.window.showInformationMessage(`Spec Graph: specs=${graph.specs.length}, nodes=${graph.nodes.length}, edges=${graph.edges.length}`);
        }),
    );

    // 새로고침
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.refresh', async () => {
            await specLoader.reload();
            const gitRoot = findGitRoot(specLoader.specsDirectory);
            if (gitRoot) { await updatePendingPushContext(gitRoot); }
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
                require('path').join(specLoader.specsDirectory, `${specId}.json`)
            );
            try {
                const doc = await vscode.workspace.openTextDocument(filePath);
                await vscode.window.showTextDocument(doc);
            } catch {
                vscode.window.showWarningMessage(`스펙 파일을 찾을 수 없습니다: ${specId}.json`);
            }
        }),
    );

    // 스펙 삭제
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.deleteSpec', async (item: any) => {
            const specId = item?.spec?.id || (typeof item === 'string' ? item : undefined);
            if (!specId) { return; }

            const confirm = await vscode.window.showWarningMessage(
                `스펙 "${specId}"을(를) 삭제하시겠습니까? 이 작업은 되돌릴 수 없습니다.`,
                { modal: true },
                '삭제',
            );

            if (confirm !== '삭제') { return; }

            const filePath = require('path').join(specLoader.specsDirectory, `${specId}.json`);
            try {
                require('fs').unlinkSync(filePath);
                await specLoader.reload();
                vscode.window.showInformationMessage(`스펙 "${specId}" 삭제 완료`);
            } catch (err) {
                vscode.window.showErrorMessage(`스펙 삭제 실패: ${String(err)}`);
            }
        }),
    );

    // 상태별 필터
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.filterByStatus', async () => {
            const statuses: (SpecStatus | 'all')[] = ['all', 'draft', 'requested', 'context-gathering', 'plan', 'active', 'needs-review', 'verified', 'deprecated'];
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

    // 스펙 문서 뷰 열기 (전체)
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.openSpecView', () => {
            output.appendLine('[command] specGraph.openSpecView');
            SpecViewProvider.createOrShow(context.extensionUri, specLoader, workspaceRoot);
        }),
    );

    // 스펙 문서 뷰 열기 (선택된 노드 포커스)
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.openSpecViewFocused', (item: any) => {
            output.appendLine('[command] specGraph.openSpecViewFocused');
            const specId = item?.spec?.id || item;
            const panel = SpecViewProvider.createOrShow(context.extensionUri, specLoader, workspaceRoot);
            if (specId && typeof specId === 'string') {
                panel.focusSpec(specId);
            }
        }),
    );

    // 칸반 보드 열기
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.openKanban', () => {
            output.appendLine('[command] specGraph.openKanban');
            KanbanPanel.createOrShow(context.extensionUri, specLoader, workspaceRoot);
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

    // 스펙 push
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.pushSpecs', async () => {
            output.appendLine('[command] specGraph.pushSpecs');

            const specsDir = specLoader.specsDirectory;
            const gitRoot = findGitRoot(specsDir);
            if (!gitRoot) {
                vscode.window.showErrorMessage(`git 저장소를 찾을 수 없습니다: ${specsDir}`);
                return;
            }

            // 미push 커밋 및 미커밋 변경 확인
            const pending = await checkPendingPush(gitRoot);
            const commitMsg = `feat: spec update [${new Date().toISOString().slice(0, 16)} UTC]`;

            // 변경 없음 + 미push 커밋 없음
            if (!pending.uncommitted && pending.unpushed === 0) {
                vscode.window.showInformationMessage('Spec Graph: Already up to date.');
                return;
            }

            const detail = [
                pending.uncommitted ? '미커밋 변경 있음' : '',
                pending.unpushed > 0 ? `미push 커밋 ${pending.unpushed}개` : '',
            ].filter(Boolean).join(', ');

            const confirmed = await vscode.window.showInformationMessage(
                `스펙을 원격 저장소에 push합니다. (${detail})`,
                { modal: false },
                'Push',
            );
            if (confirmed !== 'Push') { return; }

            try {
                if (pending.uncommitted) {
                    await execGitAsync(['add', '-A'], gitRoot);
                    // staged diff 확인
                    const diff = await execGitAsync(['diff', '--cached', '--quiet'], gitRoot).catch(() => null);
                    if (diff === null) {
                        // staged 변경 있음 (exit code 1)
                        await execGitAsyncOrThrow(['commit', '-m', commitMsg], gitRoot);
                    }
                }

                await execGitAsyncOrThrow(['push'], gitRoot);

                const hash = await execGitAsync(['rev-parse', '--short', 'HEAD'], gitRoot);
                output.appendLine(`[push] 완료: ${hash?.trim()} "${commitMsg}"`);

                await specLoader.reload();
                await updatePendingPushContext(gitRoot);
                vscode.window.showInformationMessage(`Spec Graph: push 완료 (${hash?.trim() ?? 'unknown'})`);

            } catch (err) {
                const msg = String(err);
                output.appendLine(`[push] 실패: ${msg}`);
                vscode.window.showErrorMessage(`Spec Graph push 실패: ${msg}`);
            }
        }),
    );

    // 5. 초기 로드
    specLoader.load().then(async () => {
        const graph = specLoader.getSpecs();
        output.appendLine(`[load] completed specs=${graph.length}, specsDirectory=${specLoader.specsDirectory}`);
        vscode.window.setStatusBarMessage('$(type-hierarchy) Spec Graph 로드 완료', 3000);

        const gitRoot = findGitRoot(specLoader.specsDirectory);
        if (gitRoot) { await updatePendingPushContext(gitRoot); }
    }).catch((err) => {
        output.appendLine(`[load] failed: ${String(err)}`);
        vscode.window.showErrorMessage(`Spec Graph 로드 실패: ${String(err)}`);
    });
}

function getStatusIcon(status: SpecStatus): string {
    const map: Record<SpecStatus, string> = {
        'draft': 'circle-outline',
        'requested': 'send',
        'context-gathering': 'search',
        'plan': 'list-tree',
        'active': 'circle-filled',
        'needs-review': 'warning',
        'verified': 'check',
        'deprecated': 'close',
    };
    return map[status] || 'circle-outline';
}

/** startPath에서 위로 올라가며 .git 디렉토리가 있는 git 루트를 반환한다. */
function findGitRoot(startPath: string): string | null {
    const path = require('path') as typeof import('path');
    const fs = require('fs') as typeof import('fs');
    let current = startPath;
    while (current) {
        if (fs.existsSync(path.join(current, '.git'))) { return current; }
        const parent = path.dirname(current);
        if (parent === current) { break; }
        current = parent;
    }
    return null;
}

/** git 명령어를 실행하고 stdout을 반환한다. 실패 시 null 반환 (throw 없음). */
function execGitAsync(args: string[], cwd: string): Promise<string | null> {
    return new Promise((resolve) => {
        cp.execFile('git', args, { cwd }, (err, stdout) => {
            resolve(err ? null : stdout.trim());
        });
    });
}

/** git 명령어를 실행하고 stdout을 반환한다. 실패(exit ≠ 0) 시 error를 throw한다. */
function execGitAsyncOrThrow(args: string[], cwd: string): Promise<string> {
    return new Promise((resolve, reject) => {
        cp.execFile('git', args, { cwd }, (err, stdout, stderr) => {
            if (err) { reject(stderr.trim() || String(err)); }
            else { resolve(stdout.trim()); }
        });
    });
}

/** 미push 커밋 수와 미커밋 변경 여부를 반환한다. */
async function checkPendingPush(gitRoot: string): Promise<{ unpushed: number; uncommitted: boolean }> {
    // 미push 커밋 수 (tracking branch 없으면 0 처리)
    const unpushedStr = await execGitAsync(['rev-list', '@{u}..HEAD', '--count'], gitRoot);
    const unpushed = unpushedStr !== null ? (parseInt(unpushedStr, 10) || 0) : 0;

    // 미커밋 변경 여부 (tracked + untracked 포함)
    const status = await execGitAsync(['status', '--porcelain'], gitRoot);
    const uncommitted = status !== null && status.trim().length > 0;

    return { unpushed, uncommitted };
}

/** pending push 상태를 VSCode context에 업데이트한다. */
async function updatePendingPushContext(gitRoot: string): Promise<void> {
    const { unpushed, uncommitted } = await checkPendingPush(gitRoot);
    const total = unpushed + (uncommitted ? 1 : 0);
    await vscode.commands.executeCommand('setContext', 'specGraph.pendingPushCount', total);
}

export function deactivate(): void {
    // cleanup handled by disposables
}
