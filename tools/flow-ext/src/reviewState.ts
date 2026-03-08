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
    questions: UserQuestion[];
    openQuestions: UserQuestion[];
    openQuestionCount: number;
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
}

type MetadataHolder = {
    metadata?: Record<string, unknown> | null;
};

export function getSpecReviewState(spec: Spec): ReviewState {
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

    return {
        totalConditions,
        verifiedConditions,
        progressPercent,
        allConditionsVerified,
        requiresManualVerification,
        manualVerificationItems,
        autoVerifyEligible: allConditionsVerified && !requiresManualVerification,
    };
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
        return { requiresUserInput: false, questionStatus: null, questions: [], openQuestions: [], openQuestionCount: 0, lastAnsweredAt: null };
    }

    const reviewMetadata = isRecord(metadata.review) ? metadata.review : undefined;
    const plannerState = readString(metadata.plannerState) ?? null;
    const reviewDisposition = readString(metadata.reviewDisposition) ?? null;
    const questionStatus = readString(metadata.questionStatus)
        ?? (plannerState === 'waiting-user-input' ? 'waiting-user-input' : null);
    const questions = mergeQuestions([
        ...readUserQuestions(metadata.questions),
        ...readUserQuestions(reviewMetadata?.questions),
        ...readAdditionalInformationRequests(reviewMetadata?.additionalInformationRequests),
    ]);
    const openQuestions = questions.filter(q => q.status === 'open');
    const lastAnsweredAt = readString(metadata.lastAnsweredAt) ?? getLatestAnsweredAt(questions) ?? null;
    const requiresUserInput = readBoolean(metadata.requiresUserInput) === true
        || readBoolean(reviewMetadata?.requiresUserInput) === true
        || questionStatus === 'waiting-user-input'
        || plannerState === 'waiting-user-input'
        || reviewDisposition === 'needs-user-decision'
        || openQuestions.length > 0;

    return {
        requiresUserInput,
        questionStatus,
        questions,
        openQuestions,
        openQuestionCount: openQuestions.length,
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

function readAdditionalInformationRequests(raw: unknown): UserQuestion[] {
    if (!Array.isArray(raw)) {
        return [];
    }

    return raw.flatMap((entry, idx) => {
        const question = readString(entry);
        if (!question) {
            return [];
        }

        return [{
            id: `review-request-${idx + 1}`,
            type: 'missing-info',
            question,
            status: 'open',
        }];
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