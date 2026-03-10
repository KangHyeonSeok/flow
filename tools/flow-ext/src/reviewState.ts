import { Condition, GraphNode, Spec } from './types';

/** 사용자 피드백 질문 항목 (F-009) */
export interface UserQuestion {
    id: string;
    type?: string;
    question: string;
    why?: string;
    status: 'open' | 'answered' | 'dismissed';
    answer?: string;
    answeredAt?: string;
    answerSuggestions?: string[];
    suggestedContextMethods?: string[];
}

/** 사용자 피드백 필요 상태 (F-009) */
export interface UserFeedbackState {
    requiresUserInput: boolean;
    questionStatus: string | null;
    reviewDisposition: string | null;
    questions: UserQuestion[];
    openQuestions: UserQuestion[];
    openQuestionCount: number;
    additionalInformationRequests: string[];
    lastAnsweredAt: string | null;
}

export interface ManualVerificationItem {
    source: 'spec' | 'condition';
    label: string;
    conditionId?: string;
    reason?: string;
}

export interface ReviewState {
    totalConditions: number;
    verifiedConditions: number;
    progressPercent: number;
    allConditionsVerified: boolean;
    requiresManualVerification: boolean;
    manualVerificationItems: ManualVerificationItem[];
    autoVerifyEligible: boolean;
    blockedByOpenQuestions: boolean;
    reviewDisposition: string | null;
    statusSummary: string;
}

type MetadataHolder = {
    metadata?: Record<string, unknown> | null;
};

export function getSpecReviewState(spec: Spec): ReviewState {
    const feedback = getUserFeedbackState(spec);
    const totalConditions = spec.conditions.length;
    const verifiedConditions = spec.conditions.filter((condition) => condition.status === 'verified').length;
    const progressPercent = totalConditions > 0
        ? Math.round((verifiedConditions / totalConditions) * 100)
        : 0;

    const manualVerificationItems = [
        ...getManualVerificationItems(spec, 'spec', spec.id),
        ...spec.conditions.flatMap((condition) =>
            getManualVerificationItems(condition, 'condition', condition.id, condition.id)),
    ];

    const allConditionsVerified = totalConditions > 0 && verifiedConditions === totalConditions;
    const requiresManualVerification = manualVerificationItems.length > 0;
    const reviewDisposition = readString(spec.metadata?.reviewDisposition) ?? null;
    const blockedByOpenQuestions = feedback.openQuestionCount > 0;
    const autoVerifyEligible = allConditionsVerified && !requiresManualVerification && !blockedByOpenQuestions;
    const statusSummary = blockedByOpenQuestions
        ? `열린 질문 ${feedback.openQuestionCount}건이 남아 자동 검증이 보류됨`
        : requiresManualVerification
            ? `수동 검증 ${manualVerificationItems.length}건 필요`
            : autoVerifyEligible
                ? '모든 조건 충족, Runner 자동 검증 대상'
                : describeReviewDisposition(reviewDisposition)
                    ?? (totalConditions > 0
                        ? `조건 ${verifiedConditions}/${totalConditions} 충족`
                        : '조건 없음');

    return {
        totalConditions,
        verifiedConditions,
        progressPercent,
        allConditionsVerified,
        requiresManualVerification,
        manualVerificationItems,
        autoVerifyEligible,
        blockedByOpenQuestions,
        reviewDisposition,
        statusSummary,
    };
}

export function describeReviewDisposition(reviewDisposition: string | null): string | null {
    switch (reviewDisposition) {
        case 'open-question':
        case 'needs-user-decision':
            return '질문 답변이 필요함';
        case 'user-test-required':
            return '사용자 수동 검증 필요';
        case 'test-failed':
            return '검증 실패로 재작업 필요';
        case 'missing-evidence':
            return '근거 부족으로 재검토 필요';
        case 'retry-queued':
            return '재작업 대기';
        case 'review-verified':
            return '자동 검증 완료';
        case 'review-done':
            return 'task 완료 판정';
        default:
            return null;
    }
}

export function getConditionManualVerificationItems(condition: Condition): ManualVerificationItem[] {
    return getManualVerificationItems(condition, 'condition', condition.id, condition.id);
}

export function getNodeManualVerificationItems(node: GraphNode): ManualVerificationItem[] {
    return getManualVerificationItems(node, node.nodeType === 'condition' ? 'condition' : 'spec', node.id, node.id);
}

