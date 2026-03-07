import { Condition, GraphNode, Spec } from './types';

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