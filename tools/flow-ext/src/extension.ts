/**
 * Flow VSCode Extension
 *
 * Flow 통합 확장
 * - 사이드바 트리뷰: 스펙 계층 구조
 * - Webview 그래프: Cytoscape.js 기반 DAG 렌더링
 * - 상세 패널: 노드 선택 시 상세 정보
 * - 코드 참조 이동: codeRefs 클릭 시 에디터에서 열기
 * - Flow Runner 데몬 시작/중지
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
let daemonStatusBarItem: vscode.StatusBarItem;
let daemonPollTimer: NodeJS.Timeout | undefined;

export function activate(context: vscode.ExtensionContext): void {
    output = vscode.window.createOutputChannel('Flow');
    context.subscriptions.push(output);
    output.appendLine('[activate] Flow extension activated');

    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (!workspaceFolders || workspaceFolders.length === 0) {
        output.appendLine('[activate] No workspace folder found');
        return;
    }

    const workspaceRoot = workspaceFolders[0].uri.fsPath;
    output.appendLine(`[activate] workspaceRoot=${workspaceRoot}`);

    // 0. Daemon StatusBar 아이템
    daemonStatusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 10);
    daemonStatusBarItem.command = 'flowExt.startDaemon';
    context.subscriptions.push(daemonStatusBarItem);
    updateDaemonStatusBar(false);
    daemonStatusBarItem.show();

    // 주기적 데몬 상태 폴링 (10초)
    daemonPollTimer = setInterval(async () => {
        const running = await checkDaemonRunning(workspaceRoot);
        await setDaemonRunningContext(running);
    }, 10_000);
    context.subscriptions.push({ dispose: () => { if (daemonPollTimer) { clearInterval(daemonPollTimer); } } });

    // 초기 상태 확인
    checkDaemonRunning(workspaceRoot).then(running => setDaemonRunningContext(running));

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
        vscode.window.setStatusBarMessage('$(type-hierarchy) Flow 로드 완료', 3000);

        const gitRoot = findGitRoot(specLoader.specsDirectory);
        if (gitRoot) { await updatePendingPushContext(gitRoot); }
    }).catch((err) => {
        output.appendLine(`[load] failed: ${String(err)}`);
        vscode.window.showErrorMessage(`Flow 로드 실패: ${String(err)}`);
    });

    // Flow Runner 데몬 시작
    context.subscriptions.push(
        vscode.commands.registerCommand('flowExt.startDaemon', async () => {
            output.appendLine('[command] flowExt.startDaemon');
            const flowExe = resolveFlowExecutable(workspaceRoot);
            if (!flowExe) {
                vscode.window.showErrorMessage('flow CLI를 찾을 수 없습니다. PATH 또는 flow.executablePath 설정을 확인하세요.');
                return;
            }

            const terminal = vscode.window.createTerminal({
                name: 'Flow Runner',
                hideFromUser: false,
            });
            terminal.show(false);
            terminal.sendText(`${flowExe} runner-start --daemon`);
            output.appendLine(`[daemon] 시작: ${flowExe} runner-start --daemon`);

            // 잠시 대기 후 상태 확인
            await new Promise(r => setTimeout(r, 2000));
            const running = await checkDaemonRunning(workspaceRoot);
            await setDaemonRunningContext(running);
            if (running) {
                vscode.window.showInformationMessage('Flow Runner 데몬이 시작되었습니다.');
            }
        }),
    );

    // Flow Runner 데몬 중지
    context.subscriptions.push(
        vscode.commands.registerCommand('flowExt.stopDaemon', async () => {
            output.appendLine('[command] flowExt.stopDaemon');
            const flowExe = resolveFlowExecutable(workspaceRoot);
            if (!flowExe) {
                vscode.window.showErrorMessage('flow CLI를 찾을 수 없습니다.');
                return;
            }

            try {
                await execCommandAsync(flowExe, ['runner-stop'], workspaceRoot);
                await setDaemonRunningContext(false);
                vscode.window.showInformationMessage('Flow Runner 데몬이 중지되었습니다.');
            } catch (err) {
                const msg = String(err);
                output.appendLine(`[daemon] 중지 실패: ${msg}`);
                vscode.window.showErrorMessage(`Flow Runner 중지 실패: ${msg}`);
            }
        }),
    );
}

/** flow CLI 실행 경로를 반환한다. 설정값 → PATH 순으로 탐색. */
function resolveFlowExecutable(workspaceRoot: string): string | null {
    const path = require('path') as typeof import('path');
    const fs = require('fs') as typeof import('fs');

    // 1. 사용자 설정
    const configPath = vscode.workspace.getConfiguration('flow').get<string>('executablePath', '');
    if (configPath && fs.existsSync(configPath)) { return configPath; }

    // 2. 워크스페이스 루트의 flow.ps1 (Windows)
    const flowPs1 = path.join(workspaceRoot, 'flow.ps1');
    if (process.platform === 'win32' && fs.existsSync(flowPs1)) {
        return `powershell -ExecutionPolicy Bypass -File "${flowPs1}"`;
    }

    // 3. PATH에서 flow 탐색
    try {
        const which = require('child_process').execSync(
            process.platform === 'win32' ? 'where flow' : 'which flow',
            { encoding: 'utf8' }
        ).trim().split('\n')[0].trim();
        if (which && fs.existsSync(which)) { return which; }
    } catch { /* not found */ }

    return null;
}

