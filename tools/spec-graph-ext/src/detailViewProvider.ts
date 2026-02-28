/**
 * DetailViewProvider - 사이드바 하단의 스펙 상세 Webview
 *
 * 트리뷰나 그래프에서 노드를 선택하면 이 패널에 상세 정보 표시
 */
import * as vscode from 'vscode';
import { SpecLoader } from './specLoader';
import { STATUS_COLORS, SpecStatus, GraphNode, GitHubRef, DocLink } from './types';

export class DetailViewProvider implements vscode.WebviewViewProvider {
    public static readonly viewType = 'specDetail';

    private view?: vscode.WebviewView;
    private currentNodeId?: string;

    constructor(
        private extensionUri: vscode.Uri,
        private loader: SpecLoader,
        private workspaceRoot: string,
    ) {
        loader.onDidChange(() => {
            if (this.currentNodeId) {
                this.showNode(this.currentNodeId);
            }
        });
    }

    resolveWebviewView(
        webviewView: vscode.WebviewView,
        _context: vscode.WebviewViewResolveContext,
        _token: vscode.CancellationToken,
    ): void {
        this.view = webviewView;

        webviewView.webview.options = {
            enableScripts: true,
            localResourceRoots: [this.extensionUri],
        };

        webviewView.webview.onDidReceiveMessage(async (msg) => {
            if (msg.type === 'openCodeRef') {
                await this.openCodeRef(msg.codeRef);
            } else if (msg.type === 'openSpec') {
                await this.openSpecFile(msg.specId);
            } else if (msg.type === 'openExternal') {
                await vscode.env.openExternal(vscode.Uri.parse(msg.url));
            } else if (msg.type === 'openDocLink') {
                await this.openDocLink(msg.path);
            }
        });

        // 초기 HTML
        webviewView.webview.html = this.getEmptyHtml();
    }

    /** 노드 상세 표시 */
    async showNode(nodeId: string): Promise<void> {
        this.currentNodeId = nodeId;
        if (!this.view) {
            return;
        }

        const graph = await this.loader.getGraph();
        const node = graph.nodes.find(n => n.id === nodeId);
        if (!node) {
            return;
        }

        const spec = graph.specs.find(s => s.id === nodeId);
        this.view.webview.html = this.getDetailHtml(node, spec);
    }

    private getEmptyHtml(): string {
        return /*html*/ `<!DOCTYPE html>
<html><head><style>
body {
    font-family: var(--vscode-font-family);
    color: var(--vscode-foreground);
    padding: 12px;
    font-size: 13px;
}
.hint { color: var(--vscode-descriptionForeground); text-align: center; margin-top: 40px; }
</style></head><body>
<div class="hint">트리뷰 또는 그래프에서<br>노드를 선택하세요</div>
</body></html>`;
    }

