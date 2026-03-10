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
import { SpecViewProvider } from './specViewProvider';
import { KanbanPanel } from './kanbanPanel';
import { SpecStatus } from './types';
import { registerWebviewTests } from './webviewTest';

let specLoader: SpecLoader;
let output: vscode.OutputChannel;
let daemonStatusBarItem: vscode.StatusBarItem;
let reloadStatusBarItem: vscode.StatusBarItem;
let daemonPollTimer: NodeJS.Timeout | undefined;
let reloadSignalWatcher: vscode.FileSystemWatcher | undefined;
const LEGACY_EXTENSION_ID = 'flow-team.spec-graph';

export function activate(context: vscode.ExtensionContext): void {
    output = vscode.window.createOutputChannel('Flow');
    context.subscriptions.push(output);
    output.appendLine('[activate] Flow extension activated');

    notifyLegacyExtensionConflict();

    const workspaceFolders = vscode.workspace.workspaceFolders;
    const workspaceRoot = workspaceFolders?.[0]?.uri.fsPath;
    output.appendLine(workspaceRoot
        ? `[activate] workspaceRoot=${workspaceRoot}`
        : '[activate] No workspace folder found');

    const requireWorkspace = async (featureName: string): Promise<string | null> => {
        const root = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
        if (root) {
            return root;
        }

        const choice = await vscode.window.showWarningMessage(
            `${featureName} 기능을 사용하려면 폴더 또는 워크스페이스를 열어야 합니다.`,
            '폴더 열기'
        );

        if (choice === '폴더 열기') {
            await vscode.commands.executeCommand('vscode.openFolder');
        }
        return null;
    };

    // 0. Daemon StatusBar 아이템
    daemonStatusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 10);
    daemonStatusBarItem.command = 'flowExt.startDaemon';
    context.subscriptions.push(daemonStatusBarItem);
    updateDaemonStatusBar(false);
    daemonStatusBarItem.show();

    // 0-1. 간편 리로드 StatusBar 아이템
    reloadStatusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 9);
    reloadStatusBarItem.text = '$(debug-restart)';
    reloadStatusBarItem.tooltip = 'Reload Flow: Flow 확장 변경 반영을 위해 확장 호스트를 다시 시작';
    reloadStatusBarItem.command = 'flowExt.reloadWindow';
    context.subscriptions.push(reloadStatusBarItem);
    reloadStatusBarItem.show();

    if (workspaceRoot) {
        const reloadSignalPattern = new vscode.RelativePattern(workspaceRoot, '.flow/flow-ext.reload.signal');
        reloadSignalWatcher = vscode.workspace.createFileSystemWatcher(reloadSignalPattern);
        const handleReloadSignal = async () => {
            output.appendLine('[reload] auto reload signal detected');
            await vscode.commands.executeCommand('flowExt.reloadWindow', { source: 'signal' });
        };
        reloadSignalWatcher.onDidCreate(handleReloadSignal);
        reloadSignalWatcher.onDidChange(handleReloadSignal);
        context.subscriptions.push(reloadSignalWatcher);

        daemonPollTimer = setInterval(async () => {
            const running = await checkDaemonRunning(workspaceRoot);
            await setDaemonRunningContext(running);
        }, 10_000);
        context.subscriptions.push({ dispose: () => { if (daemonPollTimer) { clearInterval(daemonPollTimer); } } });

        checkDaemonRunning(workspaceRoot).then(running => setDaemonRunningContext(running));

        specLoader = new SpecLoader(workspaceRoot);
        context.subscriptions.push({ dispose: () => specLoader.dispose() });
    }

    let treeProvider: SpecTreeProvider | undefined;

    if (specLoader && workspaceRoot) {
        try {
            treeProvider = new SpecTreeProvider(specLoader);
            const treeView = vscode.window.createTreeView('specTree', {
                treeDataProvider: treeProvider,
                showCollapseAll: true,
            });
            context.subscriptions.push(treeView);
        } catch (err) {
            output.appendLine(`[activate] specTree init failed: ${String(err)}`);
            vscode.window.showWarningMessage(`Flow 트리 초기화 실패: ${String(err)}`);
        }
    }

    // 4. 명령어 등록

    // 그래프 열기
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.openGraph', () => {
            const root = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
            if (!specLoader || !root) {
                void requireWorkspace('그래프 보기');
                return;
            }
            output.appendLine('[command] specGraph.openGraph');
            GraphPanel.createOrShow(context.extensionUri, specLoader, root);
        }),
    );

    // 웹 렌더링 프리뷰 (별도 패널)
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.openGraphPreview', () => {
            const root = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
            if (!specLoader || !root) {
                void requireWorkspace('그래프 프리뷰');
                return;
            }
            output.appendLine('[command] specGraph.openGraphPreview');
            GraphPanel.openPreview(context.extensionUri, specLoader, root);
        }),
    );

    // 디버그 정보 출력
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.debugInfo', async () => {
            if (!specLoader) {
                await requireWorkspace('디버그 정보 보기');
                return;
            }
            const graph = await specLoader.getGraph();
            const brokenSpecs = specLoader.getBrokenSpecs();
            const msg = [
                `[debug] specsDirectory=${specLoader.specsDirectory}`,
                `[debug] specs=${graph.specs.length}`,
                `[debug] nodes=${graph.nodes.length}`,
                `[debug] edges=${graph.edges.length}`,
                ...(brokenSpecs.length > 0 ? [
                    `[debug] 손상 스펙 ${brokenSpecs.length}개:`,
                    ...brokenSpecs.map(r =>
                        `  - ${r.specId}: ${r.errorMessage.slice(0, 80)}${r.line != null ? ` (line ${r.line}${r.column != null ? `:${r.column}` : ''})` : ''}`
                    ),
                ] : [`[debug] 손상 스펙 없음`]),
            ].join('\n');
            output.appendLine(msg);
            output.show(true);
            const brokenInfo = brokenSpecs.length > 0
                ? ` | 손상 스펙 ${brokenSpecs.length}개 (Output 패널 참조)`
                : '';
            vscode.window.showInformationMessage(
                `Spec Graph: specs=${graph.specs.length}, nodes=${graph.nodes.length}, edges=${graph.edges.length}${brokenInfo}`
            );
        }),
    );

    // 새로고침
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.refresh', async () => {
            const root = await requireWorkspace('새로고침');
            if (!root || !specLoader) { return; }
            const flowExe = resolveFlowExecutable(root);
            if (flowExe) {
                try {
                    await execCommandAsync(flowExe, ['spec-sync'], root);
                    output.appendLine('[spec-sync] refresh triggered remote spec sync');
                } catch (err) {
                    const msg = String(err);
                    output.appendLine(`[spec-sync] sync failed during refresh: ${msg}`);
                    vscode.window.showWarningMessage(`원격 스펙 동기화 실패: ${msg}`);
                }
            }

            await specLoader.reload();
            const gitRoot = findGitRoot(specLoader.specsDirectory);
            if (gitRoot) { await updatePendingPushContext(gitRoot); }
            const brokenSpecs = specLoader.getBrokenSpecs();
            if (brokenSpecs.length > 0) {
                // F-025-C2: 손상 스펙이 있으면 전체 트리/그래프가 빈 화면이 되지 않도록 경고 노출
                vscode.window.showWarningMessage(
                    `Spec Graph 새로고침 완료. 손상 스펙 ${brokenSpecs.length}개 감지됨: ${brokenSpecs.map(r => r.specId).join(', ')}`,
                    '진단 정보 보기'
                ).then(choice => {
                    if (choice === '진단 정보 보기') {
                        vscode.commands.executeCommand('specGraph.debugInfo');
                    }
                });
            } else {
                vscode.window.showInformationMessage('Spec Graph 새로고침 완료');
            }
        }),
    );

    // 노드 포커스 (트리 클릭 → 그래프 포커스)
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.focusNode', (nodeId: string) => {
            if (!specLoader) {
                void requireWorkspace('노드 포커스');
                return;
            }
            if (GraphPanel.currentPanel) {
                GraphPanel.currentPanel.focusNode(nodeId);
            }
        }),
    );

    // 스펙 파일 열기
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.openSpec', async (item: any) => {
            if (!specLoader) {
                await requireWorkspace('스펙 파일 열기');
                return;
            }
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
            if (!specLoader) {
                await requireWorkspace('스펙 삭제');
                return;
            }
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
            if (!specLoader) {
                await requireWorkspace('상태 필터');
                return;
            }
            const statuses: (SpecStatus | 'all')[] = ['all', 'draft', 'queued', 'working', 'needs-review', 'verified', 'deprecated', 'done'];
            const statusLabels: Record<SpecStatus | 'all', string> = {
                all: '전체',
                draft: '초안',
                queued: '대기중',
                working: '작업중',
                'needs-review': '검토 대기',
                verified: '검증 완료',
                deprecated: '폐기',
                done: '완료',
            };
            const items = statuses.map(s => ({
                label: s === 'all' ? '$(list-flat) 전체' : `$(${getStatusIcon(s as SpecStatus)}) ${statusLabels[s]}`,
                value: s,
            }));

            const picked = await vscode.window.showQuickPick(items, {
                placeHolder: '표시할 상태를 선택하세요',
            });

            if (picked) {
                const value = (picked as any).value;
                if (!treeProvider) {
                    vscode.window.showWarningMessage('Spec Tree가 초기화되지 않아 상태 필터를 적용할 수 없습니다.');
                    return;
                }
                treeProvider.setStatusFilter(value === 'all' ? null : value);
            }
        }),
    );

    // 스펙 문서 뷰 열기 (전체)
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.openSpecView', () => {
            const root = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
            if (!specLoader || !root) {
                void requireWorkspace('스펙 문서 보기');
                return;
            }
            output.appendLine('[command] specGraph.openSpecView');
            SpecViewProvider.createOrShow(context.extensionUri, specLoader, root);
        }),
    );

    // 스펙 문서 뷰 열기 (선택된 노드 포커스)
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.openSpecViewFocused', (item: any, preferBeside?: boolean) => {
            const root = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
            if (!specLoader || !root) {
                void requireWorkspace('선택 스펙 문서 보기');
                return;
            }
            output.appendLine('[command] specGraph.openSpecViewFocused');
            const specId = item?.spec?.id || item;
            const column = preferBeside ? vscode.ViewColumn.Beside : vscode.ViewColumn.One;
            const panel = SpecViewProvider.createOrShow(context.extensionUri, specLoader, root, column);
            if (specId && typeof specId === 'string') {
                panel.focusSpec(specId);
            }
        }),
    );

    // 칸반 보드 열기
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.openKanban', () => {
            const root = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
            if (!specLoader || !root) {
                void requireWorkspace('칸반 보드');
                return;
            }
            output.appendLine('[command] specGraph.openKanban');
            KanbanPanel.createOrShow(context.extensionUri, specLoader, root);
        }),
    );

    // 태그별 필터
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.filterByTag', async () => {
            if (!specLoader) {
                await requireWorkspace('태그 필터');
                return;
            }
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
                if (!treeProvider) {
                    vscode.window.showWarningMessage('Spec Tree가 초기화되지 않아 태그 필터를 적용할 수 없습니다.');
                    return;
                }
                treeProvider.setTagFilter(value === 'all' ? null : value);
            }
        }),
    );

    output.appendLine('[activate] command registration completed');

    // 스펙 push
    context.subscriptions.push(
        vscode.commands.registerCommand('specGraph.pushSpecs', async () => {
            const root = await requireWorkspace('스펙 push');
            if (!root || !specLoader) {
                return;
            }
            output.appendLine('[command] specGraph.pushSpecs');

            const flowExe = resolveFlowExecutable(root);
            if (!flowExe) {
                vscode.window.showErrorMessage('flow CLI를 찾을 수 없습니다. PATH 또는 flow.executablePath 설정을 확인하세요.');
                return;
            }

            const specsGitRoot = findGitRoot(specLoader.specsDirectory);
            const pending = specsGitRoot ? await checkPendingPush(specsGitRoot) : { unpushed: 0, uncommitted: false };
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
                const stdout = await execCommandAsync(flowExe, ['spec-push', '-m', commitMsg, '--pretty'], root);
                output.appendLine(`[push] flow spec-push completed: ${stdout}`);

                await specLoader.reload();
                if (specsGitRoot) {
                    await updatePendingPushContext(specsGitRoot);
                }
                vscode.window.showInformationMessage('Spec Graph: push 완료');

            } catch (err) {
                const msg = String(err);
                output.appendLine(`[push] 실패: ${msg}`);
                vscode.window.showErrorMessage(`Spec Graph push 실패: ${msg}`);
            }
        }),
    );

    // 5. 초기 로드
    if (specLoader) {
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
    }

    // Flow Runner 데몬 시작
    context.subscriptions.push(
        vscode.commands.registerCommand('flowExt.startDaemon', async () => {
            output.appendLine('[command] flowExt.startDaemon');
            const root = await requireWorkspace('Flow Runner 시작');
            if (!root) { return; }
            const flowExe = resolveFlowExecutable(root);
            if (!flowExe) {
                vscode.window.showErrorMessage('flow CLI를 찾을 수 없습니다. PATH 또는 flow.executablePath 설정을 확인하세요.');
                return;
            }

            // 터미널 대신 detached 백그라운드 프로세스로 시작한다.
            // terminal.sendText()로 실행하면 VS Code ConPTY 콘솔 세션에 붙어서
            // checkDaemonRunning() 호출 시 Windows가 CTRL_C_EVENT를 콘솔 그룹 전체에
            // 브로드캐스트하여 daemon이 즉시 종료되는 문제가 발생한다.
            const spawned = spawnDaemonBackground(root, output);
            if (!spawned) {
                vscode.window.showErrorMessage('Flow Runner 데몬 시작 실패: flow 실행 파일을 찾을 수 없습니다.');
                return;
            }

            // 잠시 대기 후 상태 확인
            await new Promise(r => setTimeout(r, 2000));
            const running = await checkDaemonRunning(root);
            await setDaemonRunningContext(running);
            if (running) {
                vscode.window.showInformationMessage('Flow Runner 데몬이 시작되었습니다.');
            } else {
                vscode.window.showWarningMessage('Flow Runner 데몬 시작을 확인할 수 없습니다. 로그를 확인하세요.');
            }
        }),
    );

    // VS Code 창 리로드
    context.subscriptions.push(
        vscode.commands.registerCommand('flowExt.reloadWindow', async (options?: { source?: string }) => {
            output.appendLine('[command] flowExt.reloadWindow');

            const dirtyDocuments = vscode.workspace.textDocuments.filter((doc) => doc.isDirty);
            if (dirtyDocuments.length > 0) {
                const source = options?.source === 'signal' ? '자동 리로드 요청' : '리로드 요청';
                const confirmed = await vscode.window.showWarningMessage(
                    `${source}: 저장되지 않은 편집기 ${dirtyDocuments.length}개가 있습니다. 계속하면 변경 사항이 손실될 수 있습니다.`,
                    { modal: true },
                    '리로드',
                    '취소',
                );

                if (confirmed !== '리로드') {
                    output.appendLine('[command] flowExt.reloadWindow cancelled due to dirty editors');
                    return;
                }
            }

            try {
                await vscode.commands.executeCommand('workbench.action.restartExtensionHost');
            } catch {
                await vscode.commands.executeCommand('workbench.action.reloadWindow');
            }
        }),
    );

    // Flow Runner 데몬 중지
    context.subscriptions.push(
        vscode.commands.registerCommand('flowExt.stopDaemon', async () => {
            output.appendLine('[command] flowExt.stopDaemon');
            const root = await requireWorkspace('Flow Runner 중지');
            if (!root) { return; }
            const flowExe = resolveFlowExecutable(root);
            if (!flowExe) {
                vscode.window.showErrorMessage('flow CLI를 찾을 수 없습니다.');
                return;
            }

            try {
                await execCommandAsync(flowExe, ['runner-stop'], root);
                await setDaemonRunningContext(false);
                vscode.window.showInformationMessage('Flow Runner 데몬이 중지되었습니다.');
            } catch (err) {
                const msg = String(err);
                output.appendLine(`[daemon] 중지 실패: ${msg}`);
                vscode.window.showErrorMessage(`Flow Runner 중지 실패: ${msg}`);
            }
        }),
    );

    // 웹뷰 postMessage 진단 테스트 (CSP 비교)
    registerWebviewTests(context);
}

