/**
 * webviewTest - CSP/postMessage 진단용 테스트 웹뷰 3종
 *
 * Test A: CSP 없음 (원본 상태)
 * Test B: CSP with nonce + cspSource (graphPanel 방식)
 * Test C: CSP with nonce only (cspSource 없음)
 */
import * as vscode from 'vscode';

function getNonce(): string {
    let text = '';
    const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    for (let i = 0; i < 32; i++) {
        text += possible.charAt(Math.floor(Math.random() * possible.length));
    }
    return text;
}

function makeHtml(title: string, cspMeta: string, scriptAttr: string): string {
    return /* html */`<!DOCTYPE html>
<html lang="ko">
<head>
<meta charset="UTF-8">
${cspMeta}
<title>${title}</title>
<style>
body { font-family: var(--vscode-font-family); background: var(--vscode-editor-background); color: var(--vscode-editor-foreground); padding: 24px; }
h2 { margin-bottom: 12px; }
pre { background: #1e1e1e; padding: 8px; border-radius: 4px; font-size: 11px; margin-bottom: 16px; }
button { padding: 8px 20px; font-size: 13px; cursor: pointer; background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; border-radius: 4px; }
button:hover { background: var(--vscode-button-hoverBackground); }
#log { margin-top: 16px; font-size: 12px; color: #4ec9b0; }
</style>
</head>
<body>
<h2>${title}</h2>
<pre>${cspMeta || '(CSP 없음)'}</pre>
<button id="btn">버튼 클릭 → 토스트</button>
<div id="log">대기 중...</div>
<script${scriptAttr}>
const vscode = acquireVsCodeApi();
document.getElementById('btn').addEventListener('click', () => {
    document.getElementById('log').textContent = '클릭됨 — postMessage 전송 중...';
    vscode.postMessage({ type: 'toast', text: '${title} 버튼 클릭 성공!' });
});
window.addEventListener('message', (e) => {
    if (e.data.type === 'ack') {
        document.getElementById('log').textContent = '✅ 메시지 수신 확인: ' + e.data.text;
    }
});
</script>
</body>
</html>`;
}

function openTest(
    context: vscode.ExtensionContext,
    title: string,
    cspMeta: string,
    scriptAttr: string,
): void {
    const panel = vscode.window.createWebviewPanel(
        `webviewTest.${title}`,
        title,
        vscode.ViewColumn.Beside,
        { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [context.extensionUri] },
    );
    panel.webview.html = makeHtml(title, cspMeta, scriptAttr);

    panel.webview.onDidReceiveMessage((msg) => {
        if (msg.type === 'toast') {
            vscode.window.showInformationMessage(`[${title}] ${msg.text}`);
            panel.webview.postMessage({ type: 'ack', text: msg.text });
        }
    });
}

export function registerWebviewTests(context: vscode.ExtensionContext): void {
    // Test A: CSP 없음
    context.subscriptions.push(
        vscode.commands.registerCommand('flowExt.testA_noCSP', () => {
            openTest(context, 'TestA: CSP없음', '', '');
        }),
    );

    // Test B: nonce + cspSource (graphPanel 방식)
    context.subscriptions.push(
        vscode.commands.registerCommand('flowExt.testB_cspSource', () => {
            const nonce = getNonce();
            const panel = vscode.window.createWebviewPanel(
                'webviewTest.B',
                'TestB: nonce+cspSource',
                vscode.ViewColumn.Beside,
                { enableScripts: true, retainContextWhenHidden: true },
            );
            const csp = `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}' ${panel.webview.cspSource}; img-src ${panel.webview.cspSource} data:;">`;
            panel.webview.html = makeHtml('TestB: nonce+cspSource', csp, ` nonce="${nonce}"`);
            panel.webview.onDidReceiveMessage((msg) => {
                if (msg.type === 'toast') {
                    vscode.window.showInformationMessage(`[TestB] ${msg.text}`);
                    panel.webview.postMessage({ type: 'ack', text: msg.text });
                }
            });
        }),
    );

    // Test C: nonce only (cspSource 없음)
    context.subscriptions.push(
        vscode.commands.registerCommand('flowExt.testC_nonceOnly', () => {
            const nonce = getNonce();
            const panel = vscode.window.createWebviewPanel(
                'webviewTest.C',
                'TestC: nonce only',
                vscode.ViewColumn.Beside,
                { enableScripts: true, retainContextWhenHidden: true },
            );
            const csp = `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">`;
            panel.webview.html = makeHtml('TestC: nonce only', csp, ` nonce="${nonce}"`);
            panel.webview.onDidReceiveMessage((msg) => {
                if (msg.type === 'toast') {
                    vscode.window.showInformationMessage(`[TestC] ${msg.text}`);
                    panel.webview.postMessage({ type: 'ack', text: msg.text });
                }
            });
        }),
    );
}