    private getDetailHtml(node: GraphNode, spec?: { conditions: any[]; [key: string]: any }): string {
        const color = STATUS_COLORS[node.status] || '#888';
        const isFeature = node.nodeType === 'feature';

        let conditionsHtml = '';
        if (spec && spec.conditions) {
            conditionsHtml = spec.conditions.map((c: any) => {
                const cColor = STATUS_COLORS[c.status as SpecStatus] || '#888';
                const refsHtml = (c.codeRefs || []).map((r: string) =>
                    `<a class="code-ref" data-ref="${this.escapeAttr(r)}">${this.escapeHtml(r)}</a>`
                ).join('');
                return `<div class="condition">
                    <span class="cond-status" style="color:${cColor}">●</span>
                    <strong>${this.escapeHtml(c.id)}</strong> [${c.status}]<br>
                    <span class="cond-desc">${this.escapeHtml(c.description)}</span>
                    ${refsHtml}
                </div>`;
            }).join('');
        }

        const tagsHtml = node.tags.map(t => `<span class="tag">${this.escapeHtml(t)}</span>`).join('');
        const refsHtml = node.codeRefs.map(r =>
            `<a class="code-ref" data-ref="${this.escapeAttr(r)}">${this.escapeHtml(r)}</a>`
        ).join('');

        const githubRefsHtml = (node.githubRefs || []).map((ref: GitHubRef) => {
            const label = ref.title ? `#${ref.number} ${ref.title}` : `#${ref.number}`;
            const icon = ref.type === 'issue' ? '⚑' : ref.type === 'pr' ? '⟳' : '◉';
            const url = ref.url || null;
            return url
                ? `<span class="github-ref ${ref.type}" data-url="${this.escapeAttr(url)}">${icon} ${this.escapeHtml(label)}</span>`
                : `<span class="github-ref ${ref.type}">${icon} ${this.escapeHtml(label)}</span>`;
        }).join('');

        const docLinksHtml = (node.docLinks || []).map((link: DocLink) => {
            const icon = link.type === 'doc' ? '📄' : link.type === 'reference' ? '📚' : '🔗';
            if (link.path) {
                return `<a class="doc-link ${link.type}" data-path="${this.escapeAttr(link.path)}">${icon} ${this.escapeHtml(link.title)}</a>`;
            } else if (link.url) {
                return `<a class="doc-link ${link.type}" data-url="${this.escapeAttr(link.url)}">${icon} ${this.escapeHtml(link.title)}</a>`;
            }
            return `<span class="doc-link ${link.type}">${icon} ${this.escapeHtml(link.title)}</span>`;
        }).join('');

        const fileId = isFeature ? node.id : node.featureId;

        return /*html*/ `<!DOCTYPE html>
<html><head><style>
body {
    font-family: var(--vscode-font-family);
    color: var(--vscode-foreground);
    padding: 8px;
    font-size: 13px;
    line-height: 1.5;
}
h2 { font-size: 14px; margin: 0 0 6px 0; }
h3 { font-size: 11px; margin: 10px 0 4px 0; color: var(--vscode-descriptionForeground);
     text-transform: uppercase; letter-spacing: 0.5px; }
.badge {
    display: inline-block; padding: 2px 8px; border-radius: 10px;
    font-size: 11px; font-weight: 600; color: #fff; margin-bottom: 6px;
}
.desc { margin-bottom: 8px; }
.tag {
    display: inline-block; padding: 1px 6px; border-radius: 3px; font-size: 11px;
    margin-right: 4px; background: var(--vscode-badge-background); color: var(--vscode-badge-foreground);
}
.code-ref {
    display: block; font-size: 12px; color: var(--vscode-textLink-foreground);
    cursor: pointer; padding: 2px 0;
}
.code-ref:hover { text-decoration: underline; }
.condition {
    padding: 6px 8px; margin: 4px 0; border-radius: 4px;
    background: var(--vscode-editor-background); font-size: 12px;
}
.cond-desc { font-size: 11px; color: var(--vscode-descriptionForeground); }
.btn-open {
    display: inline-block; margin-top: 8px; padding: 3px 10px;
    background: var(--vscode-button-secondaryBackground);
    color: var(--vscode-button-secondaryForeground);
    border: none; border-radius: 3px; cursor: pointer; font-size: 12px;
}
.github-ref {
    display: inline-flex; align-items: center; gap: 4px;
    padding: 2px 8px; margin: 2px 4px 2px 0; border-radius: 10px;
    font-size: 11px; cursor: pointer;
    background: var(--vscode-badge-background); color: var(--vscode-badge-foreground);
    border: 1px solid var(--vscode-widget-border);
}
.github-ref:hover { opacity: 0.8; text-decoration: underline; }
.doc-link {
    display: block; font-size: 12px; padding: 2px 0;
    color: var(--vscode-textLink-foreground); cursor: pointer;
}
.doc-link:hover { text-decoration: underline; }
</style></head><body>
<h2>${this.escapeHtml(node.id)}${isFeature ? ': ' + this.escapeHtml(node.label) : ''}</h2>
<span class="badge" style="background:${color}">${node.status}</span>
<span style="font-size:11px;color:var(--vscode-descriptionForeground)">${node.nodeType}</span>

<div class="desc">${this.escapeHtml(node.description)}</div>

${tagsHtml ? '<h3>Tags</h3>' + tagsHtml : ''}
${refsHtml ? '<h3>Code References</h3>' + refsHtml : ''}
${githubRefsHtml ? '<h3>GitHub</h3>' + githubRefsHtml : ''}
${docLinksHtml ? '<h3>Related Docs</h3>' + docLinksHtml : ''}
${conditionsHtml ? '<h3>Conditions</h3>' + conditionsHtml : ''}

${fileId ? `<button class="btn-open" data-spec="${this.escapeAttr(fileId)}">📄 ${fileId}.json 열기</button>` : ''}

<script>
    const vscode = acquireVsCodeApi();
    document.querySelectorAll('.code-ref').forEach(el => {
        el.addEventListener('click', () => {
            vscode.postMessage({ type: 'openCodeRef', codeRef: el.dataset.ref });
        });
    });
    document.querySelectorAll('.github-ref[data-url]').forEach(el => {
        el.addEventListener('click', () => {
            vscode.postMessage({ type: 'openExternal', url: el.dataset.url });
        });
    });
    document.querySelectorAll('.doc-link[data-path]').forEach(el => {
        el.addEventListener('click', () => {
            vscode.postMessage({ type: 'openDocLink', path: el.dataset.path });
        });
    });
    document.querySelectorAll('.doc-link[data-url]').forEach(el => {
        el.addEventListener('click', () => {
            vscode.postMessage({ type: 'openExternal', url: el.dataset.url });
        });
    });
    document.querySelectorAll('.btn-open').forEach(el => {
        el.addEventListener('click', () => {
            vscode.postMessage({ type: 'openSpec', specId: el.dataset.spec });
        });
    });
</script>
</body></html>`;
    }

    private async openCodeRef(codeRef: string): Promise<void> {
        const path = require('path');
        const parts = codeRef.split('#');
        const filePath = parts[0];
        const lineRange = parts[1];

        const fullPath = vscode.Uri.file(path.join(this.workspaceRoot, filePath));

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
            pathMod.join(this.workspaceRoot, 'docs', 'specs', `${specId}.json`)
        );
        try {
            const doc = await vscode.workspace.openTextDocument(filePath);
            await vscode.window.showTextDocument(doc, { preview: true });
        } catch {
            vscode.window.showWarningMessage(`스펙 파일을 찾을 수 없습니다: ${specId}.json`);
        }
    }

    private escapeHtml(str: string): string {
        if (!str) { return ''; }
        return str.replace(/&/g, '&amp;')
                  .replace(/</g, '&lt;')
                  .replace(/>/g, '&gt;')
                  .replace(/"/g, '&quot;');
    }

    private escapeAttr(str: string): string {
        return this.escapeHtml(str).replace(/'/g, '&#039;');
    }
}