/** `flow runner-status` JSON 결과를 파싱하여 데몬 실행 여부를 반환한다. */
async function checkDaemonRunning(workspaceRoot: string): Promise<boolean> {
    const flowExe = resolveFlowExecutable(workspaceRoot);
    if (!flowExe) { return false; }
    try {
        const stdout = await execCommandAsync(flowExe, ['runner-status'], workspaceRoot);
        const json = JSON.parse(stdout);
        return json?.data?.running === true;
    } catch {
        return false;
    }
}

/** VSCode context와 상태 바를 데몬 실행 상태에 따라 업데이트한다. */
async function setDaemonRunningContext(running: boolean): Promise<void> {
    await vscode.commands.executeCommand('setContext', 'flowExt.daemonRunning', running);
    updateDaemonStatusBar(running);
}

/** 상태 바 아이템의 텍스트/툴팁/커맨드를 업데이트한다. */
function updateDaemonStatusBar(running: boolean): void {
    if (!daemonStatusBarItem) { return; }
    if (running) {
        daemonStatusBarItem.text = '$(sync~spin) Flow Runner';
        daemonStatusBarItem.tooltip = 'Flow Runner 데몬 실행 중 — 클릭하여 중지';
        daemonStatusBarItem.command = 'flowExt.stopDaemon';
        daemonStatusBarItem.color = new vscode.ThemeColor('statusBarItem.warningForeground');
    } else {
        daemonStatusBarItem.text = '$(play) Flow Runner';
        daemonStatusBarItem.tooltip = 'Flow Runner 데몬 시작';
        daemonStatusBarItem.command = 'flowExt.startDaemon';
        daemonStatusBarItem.color = undefined;
    }
}

/** 외부 명령어를 실행하고 stdout을 반환한다. 실패 시 reject. */
function execCommandAsync(exe: string, args: string[], cwd: string): Promise<string> {
    return new Promise((resolve, reject) => {
        // PowerShell 래퍼인 경우 특수 처리
        if (exe.startsWith('powershell')) {
            // exe: powershell ... -File "path" → args를 추가
            const fullCmd = `${exe} ${args.join(' ')}`;
            require('child_process').exec(fullCmd, { cwd }, (err: any, stdout: string, stderr: string) => {
                if (err) { reject(stderr.trim() || String(err)); } else { resolve(stdout.trim()); }
            });
        } else {
            cp.execFile(exe, args, { cwd }, (err, stdout, stderr) => {
                if (err) { reject(stderr.trim() || String(err)); } else { resolve(stdout.trim()); }
            });
        }
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
