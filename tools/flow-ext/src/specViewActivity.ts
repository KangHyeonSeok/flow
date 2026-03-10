import { Spec, SpecActivityEntry } from './types';

export interface SpecViewSelection {
    specs: Spec[];
    focusedSpec: Spec | null;
}

export function resolveSpecViewSelection(allSpecs: Spec[], focusedId: string | null): SpecViewSelection {
    if (!focusedId) {
        return { specs: allSpecs, focusedSpec: null };
    }

    const focusedSpec = allSpecs.find((spec) => spec.id === focusedId) ?? null;
    if (!focusedSpec) {
        return { specs: allSpecs, focusedSpec: null };
    }

    const selected: Spec[] = [];
    const seen = new Set<string>();

    if (focusedSpec.parent) {
        const parent = allSpecs.find((spec) => spec.id === focusedSpec.parent);
        if (parent) {
            selected.push(parent);
            seen.add(parent.id);
        }
    }

    selected.push(focusedSpec);
    seen.add(focusedSpec.id);

    for (const child of allSpecs.filter((spec) => spec.parent === focusedSpec.id).sort((a, b) => a.id.localeCompare(b.id))) {
        if (seen.has(child.id)) {
            continue;
        }

        selected.push(child);
        seen.add(child.id);
    }

    return { specs: selected, focusedSpec };
}

export function renderSpecActivitySection(spec: Spec): string {
    const entries = normalizeEntries(spec.activity ?? []);
    if (entries.length === 0) {
        return '';
    }

    return `
    <div class="section-title">Activity (${entries.length})</div>
    <ul class="activity-list">
        ${entries.map(renderActivityEntry).join('')}
    </ul>`;
}

function normalizeEntries(activity: SpecActivityEntry[]): SpecActivityEntry[] {
    return [...activity]
        .map((entry, index) => ({
            entry,
            index,
            timestamp: entry.at ? Date.parse(entry.at) : Number.NEGATIVE_INFINITY,
        }))
        .sort((left, right) => {
            if (left.timestamp !== right.timestamp) {
                return right.timestamp - left.timestamp;
            }

            return left.index - right.index;
        })
        .map((item) => item.entry);
}

function renderActivityEntry(entry: SpecActivityEntry): string {
    const label = toDisplayLabel(entry.type ?? entry.kind ?? 'activity');
    const timestamp = entry.at ? formatActivityTimestamp(entry.at) : '';
    const author = entry.author ?? entry.actor ?? '';
    const metaItems: string[] = [];

    if (author) {
        metaItems.push(`작성자: ${author}`);
    }
    if (entry.relatedIds && entry.relatedIds.length > 0) {
        metaItems.push(`관련 스펙: ${entry.relatedIds.join(', ')}`);
    }
    if (entry.statusChange?.from && entry.statusChange?.to) {
        metaItems.push(`상태 변경: ${entry.statusChange.from} → ${entry.statusChange.to}`);
    }
    if (entry.outcome) {
        metaItems.push(`결과: ${entry.outcome}`);
    }
    if (entry.role) {
        metaItems.push(`역할: ${entry.role}`);
    }
    if (entry.model) {
        metaItems.push(`모델: ${entry.model}`);
    }

    return `
        <li class="activity-item">
            <div class="activity-head">
                <span class="activity-kind">${esc(label)}</span>
                ${timestamp ? `<span class="activity-time">${esc(timestamp)}</span>` : ''}
            </div>
            ${entry.summary ? `<div class="activity-summary">${esc(entry.summary)}</div>` : ''}
            ${metaItems.length > 0 ? `<div class="activity-meta">${metaItems.map((item) => `<span class="activity-meta-item">${esc(item)}</span>`).join('')}</div>` : ''}
        </li>`;
}

function toDisplayLabel(value: string): string {
    return value
        .replace(/[_-]+/g, ' ')
        .replace(/\s+/g, ' ')
        .trim()
        .replace(/\b\w/g, (char) => char.toUpperCase());
}

function formatActivityTimestamp(value: string): string {
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return value;
    }

    const year = date.getUTCFullYear();
    const month = String(date.getUTCMonth() + 1).padStart(2, '0');
    const day = String(date.getUTCDate()).padStart(2, '0');
    const hour = String(date.getUTCHours()).padStart(2, '0');
    const minute = String(date.getUTCMinutes()).padStart(2, '0');
    return `${year}-${month}-${day} ${hour}:${minute} UTC`;
}

function esc(str: string): string {
    return str.replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
}
