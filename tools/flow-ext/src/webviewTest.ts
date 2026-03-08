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

// ─── 칸반 이관 단계 테스트 ────────────────────────────────────────────────────

/**
 * Step1: TestA 방식 + ViewColumn.One (칸반과 같은 컬럼)
 *   차이: 매번 새 패널, html 한 번만 설정
 */
function testKanbanStep1(context: vscode.ExtensionContext): void {
    const panel = vscode.window.createWebviewPanel(
        'testKanban.step1',
        'KanbanTest-Step1',
        vscode.ViewColumn.One,
        { enableScripts: true },
    );
    panel.webview.html = `<!DOCTYPE html><html><body
      style="background:var(--vscode-editor-background);color:var(--vscode-editor-foreground);padding:20px;">
<h3>Step1: ViewColumn.One, html 한 번 설정</h3>
<button id="btn">postMessage 테스트</button>
<div id="log">대기중...</div>
<script>
const vscode = acquireVsCodeApi();
document.getElementById('btn').addEventListener('click', () => {
  document.getElementById('log').textContent = '클릭됨';
  vscode.postMessage({ type: 'step1', text: 'step1 클릭!' });
});
</script></body></html>`;
    panel.webview.onDidReceiveMessage((msg) => {
        if (msg.type === 'step1') {
            vscode.window.showInformationMessage(`[Step1 성공] ${msg.text}`);
        }
    });
}

/**
 * Step2: Step1 + retainContextWhenHidden + mousedown 이벤트
 */
function testKanbanStep2(context: vscode.ExtensionContext): void {
    const panel = vscode.window.createWebviewPanel(
        'testKanban.step2',
        'KanbanTest-Step2',
        vscode.ViewColumn.One,
        { enableScripts: true, retainContextWhenHidden: true },
    );
    panel.webview.html = `<!DOCTYPE html><html><body
      style="background:var(--vscode-editor-background);color:var(--vscode-editor-foreground);padding:20px;">
<h3>Step2: retainContextWhenHidden + mousedown</h3>
<div id="card" style="padding:12px;border:1px solid #555;cursor:pointer;margin:8px 0;">카드 (mousedown 클릭)</div>
<button id="btn">버튼 (click 클릭)</button>
<div id="log">대기중...</div>
<script>
const vscode = acquireVsCodeApi();
document.getElementById('card').addEventListener('mousedown', () => {
  document.getElementById('log').textContent = 'mousedown 발생';
  vscode.postMessage({ type: 'step2', text: 'step2 mousedown!' });
});
document.getElementById('btn').addEventListener('click', () => {
  document.getElementById('log').textContent = 'click 발생';
  vscode.postMessage({ type: 'step2', text: 'step2 click!' });
});
</script></body></html>`;
    panel.webview.onDidReceiveMessage((msg) => {
        if (msg.type === 'step2') {
            vscode.window.showInformationMessage(`[Step2 성공] ${msg.text}`);
        }
    });
}

/**
 * Step3: Step2 + 싱글톤 패턴 (칸반과 동일)
 */
let _step3Panel: vscode.WebviewPanel | undefined;
function testKanbanStep3(context: vscode.ExtensionContext): void {
    if (_step3Panel) {
        _step3Panel.reveal(vscode.ViewColumn.One);
        return;
    }
    const panel = vscode.window.createWebviewPanel(
        'testKanban.step3',
        'KanbanTest-Step3',
        vscode.ViewColumn.One,
        { enableScripts: true, retainContextWhenHidden: true },
    );
    _step3Panel = panel;
    panel.onDidDispose(() => { _step3Panel = undefined; });
    panel.webview.html = `<!DOCTYPE html><html><body
      style="background:var(--vscode-editor-background);color:var(--vscode-editor-foreground);padding:20px;">
<h3>Step3: 싱글톤 패턴</h3>
<div id="card" style="padding:12px;border:1px solid #555;cursor:pointer;margin:8px 0;">카드 (mousedown)</div>
<div id="log">대기중...</div>
<script>
const vscode = acquireVsCodeApi();
document.getElementById('card').addEventListener('mousedown', () => {
  document.getElementById('log').textContent = 'mousedown 발생';
  vscode.postMessage({ type: 'step3', text: 'step3 mousedown!' });
});
</script></body></html>`;
    panel.webview.onDidReceiveMessage((msg) => {
        if (msg.type === 'step3') {
            vscode.window.showInformationMessage(`[Step3 성공] ${msg.text}`);
        }
    });
}

/**
 * Step4: Step3 + html을 비동기로 여러 번 교체 (칸반 update() 패턴)
 */
let _step4Panel: vscode.WebviewPanel | undefined;
function makeStep4Html(count: number): string {
    return `<!DOCTYPE html><html><body
      style="background:var(--vscode-editor-background);color:var(--vscode-editor-foreground);padding:20px;">
<h3>Step4: html 다중 교체 (렌더 #${count})</h3>
<div id="card" style="padding:12px;border:1px solid #555;cursor:pointer;margin:8px 0;">카드 mousedown (렌더 #${count})</div>
<div id="log">대기중...</div>
<script>
const vscode = acquireVsCodeApi();
document.getElementById('card').addEventListener('mousedown', () => {
  document.getElementById('log').textContent = 'mousedown #${count}';
  vscode.postMessage({ type: 'step4', text: 'step4 mousedown render#${count}' });
});
</script></body></html>`;
}
function testKanbanStep4(context: vscode.ExtensionContext): void {
    if (_step4Panel) {
        _step4Panel.reveal(vscode.ViewColumn.One);
        return;
    }
    const panel = vscode.window.createWebviewPanel(
        'testKanban.step4',
        'KanbanTest-Step4',
        vscode.ViewColumn.One,
        { enableScripts: true, retainContextWhenHidden: true },
    );
    _step4Panel = panel;
    panel.onDidDispose(() => { _step4Panel = undefined; });

    let renderCount = 0;
    const setHtml = () => {
        renderCount++;
        panel.webview.html = makeStep4Html(renderCount);
    };
    setHtml();

    // 3초마다 html 교체 — 칸반의 onDidChange + update() 패턴 시뮬레이션
    const timer = setInterval(setHtml, 3000);
    panel.onDidDispose(() => clearInterval(timer));

    panel.webview.onDidReceiveMessage((msg) => {
        if (msg.type === 'step4') {
            vscode.window.showInformationMessage(`[Step4 성공] ${msg.text}`);
        }
    });
}

export function registerWebviewTests(context: vscode.ExtensionContext): void {
    context.subscriptions.push(
        vscode.commands.registerCommand('flowExt.testKanban_step1', () => testKanbanStep1(context)),
        vscode.commands.registerCommand('flowExt.testKanban_step2', () => testKanbanStep2(context)),
        vscode.commands.registerCommand('flowExt.testKanban_step3', () => testKanbanStep3(context)),
        vscode.commands.registerCommand('flowExt.testKanban_step4', () => testKanbanStep4(context)),
    );
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
