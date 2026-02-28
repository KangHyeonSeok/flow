/**
 * Spec Graph 타입 정의
 * docs/specs/ JSON 스키마 v2에 대응
 */

/** 스펙 상태 */
export type SpecStatus = 'draft' | 'active' | 'needs-review' | 'verified' | 'deprecated';

/** 노드 타입 */
export type NodeType = 'feature' | 'condition';

/** 증거 타입 */
export type EvidenceType = 'screenshot' | 'log' | 'metric' | 'test-result';

/** 증거 */
export interface Evidence {
    type: EvidenceType;
    path: string;
    capturedAt: string;
    platform?: string;
    summary?: string;
}

/** 수락 조건 (Condition) - 그래프에서 하위 노드로 표현 */
export interface Condition {
    id: string;
    nodeType: 'condition';
    description: string;
    status: SpecStatus;
    codeRefs: string[];
    evidence: Evidence[];
}

/** 스펙 (Feature) - 그래프의 주 노드 */
export interface Spec {
    schemaVersion: number;
    id: string;
    nodeType: 'feature';
    title: string;
    description: string;
    status: SpecStatus;
    parent: string | null;
    dependencies: string[];
    conditions: Condition[];
    codeRefs: string[];
    evidence: Evidence[];
    tags: string[];
    metadata: Record<string, unknown>;
    createdAt: string;
    updatedAt: string;
}

/** 그래프 노드 (Feature + Condition 통합) */
export interface GraphNode {
    id: string;
    nodeType: NodeType;
    label: string;
    description: string;
    status: SpecStatus;
    parent: string | null;
    tags: string[];
    codeRefs: string[];
    evidence: Evidence[];
    /** condition인 경우, 소속 feature id */
    featureId?: string;
}

/** 그래프 엣지 */
export interface GraphEdge {
    source: string;
    target: string;
    type: 'parent' | 'dependency' | 'condition';
}

/** 전체 그래프 데이터 */
export interface SpecGraph {
    nodes: GraphNode[];
    edges: GraphEdge[];
    specs: Spec[];
}

/** 상태별 색상 매핑 */
export const STATUS_COLORS: Record<SpecStatus, string> = {
    'draft': '#9e9e9e',
    'active': '#2196f3',
    'needs-review': '#ff9800',
    'verified': '#4caf50',
    'deprecated': '#f44336',
};

/** 상태별 아이콘 ID (codicon) */
export const STATUS_ICONS: Record<SpecStatus, string> = {
    'draft': 'circle-outline',
    'active': 'circle-filled',
    'needs-review': 'warning',
    'verified': 'check',
    'deprecated': 'close',
};