/**
 * daemon을 터미널 없이 detached 백그라운드 프로세스로 시작한다.
 * ConPTY 터미널에 붙지 않으므로 CTRL_C_EVENT 브로드캐스트 간섭이 없다.
 */
function spawnDaemonBackground(workspaceRoot: string, output: vscode.OutputChannel): boolean {
    const path = require('path') as typeof import('path');
    const fs = require('fs') as typeof import('fs');
    const { spawn } = require('child_process') as typeof import('child_process');

    let cmd: string;
    let args: string[];

    if (process.platform === 'win32') {
        const flowExeBin = path.join(workspaceRoot, '.flow', 'bin', 'flow.exe');
        if (fs.existsSync(flowExeBin)) {
            cmd = flowExeBin;
            args = ['runner-start', '--daemon'];
        } else {
            const flowPs1 = path.join(workspaceRoot, 'flow.ps1');
            if (!fs.existsSync(flowPs1)) { return false; }
            cmd = 'powershell.exe';
            args = ['-ExecutionPolicy', 'Bypass', '-NonInteractive', '-File', flowPs1, 'runner-start', '--daemon'];
        }
    } else {
        const flowExe = resolveFlowExecutable(workspaceRoot);
        if (!flowExe) { return false; }
        cmd = flowExe;
        args = ['runner-start', '--daemon'];
    }

    output.appendLine(`[daemon] 백그라운드 시작: ${cmd} ${args.join(' ')}`);

    const child = spawn(cmd, args, {
        detached: true,
        stdio: 'ignore',
        cwd: workspaceRoot,
        windowsHide: true,
    });
    child.unref();
    return true;
}

