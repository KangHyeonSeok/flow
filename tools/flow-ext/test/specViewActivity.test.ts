import test from 'node:test';
import assert from 'node:assert/strict';
import { renderSpecActivitySection, resolveSpecViewSelection } from '../src/specViewActivity';
import { Spec, Condition, Evidence, SpecActivityEntry } from '../src/types';

function makeCondition(id: string): Condition {
    return {
        id,
        nodeType: 'condition',
        description: `${id} description`,
        status: 'draft',
        codeRefs: [],
        evidence: [],
    };
}

function makeSpec(id: string, title: string, overrides: Partial<Spec> = {}): Spec {
    return {
        schemaVersion: 2,
        id,
        nodeType: 'feature',
        title,
        description: `${title} description`,
        status: 'working',
        parent: null,
        dependencies: [],
        conditions: [makeCondition(`${id}-C1`)],
        codeRefs: [],
        evidence: [] as Evidence[],
        tags: [],
        metadata: {},
        activity: [],
        createdAt: '2026-03-10T14:00:00.000Z',
        updatedAt: '2026-03-10T14:00:00.000Z',
        ...overrides,
    };
}

test('F-001-C1/C2: activity section renders newest entries first with readable metadata', () => {
    const spec = makeSpec('F-001', 'Activity Spec', {
        activity: [
            {
                type: 'status-update',
                at: '2026-03-10T14:15:00.000Z',
                author: 'flow-bot',
                summary: 'Older update',
                relatedIds: ['F-003'],
            },
            {
                kind: 'implementation',
                at: '2026-03-10T14:16:00.000Z',
                author: 'copilot',
                summary: 'Latest update',
                relatedIds: ['F-010', 'F-011'],
                statusChange: { from: 'queued', to: 'working' },
                outcome: 'handoff',
            } satisfies SpecActivityEntry,
        ],
    });

    const html = renderSpecActivitySection(spec);

    assert.match(html, /Activity \(2\)/);
    assert.ok(html.indexOf('Latest update') < html.indexOf('Older update'), 'latest activity should come first');
    assert.match(html, /Implementation/);
    assert.match(html, /2026-03-10 14:16 UTC/);
    assert.match(html, /작성자: copilot/);
    assert.match(html, /관련 스펙: F-010, F-011/);
    assert.match(html, /상태 변경: queued → working/);
    assert.match(html, /결과: handoff/);
});

test('F-001-C3: empty or partial activity stays safe without breaking rendering', () => {
    const withoutActivity = makeSpec('F-002', 'No Activity');
    assert.equal(renderSpecActivitySection(withoutActivity), '');

    const partial = makeSpec('F-003', 'Partial Activity', {
        activity: [
            {
                summary: 'Summary only entry',
            } satisfies SpecActivityEntry,
        ],
    });

    const html = renderSpecActivitySection(partial);

    assert.match(html, /Activity \(1\)/);
    assert.match(html, /Summary only entry/);
    assert.match(html, />Activity</);
});

test('F-001-C4: selection switches and refreshed data use only the current visible specs', () => {
    const root = makeSpec('F-010', 'Root');
    const focusedOld = makeSpec('F-011', 'Focused', {
        parent: 'F-010',
        activity: [{ summary: 'Old focused summary', at: '2026-03-10T14:10:00.000Z' }],
    });
    const child = makeSpec('F-012', 'Child', { parent: 'F-011' });
    const sibling = makeSpec('F-013', 'Sibling', {
        parent: 'F-010',
        activity: [{ summary: 'Sibling summary', at: '2026-03-10T14:11:00.000Z' }],
    });

    const allSelection = resolveSpecViewSelection([root, focusedOld, child, sibling], null);
    assert.deepEqual(allSelection.specs.map((spec) => spec.id), ['F-010', 'F-011', 'F-012', 'F-013']);

    const focusedNew = makeSpec('F-011', 'Focused', {
        parent: 'F-010',
        activity: [{ summary: 'Fresh focused summary', at: '2026-03-10T14:20:00.000Z' }],
    });
    const focusedSelection = resolveSpecViewSelection([root, focusedNew, child, sibling], 'F-011');

    assert.deepEqual(focusedSelection.specs.map((spec) => spec.id), ['F-010', 'F-011', 'F-012']);

    const html = renderSpecActivitySection(focusedSelection.specs[1]);
    assert.match(html, /Fresh focused summary/);
    assert.doesNotMatch(html, /Old focused summary/);
    assert.doesNotMatch(html, /Sibling summary/);
});
