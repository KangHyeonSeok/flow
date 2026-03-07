import { Condition, GraphNode, Spec } from './types';

/** 사용자 피드백 질문 항목 (F-009) */
export interface UserQuestion {
    id: string;
    question: string;
    status: 'open' | 'answered' | 'dismissed';
    answer?: string;
    answeredAt?: string;
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

    const requiresUserInput = readBoolean(metadata.requiresUserInput) === true;
    const questionStatus = readString(metadata.questionStatus) ?? null;
    const questions = readUserQuestions(metadata.questions);
    const openQuestions = questions.filter(q => q.status === 'open');
    const lastAnsweredAt = readString(metadata.lastAnsweredAt) ?? null;

    return {
        requiresUserInput: requiresUserInput || questionStatus === 'waiting-user-input',
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
        const question = readString(entry.question) ?? readString(entry.text) ?? '';
        if (!question) { return []; }
        const statusRaw = readString(entry.status) ?? 'open';
        const status: UserQuestion['status'] =
            statusRaw === 'answered' ? 'answered' :
            statusRaw === 'dismissed' ? 'dismissed' : 'open';
        const answer = readString(entry.answer);
        const answeredAt = readString(entry.answeredAt);
        const suggestedContextMethods = Array.isArray(entry.suggestedContextMethods)
            ? (entry.suggestedContextMethods as unknown[]).filter((s): s is string => typeof s === 'string')
            : undefined;
        return [{ id, question, status, answer, answeredAt, suggestedContextMethods }];
    });
}