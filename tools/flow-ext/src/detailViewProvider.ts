/**
 * DetailViewProvider - 사이드바 하단의 스펙 상세 Webview
 *
 * 트리뷰나 그래프에서 노드를 선택하면 이 패널에 상세 정보 표시
 */
import * as vscode from 'vscode';
import { SpecLoader } from './specLoader';
import { STATUS_COLORS, SpecStatus, GraphNode, GitHubRef, DocLink, Condition, Spec } from './types';
import { describeReviewDisposition, getConditionManualVerificationItems, getNodeManualVerificationItems, getSpecReviewState, getUserFeedbackState } from './reviewState';
import { saveQuestionAnswer } from './feedbackStore';

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
            } else if (msg.type === 'answerQuestion') {
                await this.answerQuestion(msg.specId, msg.questionId, msg.questionText, msg.answer);
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
        const conditionRef = this.loader.findCondition(nodeId);
        this.view.webview.html = this.getDetailHtml(node, spec ?? conditionRef?.spec, conditionRef?.condition);
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

    private getDetailHtml(node: GraphNode, spec?: Spec, condition?: Condition): string {
        const color = STATUS_COLORS[node.status] || '#888';
        const isFeature = node.nodeType === 'feature' || node.nodeType === 'task';
        const review = isFeature && spec ? getSpecReviewState(spec) : null;
        const feedback = isFeature && spec ? getUserFeedbackState(spec) : null;
        const nodeManualItems = condition
            ? getConditionManualVerificationItems(condition)
            : getNodeManualVerificationItems(node);

        let reviewHtml = '';
        if (review) {
            const statusText = review.statusSummary;
            reviewHtml = `<div class="review-panel ${review.blockedByOpenQuestions ? 'blocked' : review.requiresManualVerification ? 'manual' : review.autoVerifyEligible ? 'eligible' : ''}">
                <div class="review-title">검증 상태</div>
                <div class="review-badge-row">
                    <span class="review-pill">${statusText}</span>
                    ${review.totalConditions > 0 ? `<span class="review-progress">${review.progressPercent}%</span>` : ''}
                </div>
                ${review.totalConditions > 0 ? `<div class="progress-track"><div class="progress-fill" style="width:${review.progressPercent}%;background:${review.progressPercent === 100 ? '#4caf50' : '#2196f3'}"></div></div>` : ''}
                ${feedback?.reviewDisposition ? `<div class="review-disposition">사유: ${this.escapeHtml(describeReviewDisposition(feedback.reviewDisposition) ?? feedback.reviewDisposition)}</div>` : ''}
                ${review.requiresManualVerification ? `<ul class="review-items">${review.manualVerificationItems.map((item) => `<li><strong>${this.escapeHtml(item.label)}</strong>${item.reason ? ` - ${this.escapeHtml(item.reason)}` : ''}</li>`).join('')}</ul>` : ''}
            </div>`;
        } else if (nodeManualItems.length > 0) {
            reviewHtml = `<div class="review-panel manual">
                <div class="review-title">수동 검증 필요</div>
                <ul class="review-items">${nodeManualItems.map((item) => `<li><strong>${this.escapeHtml(item.label)}</strong>${item.reason ? ` - ${this.escapeHtml(item.reason)}` : ''}</li>`).join('')}</ul>
            </div>`;
        }

        // C3: 사용자 피드백 필요 패널 - open 질문 목록, 컨텍스트 수집 방법, 최근 답변 시각, 답변 입력
        let feedbackHtml = '';
        if (feedback && feedback.openQuestionCount > 0) {
            const lastAnsweredRow = feedback.lastAnsweredAt
                ? `<div class="feedback-last-answered">🕐 마지막 답변: ${this.escapeHtml(feedback.lastAnsweredAt)}</div>`
                : '';
            const dispositionRow = feedback.reviewDisposition
                ? `<div class="feedback-review-reason">현재 상태: ${this.escapeHtml(describeReviewDisposition(feedback.reviewDisposition) ?? feedback.reviewDisposition)}</div>`
                : '';

            const questionsHtml = feedback.openQuestions.map((q, idx) => {
                const contextMethodsHtml = q.suggestedContextMethods && q.suggestedContextMethods.length > 0
                    ? `<div class="feedback-context-methods"><span class="feedback-context-label">컨텍스트 수집 방법:</span> ${q.suggestedContextMethods.map(m => `<span class="feedback-context-item">${this.escapeHtml(m)}</span>`).join('')}</div>`
                    : '';
                const whyHtml = q.why
                    ? `<div class="feedback-why">${this.escapeHtml(q.why)}</div>`
                    : '';
                const typeHtml = q.type
                    ? `<span class="feedback-type">${this.escapeHtml(q.type)}</span>`
                    : '';
                const suggestionsHtml = q.answerSuggestions && q.answerSuggestions.length > 0
                    ? `<div class="feedback-suggestions">${q.answerSuggestions.map((suggestion) => `<button class="feedback-suggestion-btn" data-answer="${this.escapeAttr(suggestion)}">${this.escapeHtml(suggestion)}</button>`).join('')}</div>`
                    : '';
                const saveLabel = q.type === 'user-decision' ? '결정 저장' : '답변 저장';
                return `<div class="feedback-question">
                    <div class="feedback-q-head">${typeHtml}<div class="feedback-q-text">❓ ${this.escapeHtml(q.question)}</div></div>
                    ${whyHtml}
                    ${contextMethodsHtml}
                    ${suggestionsHtml}
                    <div class="feedback-answer-area">
                        <textarea class="feedback-answer-input" data-q-id="${this.escapeAttr(q.id || String(idx))}" data-question="${this.escapeAttr(q.question)}" placeholder="여기서 바로 답변을 입력하거나 제안 답변을 선택하세요.">${this.escapeHtml(q.answer ?? '')}</textarea>
                        <div class="feedback-actions"><button class="feedback-save-btn" data-q-id="${this.escapeAttr(q.id || String(idx))}" data-question="${this.escapeAttr(q.question)}">${saveLabel}</button></div>
                    </div>
                </div>`;
            }).join('');

            feedbackHtml = `<div class="feedback-panel">
                <div class="feedback-title">❓ 사용자 판단 필요</div>
                ${dispositionRow}
                ${lastAnsweredRow}
                ${questionsHtml || '<div class="feedback-no-questions">대기 중인 미해결 질문이 없습니다.</div>'}
            </div>`;
        }

        if (feedback && feedback.additionalInformationRequests.length > 0) {
            feedbackHtml += `<div class="feedback-panel readonly">
                <div class="feedback-title">ℹ 추가 정보 요청</div>
                <ul class="feedback-readonly-list">${feedback.additionalInformationRequests.map((item) => `<li>${this.escapeHtml(item)}</li>`).join('')}</ul>
                <div class="feedback-readonly-note">이 항목은 리뷰 참고 요청이며, 직접 답변을 입력하는 질문으로 집계되지 않습니다.</div>
            </div>`;
        }

        let conditionsHtml = '';

        // C3: 자동 승격 이력 패널 - promotion.source, reason, confidence, promotedAt, plannerState
        let promotionHtml = '';
        if (spec?.metadata) {
            const meta = spec.metadata;
            const promotion = meta['promotion'] as Record<string, unknown> | undefined;
            const plannerState = typeof meta['plannerState'] === 'string' ? meta['plannerState'] : null;

            if (promotion && typeof promotion === 'object') {
                const src = typeof promotion['source'] === 'string' ? promotion['source'] : '';
                const reason = typeof promotion['reason'] === 'string' ? promotion['reason'] : '';
                const promotedAt = typeof promotion['promotedAt'] === 'string' ? promotion['promotedAt'] : '';
                const confidence = typeof promotion['confidence'] === 'number' ? promotion['confidence'] : null;
                const revertedAt = typeof promotion['revertedAt'] === 'string' ? promotion['revertedAt'] : null;
                const revertReason = typeof promotion['revertReason'] === 'string' ? promotion['revertReason'] : null;

                const isReverted = !!revertedAt;
                const panelClass = isReverted ? 'promotion-panel reverted' : 'promotion-panel';
                const icon = isReverted ? '↩' : '⬆';
                const titleText = isReverted ? '자동 승격 (복원됨)' : '자동 승격 이력';

                const confidenceBar = confidence !== null
                    ? `<div class="promo-confidence-row">
                        <span class="promo-label">신뢰도</span>
                        <div class="promo-conf-track"><div class="promo-conf-fill" style="width:${Math.round(confidence * 100)}%"></div></div>
                        <span class="promo-conf-val">${Math.round(confidence * 100)}%</span>
                       </div>`
                    : '';
                const revertRow = isReverted
                    ? `<div class="promo-revert-row">↩ ${this.escapeHtml(revertedAt!)}${revertReason ? ` — ${this.escapeHtml(revertReason)}` : ''}</div>`
                    : '';

                promotionHtml = `<div class="${panelClass}">
                    <div class="promo-title">${icon} ${titleText}</div>
                    ${src ? `<div class="promo-row"><span class="promo-label">출처</span><span class="promo-val">${this.escapeHtml(src)}</span></div>` : ''}
                    ${promotedAt ? `<div class="promo-row"><span class="promo-label">승격 시각</span><span class="promo-val">${this.escapeHtml(promotedAt)}</span></div>` : ''}
                    ${reason ? `<div class="promo-row"><span class="promo-label">근거</span><span class="promo-val">${this.escapeHtml(reason)}</span></div>` : ''}
                    ${confidenceBar}
                    ${plannerState && !isReverted ? `<div class="promo-row"><span class="promo-label">plannerState</span><span class="promo-val">${this.escapeHtml(plannerState)}</span></div>` : ''}
                    ${revertRow}
                </div>`;
            } else if (plannerState === 'waiting-user-input') {
                promotionHtml = `<div class="promotion-panel waiting">
                    <div class="promo-title">⏳ 자동 승격 대기</div>
                    <div class="promo-row"><span class="promo-label">plannerState</span><span class="promo-val">${this.escapeHtml(plannerState)}</span></div>
                </div>`;
            }
        }

        if (spec && spec.conditions) {
            conditionsHtml = spec.conditions.map((c) => {
                const cColor = STATUS_COLORS[c.status as SpecStatus] || '#888';
                const refsHtml = (c.codeRefs || []).map((r: string) =>
                    `<a class="code-ref" data-ref="${this.escapeAttr(r)}">${this.escapeHtml(r)}</a>`
                ).join('');
                const manualItems = getConditionManualVerificationItems(c);
                return `<div class="condition">
                    <span class="cond-status" style="color:${cColor}">●</span>
                    <strong>${this.escapeHtml(c.id)}</strong> [${c.status}]<br>
                    <span class="cond-desc">${this.escapeHtml(c.description)}</span>
                    ${manualItems.length > 0 ? `<div class="cond-review">⚠ 수동 검증${manualItems[0]?.reason ? ` - ${this.escapeHtml(manualItems[0].reason)}` : ''}</div>` : ''}
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

        // F-021-C5: 관계 요약 HTML
        let relationHtml = '';
        if (spec && isFeature) {
            const hasRelations = (spec.supersedes && spec.supersedes.length > 0)
                || (spec.supersededBy && spec.supersededBy.length > 0)
                || (spec.mutates && spec.mutates.length > 0)
                || (spec.mutatedBy && spec.mutatedBy.length > 0);
            if (hasRelations) {
                const rows: string[] = [];
                if (spec.supersedes && spec.supersedes.length > 0) {
                    rows.push(`<div class="rel-row"><span class="rel-label supersedes-label">↠ supersedes</span>${
                        spec.supersedes.map(id => `<a class="spec-link" data-spec="${this.escapeAttr(id)}">${this.escapeHtml(id)}</a>`).join(', ')
                    }</div>`);
                }
                if (spec.supersededBy && spec.supersededBy.length > 0) {
                    rows.push(`<div class="rel-row"><span class="rel-label supersededby-label">⇝ supersededBy</span>${
                        spec.supersededBy.map(id => `<a class="spec-link" data-spec="${this.escapeAttr(id)}">${this.escapeHtml(id)}</a>`).join(', ')
                    }${spec.status !== 'deprecated' ? ' <span class="rel-warn">⚠ 대체됨</span>' : ''}</div>`);
                }
                if (spec.mutates && spec.mutates.length > 0) {
                    rows.push(`<div class="rel-row"><span class="rel-label mutates-label">⟳ mutates</span>${
                        spec.mutates.map(id => `<a class="spec-link" data-spec="${this.escapeAttr(id)}">${this.escapeHtml(id)}</a>`).join(', ')
                    }</div>`);
                }
                if (spec.mutatedBy && spec.mutatedBy.length > 0) {
                    rows.push(`<div class="rel-row"><span class="rel-label mutatedby-label">⟲ mutatedBy</span>${
                        spec.mutatedBy.map(id => `<a class="spec-link" data-spec="${this.escapeAttr(id)}">${this.escapeHtml(id)}</a>`).join(', ')
                    }</div>`);
                }
                const recommended = spec.supersededBy && spec.supersededBy.length > 0 && spec.status !== 'deprecated'
                    ? `<div class="rel-recommend">권장: 대체 스펙(${spec.supersededBy.join(', ')}) 검토 후 deprecated 전환 고려</div>`
                    : '';
                relationHtml = `<h3>Relationships</h3><div class="rel-summary">${rows.join('')}${recommended}</div>`;
            }
        }

        // F-021-C1: 변경 이력 HTML
        let changeLogHtml = '';
        if (spec && spec.changeLog && spec.changeLog.length > 0) {
            const entries = spec.changeLog.slice(-3);
            const entryHtml = entries.map(entry => {
                const at = entry.at ? entry.at.substring(0, 10) : '';
                return `<div class="cl-entry">
                    <span class="cl-type cl-type-${this.escapeAttr(entry.type)}">${this.escapeHtml(entry.type)}</span>
                    <span class="cl-meta">${this.escapeHtml(at)} · ${this.escapeHtml(entry.author)}</span><br>
                    <span class="cl-summary">${this.escapeHtml(entry.summary)}</span>
                </div>`;
            }).join('');
            const moreLabel = spec.changeLog.length > 3
                ? `<div class="cl-more">… 및 ${spec.changeLog.length - 3}개 이전 항목</div>`
                : '';
            changeLogHtml = `<h3>Change Log (${spec.changeLog.length})</h3><div class="cl-list">${entryHtml}${moreLabel}</div>`;
        }

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
.cond-review {
    margin-top: 4px;
    font-size: 10px;
    color: #ffb74d;
}
.review-panel {
    margin-bottom: 10px;
    padding: 8px 10px;
    border-radius: 6px;
    border: 1px solid var(--vscode-widget-border);
    background: color-mix(in srgb, var(--vscode-editor-background) 82%, #1f3b4d 18%);
}
.review-panel.manual {
    border-color: rgba(255, 152, 0, 0.45);
    background: rgba(255, 152, 0, 0.09);
}
.review-panel.eligible {
    border-color: rgba(76, 175, 80, 0.4);
    background: rgba(76, 175, 80, 0.08);
}
.review-title {
    font-size: 11px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.4px;
    color: var(--vscode-descriptionForeground);
    margin-bottom: 6px;
}
.review-badge-row {
    display: flex;
    justify-content: space-between;
    gap: 8px;
    align-items: center;
    margin-bottom: 6px;
}
.review-pill {
    display: inline-flex;
    align-items: center;
    padding: 2px 8px;
    border-radius: 999px;
    font-size: 11px;
    font-weight: 600;
    background: var(--vscode-badge-background);
    color: var(--vscode-badge-foreground);
}
.review-progress {
    font-size: 11px;
    color: var(--vscode-descriptionForeground);
}
.progress-track {
    width: 100%;
    height: 6px;
    border-radius: 999px;
    overflow: hidden;
    background: var(--vscode-widget-border);
}
.progress-fill {
    height: 100%;
}
.review-items {
    margin: 8px 0 0 16px;
    font-size: 11px;
}
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
.feedback-panel {
    margin-bottom: 10px;
    padding: 8px 10px;
    border-radius: 6px;
    border: 1px solid rgba(233, 30, 99, 0.5);
    background: rgba(233, 30, 99, 0.08);
}
.feedback-panel.readonly {
    border-color: var(--vscode-panel-border, #3c3c3c);
    background: rgba(255, 255, 255, 0.03);
}
.feedback-title {
    font-size: 11px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.4px;
    color: #f48fb1;
    margin-bottom: 6px;
}
.feedback-last-answered {
    font-size: 10px;
    color: var(--vscode-descriptionForeground);
    margin-bottom: 6px;
}
.feedback-question {
    padding: 6px 0;
    border-bottom: 1px solid rgba(233, 30, 99, 0.2);
    margin-bottom: 6px;
}
.feedback-question:last-child { border-bottom: none; margin-bottom: 0; }
.feedback-q-head {
    display: flex;
    align-items: center;
    gap: 6px;
    margin-bottom: 4px;
}
.feedback-type {
    display: inline-flex;
    padding: 1px 6px;
    border-radius: 999px;
    font-size: 10px;
    background: rgba(233, 30, 99, 0.18);
    color: #f8bbd0;
}
.feedback-q-text {
    font-size: 12px;
    font-weight: 500;
    color: var(--vscode-foreground);
}
.feedback-why {
    font-size: 11px;
    color: var(--vscode-descriptionForeground);
    margin-bottom: 4px;
}
.feedback-context-methods {
    font-size: 10px;
    color: var(--vscode-descriptionForeground);
    margin-bottom: 4px;
}
.feedback-context-label { font-weight: 600; }
.feedback-context-item {
    display: inline-block;
    padding: 1px 5px;
    border-radius: 3px;
    background: rgba(233, 30, 99, 0.1);
    color: #f48fb1;
    margin: 1px 2px;
    font-size: 10px;
}
.feedback-answer-area { margin-top: 4px; }
.feedback-suggestions {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    margin: 6px 0;
}
.feedback-suggestion-btn,
.feedback-save-btn {
    border: 1px solid rgba(233, 30, 99, 0.35);
    border-radius: 999px;
    background: rgba(233, 30, 99, 0.12);
    color: var(--vscode-foreground);
    padding: 3px 10px;
    font-size: 11px;
    cursor: pointer;
}
.feedback-save-btn {
    border-radius: 4px;
    background: var(--vscode-button-background);
    color: var(--vscode-button-foreground);
    border-color: var(--vscode-button-background);
}
.feedback-answer-input {
    width: 100%;
    min-height: 68px;
    background: var(--vscode-input-background);
    color: var(--vscode-input-foreground);
    border: 1px solid rgba(233, 30, 99, 0.3);
    border-radius: 3px;
    font-size: 11px;
    padding: 6px;
    resize: vertical;
    font-family: var(--vscode-font-family);
}
.feedback-actions {
    display: flex;
    justify-content: flex-end;
    margin-top: 6px;
}
.feedback-no-questions {
    font-size: 11px;
    color: var(--vscode-descriptionForeground);
}
.feedback-readonly-list {
    list-style: disc;
    margin: 8px 0 0 18px;
    padding: 0;
}
.feedback-readonly-list li {
    margin-bottom: 6px;
    font-size: 12px;
    line-height: 1.6;
}
.feedback-readonly-note {
    margin-top: 8px;
    font-size: 11px;
    color: var(--vscode-descriptionForeground);
}
.promotion-panel {
    margin-bottom: 10px;
    padding: 8px 10px;
    border-radius: 6px;
    border: 1px solid rgba(156, 39, 176, 0.4);
    background: rgba(156, 39, 176, 0.07);
}
.promotion-panel.reverted {
    border-color: rgba(120, 120, 120, 0.4);
    background: rgba(120, 120, 120, 0.07);
}
.promotion-panel.waiting {
    border-color: rgba(255, 193, 7, 0.4);
    background: rgba(255, 193, 7, 0.07);
}
.promo-title {
    font-size: 11px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.4px;
    color: #ce93d8;
    margin-bottom: 6px;
}
.promotion-panel.reverted .promo-title { color: var(--vscode-descriptionForeground); }
.promotion-panel.waiting .promo-title { color: #ffd54f; }
.promo-row {
    display: flex;
    gap: 6px;
    font-size: 11px;
    margin-bottom: 3px;
    align-items: baseline;
}
.promo-label {
    font-weight: 600;
    color: var(--vscode-descriptionForeground);
    min-width: 60px;
    flex-shrink: 0;
}
.promo-val {
    color: var(--vscode-foreground);
    word-break: break-all;
}
.promo-confidence-row {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 11px;
    margin-bottom: 3px;
}
.promo-conf-track {
    flex: 1;
    height: 5px;
    border-radius: 999px;
    background: var(--vscode-widget-border);
    overflow: hidden;
}
.promo-conf-fill {
    height: 100%;
    background: #ab47bc;
    border-radius: 999px;
}
.promo-conf-val {
    font-size: 10px;
    color: var(--vscode-descriptionForeground);
    min-width: 28px;
    text-align: right;
}
.promo-revert-row {
    font-size: 10px;
    color: var(--vscode-descriptionForeground);
    margin-top: 4px;
    word-break: break-all;
}
/* F-021-C5: 관계 요약 스타일 */
.rel-summary {
    background: var(--vscode-editor-background);
    border-radius: 4px;
    padding: 6px 8px;
    margin-bottom: 6px;
}
.rel-row { font-size: 11px; margin: 3px 0; }
.rel-label { display: inline-block; min-width: 90px; font-weight: 600; }
.supersedes-label { color: #f44336; }
.supersededby-label { color: #ff7043; }
.mutates-label { color: #ff9800; }
.mutatedby-label { color: #ffc107; }
.rel-warn { color: #f44336; font-size: 10px; }
.rel-recommend { color: #ff9800; font-size: 10px; margin-top: 4px; }
.spec-link { color: var(--vscode-textLink-foreground); cursor: pointer; text-decoration: underline; }
/* F-021-C1: 변경 이력 스타일 */
.cl-list { font-size: 11px; }
.cl-entry { padding: 3px 0; border-bottom: 1px solid var(--vscode-widget-border, #333); }
.cl-type { display: inline-block; padding: 1px 4px; border-radius: 3px; font-size: 10px; font-weight: 600; }
.cl-type-create { background: #1b5e20; color: #a5d6a7; }
.cl-type-mutate { background: #e65100; color: #ffccbc; }
.cl-type-supersede { background: #b71c1c; color: #ffcdd2; }
.cl-type-deprecate { background: #4e342e; color: #d7ccc8; }
.cl-type-restore { background: #1a237e; color: #c5cae9; }
.cl-meta { color: var(--vscode-descriptionForeground); font-size: 10px; margin-left: 4px; }
.cl-summary { color: var(--vscode-foreground); }
.cl-more { color: var(--vscode-descriptionForeground); font-size: 10px; }
</style></head><body>
<h2>${this.escapeHtml(node.id)}${isFeature ? ': ' + this.escapeHtml(node.label) : ''}</h2>
<span class="badge" style="background:${color}">${node.status}</span>
<span style="font-size:11px;color:var(--vscode-descriptionForeground)">${node.nodeType}</span>

<div class="desc">${this.escapeHtml(node.description)}</div>

${feedbackHtml}
${promotionHtml}
${reviewHtml}

${tagsHtml ? '<h3>Tags</h3>' + tagsHtml : ''}
${refsHtml ? '<h3>Code References</h3>' + refsHtml : ''}
${githubRefsHtml ? '<h3>GitHub</h3>' + githubRefsHtml : ''}
${docLinksHtml ? '<h3>Related Docs</h3>' + docLinksHtml : ''}
${conditionsHtml ? '<h3>Conditions</h3>' + conditionsHtml : ''}
${relationHtml}
${changeLogHtml}

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
    document.querySelectorAll('.spec-link[data-spec]').forEach(el => {
        el.addEventListener('click', () => {
            vscode.postMessage({ type: 'openSpec', specId: el.dataset.spec });
        });
    });
    document.querySelectorAll('.feedback-suggestion-btn').forEach(el => {
        el.addEventListener('click', () => {
            const questionEl = el.closest('.feedback-question');
            const input = questionEl?.querySelector('.feedback-answer-input');
            if (input) {
                input.value = el.dataset.answer || '';
                input.focus();
            }
        });
    });
    document.querySelectorAll('.feedback-save-btn').forEach(el => {
        el.addEventListener('click', () => {
            const questionEl = el.closest('.feedback-question');
            const input = questionEl?.querySelector('.feedback-answer-input');
            const answer = input?.value?.trim() || '';
            if (!answer) {
                return;
            }

            el.disabled = true;
            vscode.postMessage({
                type: 'answerQuestion',
                specId: ${JSON.stringify(spec?.id ?? '')},
                questionId: el.dataset.qId,
                questionText: el.dataset.question,
                answer,
            });
        });
    });
</script>
</body></html>`;
    }

    private async answerQuestion(specId: string, questionId: string, questionText: string, answer: string): Promise<void> {
        if (!specId) {
            vscode.window.showWarningMessage('답변을 저장할 스펙을 찾을 수 없습니다.');
            return;
        }

        const spec = this.loader.findSpec(specId);
        if (!spec) {
            vscode.window.showWarningMessage(`스펙을 찾을 수 없습니다: ${specId}`);
            return;
        }

        const feedback = getUserFeedbackState(spec);
        const question = feedback.questions.find((item) => {
            if (questionId && item.id === questionId) {
                return true;
            }

            return item.question === questionText;
        });

        if (!question) {
            vscode.window.showWarningMessage('저장할 질문을 찾을 수 없습니다. 새로고침 후 다시 시도하세요.');
            return;
        }

        await saveQuestionAnswer(this.loader.specsDirectory, specId, question, answer);
        await this.loader.reload();
        vscode.window.showInformationMessage(`질문 응답을 저장했습니다: ${specId}`);
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
            pathMod.join(this.loader.specsDirectory, `${specId}.json`)
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