function getManualVerificationItems(
    holder: MetadataHolder,
    source: 'spec' | 'condition',
    defaultLabel: string,
    conditionId?: string,
): ManualVerificationItem[] {
    const metadata = holder.metadata;
    if (!metadata) {
        return [];
    }

    const requiresManualVerification = readBoolean(metadata.requiresManualVerification) === true;
    const reason = readString(metadata.manualVerificationReason);
    const items = readManualVerificationItems(metadata.manualVerificationItems, source, conditionId);

    if (!requiresManualVerification && items.length === 0) {
        return [];
    }

    if (items.length === 0) {
        return [{ source, label: defaultLabel, conditionId, reason }];
    }

    return items.map((item) => ({
        ...item,
        reason: item.reason ?? reason,
    }));
}

function readManualVerificationItems(
    rawValue: unknown,
    source: 'spec' | 'condition',
    conditionId?: string,
): ManualVerificationItem[] {
    if (!Array.isArray(rawValue)) {
        return [];
    }

    return rawValue.flatMap((entry) => {
        if (typeof entry === 'string' && entry.trim()) {
            return [{ source, label: entry, conditionId }];
        }

        if (!isRecord(entry)) {
            return [];
        }

        const label = readString(entry.label) ?? readString(entry.title);
        if (!label) {
            return [];
        }

        return [{
            source,
            label,
            conditionId,
            reason: readString(entry.reason),
        }];
    });
}

function readBoolean(value: unknown): boolean | undefined {
    if (typeof value === 'boolean') {
        return value;
    }

    if (typeof value === 'string') {
        const normalized = value.toLowerCase();
        if (normalized === 'true') {
            return true;
        }
        if (normalized === 'false') {
            return false;
        }
    }

    return undefined;
}