function getWorkspaceFlowCandidates(workspaceRoot: string): string[] {
    const path = require('path') as typeof import('path');

    if (process.platform === 'win32') {
        return [path.join(workspaceRoot, '.flow', 'bin', 'flow.exe')];
    }

    if (process.platform === 'darwin') {
        return [
            path.join(workspaceRoot, '.flow', 'bin', 'flow'),
            path.join(workspaceRoot, '.flow', 'bin', 'flow-osx-arm64'),
            path.join(workspaceRoot, '.flow', 'bin', 'flow-osx-x64'),
        ];
    }

    return [
        path.join(workspaceRoot, '.flow', 'bin', 'flow'),
        path.join(workspaceRoot, '.flow', 'bin', 'flow-linux'),
    ];
}

/** flow CLI 실행 경로를 반환한다. 설정값 → PATH 순으로 탐색. */
function resolveFlowExecutable(workspaceRoot: string): string | null {
    const fs = require('fs') as typeof import('fs');

    // 1. 사용자 설정
    const configPath = vscode.workspace.getConfiguration('flow').get<string>('executablePath', '');
    if (configPath && fs.existsSync(configPath)) { return configPath; }

    // 2. 워크스페이스 빌드 산출물 우선 사용
    for (const flowExeBin of getWorkspaceFlowCandidates(workspaceRoot)) {
        if (fs.existsSync(flowExeBin)) { return flowExeBin; }
    }

    const path = require('path') as typeof import('path');

    // 3. 워크스페이스 루트의 flow.ps1 (Windows fallback)
    const flowPs1 = path.join(workspaceRoot, 'flow.ps1');
    if (process.platform === 'win32' && fs.existsSync(flowPs1)) {
        return `powershell -ExecutionPolicy Bypass -File "${flowPs1}"`;
    }

    // 4. PATH에서 flow 탐색
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
        'queued': 'send',
        'working': 'circle-filled',
        'needs-review': 'warning',
        'verified': 'check',
        'deprecated': 'close',
        'done': 'check-all',
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

function notifyLegacyExtensionConflict(): void {
    const legacyExtension = vscode.extensions.getExtension(LEGACY_EXTENSION_ID);
    if (!legacyExtension) {
        return;
    }

    output.appendLine(`[activate] legacy extension detected: ${LEGACY_EXTENSION_ID}`);
    void vscode.window.showWarningMessage(
        '기존 Spec Graph 확장이 함께 설치되어 있어 사이드바 아이콘이 중복될 수 있습니다. legacy 확장을 제거하세요.',
        '확장 창 열기'
    ).then((choice) => {
        if (choice === '확장 창 열기') {
            void vscode.commands.executeCommand('workbench.view.extensions');
        }
    });
}
