/**
 * Spec Graph 타입 정의
 * ~/.flow/<project>/specs/ JSON 스키마 v2에 대응
 */

/** 스펙 상태 */
export type SpecStatus = 'draft' | 'queued' | 'working' | 'needs-review' | 'verified' | 'deprecated' | 'done';

/** 노드 타입 */
export type NodeType = 'feature' | 'condition' | 'task';

/** 증거 타입 */
export type EvidenceType = 'screenshot' | 'log' | 'metric' | 'test-result';

/** GitHub Ref 타입 */
export type GitHubRefType = 'issue' | 'pr' | 'discussion';

/** 문서 링크 타입 */
export type DocLinkType = 'doc' | 'reference' | 'url';

/** GitHub 이슈/PR/Discussion 연결 (v4) */
export interface GitHubRef {
    type: GitHubRefType;
    number: number;
    title?: string;
    /** 미입력 시 number 기반으로 자동 구성 가능 */
    url?: string;
}

/** 관련 문서·참고자료 링크 (v4) */
export interface DocLink {
    type: DocLinkType;
    title: string;
    /** doc 타입: 워크스페이스 기준 상대 경로 (에디터에서 바로 열기 지원) */
    path?: string;
    /** reference/url 타입: 외부 URL (브라우저로 열기) */
    url?: string;
}

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
    metadata?: Record<string, unknown>;
    /** v4: GitHub 이슈/PR/Discussion 연결 */
    githubRefs?: GitHubRef[];
    /** v4: 관련 문서·참고자료 링크 */
    docLinks?: DocLink[];
}

/** 스펙 변경 이력 항목 */
export interface ChangeLogEntry {
    type: 'create' | 'mutate' | 'supersede' | 'deprecate' | 'restore';
    at: string;
    author: string;
    summary: string;
    relatedIds?: string[];
}

/** 스펙 (Feature/Task) - 그래프의 주 노드 */
export interface Spec {
    schemaVersion: number;
    id: string;
    nodeType: 'feature' | 'task';
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
    /** F-021: 이 스펙이 실질적으로 대체하는 이전 스펙 ID 목록 */
    supersedes?: string[];
    /** F-021: 이 스펙을 대체한 신규 스펙 ID 목록 */
    supersededBy?: string[];
    /** F-021: 이 스펙이 in-place로 변형하는 대상 스펙 ID 목록 */
    mutates?: string[];
    /** F-021: 이 스펙을 in-place 변형한 task 스펙 ID 목록 */
    mutatedBy?: string[];
    /** F-021: 스펙 변경 이력 */
    changeLog?: ChangeLogEntry[];
    /** v4: GitHub 이슈/PR/Discussion 연결 */
    githubRefs?: GitHubRef[];
    /** v4: 관련 문서·참고자료 링크 */
    docLinks?: DocLink[];
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
    metadata?: Record<string, unknown>;
    /** v4: GitHub 이슈/PR/Discussion 연결 */
    githubRefs?: GitHubRef[];
    /** v4: 관련 문서·참고자료 링크 */
    docLinks?: DocLink[];
    /** condition인 경우, 소속 feature id */
    featureId?: string;
}

/** 그래프 엣지 */
export interface GraphEdge {
    source: string;
    target: string;
    /** F-021: 'supersedes' | 'mutates' 관계 타입 추가 */
    type: 'parent' | 'dependency' | 'condition' | 'supersedes' | 'mutates';
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
    'queued': '#9c27b0',
    'working': '#2196f3',
    'needs-review': '#ff9800',
    'verified': '#4caf50',
    'deprecated': '#f44336',
    'done': '#795548',
};

/** 상태별 아이콘 ID (codicon) */
export const STATUS_ICONS: Record<SpecStatus, string> = {
    'draft': 'circle-outline',
    'queued': 'send',
    'working': 'circle-filled',
    'needs-review': 'warning',
    'verified': 'check',
    'deprecated': 'close',
    'done': 'check-all',
};

/** 유효한 상태값 목록 (런타임 유효성 검사용) — F-090-C1 */
export const VALID_STATUSES: readonly SpecStatus[] = [
    'draft', 'queued', 'working', 'needs-review', 'verified', 'deprecated', 'done',
] as const;

/** 상태값 유효성 검사 — F-090-C1 */
export function isValidStatus(s: string): s is SpecStatus {
    return (VALID_STATUSES as readonly string[]).includes(s);
}