function readString(value: unknown): string | undefined {
    if (typeof value !== 'string') {
        return undefined;
    }

    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : undefined;
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** 사용자 피드백 필요 상태를 metadata에서 읽어 반환한다 (F-009) */
export function getUserFeedbackState(holder: { metadata?: Record<string, unknown> | null }): UserFeedbackState {
    const metadata = holder.metadata;
    if (!metadata) {
        return {
            requiresUserInput: false,
            questionStatus: null,
            reviewDisposition: null,
            questions: [],
            openQuestions: [],
            openQuestionCount: 0,
            additionalInformationRequests: [],
            lastAnsweredAt: null,
        };
    }

    const reviewMetadata = isRecord(metadata.review) ? metadata.review : undefined;
    const plannerState = readString(metadata.plannerState) ?? null;
    const reviewDisposition = readString(metadata.reviewDisposition) ?? null;
    const questionStatus = readString(metadata.questionStatus)
        ?? (plannerState === 'waiting-user-input' ? 'waiting-user-input' : null);
    const questions = mergeQuestions([
        ...readUserQuestions(metadata.questions),
        ...readUserQuestions(reviewMetadata?.questions),
    ]);
    const additionalInformationRequests = readAdditionalInformationRequests(reviewMetadata?.additionalInformationRequests);
    const openQuestions = questions.filter(q => q.status === 'open');
    const lastAnsweredAt = readString(metadata.lastAnsweredAt) ?? getLatestAnsweredAt(questions) ?? null;
    const requiresUserInput = openQuestions.length > 0;

    return {
        requiresUserInput,
        questionStatus,
        reviewDisposition,
        questions,
        openQuestions,
        openQuestionCount: openQuestions.length,
        additionalInformationRequests,
        lastAnsweredAt,
    };
}

function readUserQuestions(raw: unknown): UserQuestion[] {
    if (!Array.isArray(raw)) { return []; }
    return raw.flatMap((entry) => {
        if (!isRecord(entry)) { return []; }
        const id = readString(entry.id) ?? '';
        const type = readString(entry.type);
        const question = readString(entry.question) ?? readString(entry.text) ?? '';
        if (!question) { return []; }
        const why = readString(entry.why);
        const answer = readString(entry.answer);
        const answeredAt = readString(entry.answeredAt) ?? readString(entry.resolvedAt);
        const statusRaw = readString(entry.status) ?? (answer || answeredAt ? 'answered' : 'open');
        const status: UserQuestion['status'] =
            statusRaw === 'answered' ? 'answered' :
            statusRaw === 'dismissed' ? 'dismissed' : 'open';
        const answerSuggestions = readAnswerSuggestions(entry.options ?? entry.choices, question, type);
        const suggestedContextMethods = Array.isArray(entry.suggestedContextMethods)
            ? (entry.suggestedContextMethods as unknown[]).filter((s): s is string => typeof s === 'string')
            : undefined;
        return [{ id, type, question, why, status, answer, answeredAt, answerSuggestions, suggestedContextMethods }];
    });
}

function readAdditionalInformationRequests(raw: unknown): string[] {
    if (!Array.isArray(raw)) {
        return [];
    }

    return raw.flatMap((entry) => {
        const question = readString(entry);
        return question ? [question] : [];
    });
}

function mergeQuestions(questions: UserQuestion[]): UserQuestion[] {
    const merged = new Map<string, UserQuestion>();

    for (const question of questions) {
        const key = question.id || question.question;
        if (!key) {
            continue;
        }

        const existing = merged.get(key);
        if (!existing) {
            merged.set(key, question);
            continue;
        }

        merged.set(key, {
            ...existing,
            ...question,
            type: question.type ?? existing.type,
            why: question.why ?? existing.why,
            answerSuggestions: mergeSuggestions(existing.answerSuggestions, question.answerSuggestions),
            suggestedContextMethods: question.suggestedContextMethods ?? existing.suggestedContextMethods,
            answer: question.answer ?? existing.answer,
            answeredAt: question.answeredAt ?? existing.answeredAt,
            status: pickPreferredStatus(existing.status, question.status),
        });
    }

    return Array.from(merged.values());
}

function pickPreferredStatus(left: UserQuestion['status'], right: UserQuestion['status']): UserQuestion['status'] {
    const rank: Record<UserQuestion['status'], number> = {
        open: 3,
        answered: 2,
        dismissed: 1,
    };

    return rank[right] > rank[left] ? right : left;
}

function getLatestAnsweredAt(questions: UserQuestion[]): string | null {
    const answeredAtValues = questions
        .map((question) => question.answeredAt)
        .filter((value): value is string => !!value)
        .sort((left, right) => right.localeCompare(left));

    return answeredAtValues[0] ?? null;
}

function readAnswerSuggestions(raw: unknown, question: string, type?: string): string[] | undefined {
    const explicit = readStringArray(raw);
    if (explicit && explicit.length > 0) {
        return explicit;
    }

    const derived = deriveAnswerSuggestions(question, type);
    return derived.length > 0 ? derived : undefined;
}

function readStringArray(raw: unknown): string[] | undefined {
    if (!Array.isArray(raw)) {
        return undefined;
    }

    const values = raw.flatMap((entry) => {
        if (typeof entry === 'string') {
            const trimmed = entry.trim();
            return trimmed ? [trimmed] : [];
        }

        if (!isRecord(entry)) {
            return [];
        }

        const label = readString(entry.label) ?? readString(entry.value) ?? readString(entry.title);
        return label ? [label] : [];
    });

    return values.length > 0 ? Array.from(new Set(values)) : undefined;
}

function deriveAnswerSuggestions(question: string, type?: string): string[] {
    if (type !== 'user-decision') {
        return [];
    }

    const eitherOr = question
        .split(/\s*,?\s*아니면\s+/)
        .map(part => sanitizeSuggestedAnswer(part))
        .filter(Boolean);
    if (eitherOr.length >= 2) {
        return Array.from(new Set(eitherOr));
    }

    const betweenMatch = question.match(/^(.+?)와\s+(.+?)\s+중/);
    if (betweenMatch) {
        return [sanitizeSuggestedAnswer(betweenMatch[1]), sanitizeSuggestedAnswer(betweenMatch[2])].filter(Boolean);
    }

    return [];
}

function sanitizeSuggestedAnswer(value: string): string {
    return value
        .replace(/[?？]$/g, '')
        .replace(/원합니까$|맞나요$|맞습니까$|선택할까요$|우선할까요$/g, '')
        .trim();
}

function mergeSuggestions(left?: string[], right?: string[]): string[] | undefined {
    const merged = [...(left ?? []), ...(right ?? [])].filter((value) => value.trim().length > 0);
    return merged.length > 0 ? Array.from(new Set(merged)) : undefined;
}