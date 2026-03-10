/**
 * SpecLoader - ~/.flow/<project>/specs/*.json 파일을 읽어 그래프 데이터를 구성
 */
import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as os from 'os';
import { Spec, Condition, GraphNode, GraphEdge, SpecGraph, SpecStatus, isValidStatus } from './types';
import { recordBrokenSpec, markSpecResolved, getUnresolvedRecords, BrokenSpecDiagRecord } from './brokenSpecDiag';

export class SpecLoader {
    private specsDir: string;
    private specs: Spec[] = [];
    private graph: SpecGraph | null = null;
    private currentLoad: Promise<SpecGraph> | null = null;
    private reloadPromise: Promise<void> | null = null;
    private reloadQueued = false;
    private loadRevision = 0;
    private appliedRevision = 0;

    private _onDidChange = new vscode.EventEmitter<void>();
    readonly onDidChange = this._onDidChange.event;

    private _onDidStateChange = new vscode.EventEmitter<void>();
    readonly onDidStateChange = this._onDidStateChange.event;

    private watcher: vscode.FileSystemWatcher | undefined;
    private watcherDebounceTimer: NodeJS.Timeout | undefined;

    /** 마지막 loadSnapshot에서 status만 바뀌었는지 여부 */
    private _lastLoadStatusOnly = false;

    /** 마지막으로 로드된 스펙들의 status 맵 (status-only 변경 감지용) */
    private specJsonStatuses = new Map<string, SpecStatus>();

    constructor(private workspaceRoot: string) {
        this.specsDir = this.resolveSpecsDir(workspaceRoot);
        this.setupWatcher();
    }

    /**
     * 확장은 항상 ~/.flow/<project>/specs/를 기준으로 스펙을 로드한다.
     */
    private resolveSpecsDir(workspaceRoot: string): string {
        const projectRoot = this.resolveProjectRoot(workspaceRoot);
        return path.join(os.homedir(), '.flow', path.basename(projectRoot), 'specs');
    }

    private resolveProjectRoot(workspaceRoot: string): string {
        let current = workspaceRoot;
        while (true) {
            if (fs.existsSync(path.join(current, 'flow.ps1')) || fs.existsSync(path.join(current, 'flow.sh'))) {
                return current;
            }

            if (fs.existsSync(path.join(current, '.flow'))) {
                return current;
            }

            const parent = path.dirname(current);
            if (parent === current) {
                return workspaceRoot;
            }

            current = parent;
        }
    }

    /** 현재 사용 중인 스펙 디렉토리 경로 */
    get specsDirectory(): string {
        return this.specsDir;
    }

    private setupWatcher(): void {
        const pattern = new vscode.RelativePattern(this.specsDir, '*.json');
        this.watcher = vscode.workspace.createFileSystemWatcher(pattern);
        const debouncedReload = () => {
            if (this.watcherDebounceTimer) { clearTimeout(this.watcherDebounceTimer); }
            this.watcherDebounceTimer = setTimeout(() => void this.reload(), 1500);
        };
        this.watcher.onDidChange(debouncedReload);
        this.watcher.onDidCreate(debouncedReload);
        this.watcher.onDidDelete(debouncedReload);
    }

    private refreshSpecsDir(): void {
        const nextSpecsDir = this.resolveSpecsDir(this.workspaceRoot);
        if (nextSpecsDir === this.specsDir) {
            return;
        }

        this.watcher?.dispose();
        this.specsDir = nextSpecsDir;
        this.setupWatcher();
    }

    /** 스펙 파일 로드 및 그래프 빌드 */
    async load(): Promise<SpecGraph> {
        const revision = ++this.loadRevision;
        const loadPromise = this.loadSnapshot(revision);
        this.currentLoad = loadPromise;

        try {
            return await loadPromise;
        } finally {
            if (this.currentLoad === loadPromise) {
                this.currentLoad = null;
            }
        }
    }

    /** 캐시된 그래프 반환 또는 새로 로드 */
    async getGraph(): Promise<SpecGraph> {
        if (!this.graph) {
            return this.currentLoad ?? this.load();
        }
        return this.graph;
    }

    /** 스펙 목록 반환 */
    getSpecs(): Spec[] {
        return this.specs;
    }

    /** ID로 스펙 찾기 */
    findSpec(id: string): Spec | undefined {
        return this.specs.find(s => s.id === id);
    }

    /** ID로 조건 찾기 */
    findCondition(id: string): { spec: Spec; condition: Condition } | undefined {
        for (const spec of this.specs) {
            const cond = spec.conditions.find(c => c.id === id);
            if (cond) {
                return { spec, condition: cond };
            }
        }
        return undefined;
    }

    /** 모든 고유 태그 목록 */
    getAllTags(): string[] {
        const tags = new Set<string>();
        for (const spec of this.specs) {
            for (const tag of spec.tags) {
                tags.add(tag);
            }
        }
        return Array.from(tags).sort();
    }

    /** 모든 상태 목록 */
    getAllStatuses(): SpecStatus[] {
        const statuses = new Set<SpecStatus>();
        for (const spec of this.specs) {
            statuses.add(spec.status);
            for (const condition of spec.conditions) {
                statuses.add(condition.status);
            }
        }
        return Array.from(statuses);
    }

    /** 새로고침 */
    async reload(): Promise<void> {
        if (this.reloadPromise) {
            this.reloadQueued = true;
            return this.reloadPromise;
        }

        this.reloadPromise = (async () => {
            do {
                this.reloadQueued = false;
                this.refreshSpecsDir();
                await this.load();
                if (this._lastLoadStatusOnly) {
                    this._onDidStateChange.fire();
                } else {
                    this._onDidChange.fire();
                }
            } while (this.reloadQueued);
        })();

        try {
            await this.reloadPromise;
        } finally {
            this.reloadPromise = null;
        }
    }

    /** ~/.flow/<project>/specs/ 하위 JSON 파일 목록 */
    private async findSpecFiles(): Promise<string[]> {
        if (!fs.existsSync(this.specsDir)) {
            return [];
        }

        return fs.readdirSync(this.specsDir)
            .filter(entry => entry.endsWith('.json') && !entry.startsWith('.'))
            .map(entry => path.join(this.specsDir, entry));
    }

    /** 스펙 파일을 읽어 최신 스냅샷을 만든 뒤 최신 로드만 커밋 */
    private async loadSnapshot(revision: number): Promise<SpecGraph> {
        const files = await this.findSpecFiles();
        const nextSpecs: Spec[] = [];
        const seenSpecIds = new Set<string>();

        for (const file of files) {
            let content: string | undefined;
            try {
                content = fs.readFileSync(file, 'utf-8');
                const spec = JSON.parse(content) as Spec;
                if (spec.id && (spec.nodeType === 'feature' || spec.nodeType === 'task')) {
                    if (!isValidStatus(spec.status)) {
                        vscode.window.showWarningMessage(
                            `스펙 '${spec.id}'의 status '${spec.status}'는 유효하지 않습니다. 건너뜁니다.`
                        );
                        continue;
                    }

                    if (seenSpecIds.has(spec.id)) {
                        vscode.window.showWarningMessage(
                            `중복 스펙 ID 감지: '${spec.id}'. 첫 번째 항목만 사용합니다.`
                        );
                        continue;
                    }

                    markSpecResolved(this.workspaceRoot, file);
                    seenSpecIds.add(spec.id);
                    nextSpecs.push(spec);
                }
            } catch (error) {
                recordBrokenSpec(this.workspaceRoot, file, error, content);
                vscode.window.showWarningMessage(
                    `스펙 파일 파싱 실패: ${path.basename(file)} - ${error}`
                );
            }
        }

        const nextGraph = this.buildGraph(nextSpecs);
        if (revision >= this.appliedRevision) {
            // status-only 변경 감지: 이전 스펙과 비교 (스펙 수 동일, status 외 내용 동일한 경우)
            const prevSpecs = this.specs;
            let statusOnly = prevSpecs.length > 0 && prevSpecs.length === nextSpecs.length;
            if (statusOnly) {
                const prevMap = new Map(prevSpecs.map(s => [s.id, s]));
                for (const next of nextSpecs) {
                    const prev = prevMap.get(next.id);
                    if (!prev) { statusOnly = false; break; }
                    // status를 제외한 나머지 필드 비교 (얕은 비교)
                    const prevCopy = { ...prev, status: '' };
                    const nextCopy = { ...next, status: '' };
                    if (JSON.stringify(prevCopy) !== JSON.stringify(nextCopy)) {
                        statusOnly = false;
                        break;
                    }
                }
            }

            this.specs = nextSpecs;
            this.graph = nextGraph;
            this.appliedRevision = revision;
            this._lastLoadStatusOnly = statusOnly;
            // 새 status 맵 업데이트
            this.specJsonStatuses.clear();
            for (const s of nextSpecs) { this.specJsonStatuses.set(s.id, s.status); }
            return nextGraph;
        }

        return this.graph ?? nextGraph;
    }

    /** Spec 배열로부터 그래프 데이터 구성 */
    private buildGraph(specs: Spec[]): SpecGraph {
        const nodes: GraphNode[] = [];
        const edges: GraphEdge[] = [];

        for (const spec of specs) {
            nodes.push({
                id: spec.id,
                nodeType: spec.nodeType,
                label: spec.title,
                description: spec.description,
                status: spec.status,
                parent: spec.parent,
                tags: spec.tags,
                codeRefs: spec.codeRefs,
                evidence: spec.evidence,
                metadata: spec.metadata,
                githubRefs: spec.githubRefs,
                docLinks: spec.docLinks,
            });

            if (spec.parent) {
                edges.push({
                    source: spec.parent,
                    target: spec.id,
                    type: 'parent',
                });
            }

            for (const dependency of spec.dependencies) {
                edges.push({
                    source: dependency,
                    target: spec.id,
                    type: 'dependency',
                });
            }

            // F-021-C5: supersedes/mutates 관계 엣지 추가
            if (spec.supersedes) {
                for (const oldId of spec.supersedes) {
                    edges.push({
                        source: spec.id,
                        target: oldId,
                        type: 'supersedes',
                    });
                }
            }
            if (spec.mutates) {
                for (const targetId of spec.mutates) {
                    edges.push({
                        source: spec.id,
                        target: targetId,
                        type: 'mutates',
                    });
                }
            }

            for (const condition of spec.conditions) {
                nodes.push({
                    id: condition.id,
                    nodeType: 'condition',
                    label: condition.id,
                    description: condition.description,
                    status: condition.status,
                    parent: spec.id,
                    tags: [],
                    codeRefs: condition.codeRefs,
                    evidence: condition.evidence,
                    metadata: condition.metadata,
                    githubRefs: condition.githubRefs,
                    docLinks: condition.docLinks,
                    featureId: spec.id,
                });

                edges.push({
                    source: spec.id,
                    target: condition.id,
                    type: 'condition',
                });
            }
        }

        this.detectCycles(nodes, edges);
        return { nodes, edges, specs };
    }

    /** Kahn 알고리즘으로 순환 참조 감지 */
    private detectCycles(nodes: GraphNode[], edges: GraphEdge[]): void {
        const depEdges = edges.filter(edge => edge.type === 'dependency');
        const specNodeIds = new Set(
            nodes
                .filter(node => node.nodeType === 'feature' || node.nodeType === 'task')
                .map(node => node.id)
        );

        const inDegree = new Map<string, number>();
        const adjacency = new Map<string, string[]>();

        for (const id of specNodeIds) {
            inDegree.set(id, 0);
            adjacency.set(id, []);
        }

        for (const edge of depEdges) {
            if (specNodeIds.has(edge.source) && specNodeIds.has(edge.target)) {
                adjacency.get(edge.source)!.push(edge.target);
                inDegree.set(edge.target, (inDegree.get(edge.target) || 0) + 1);
            }
        }

        const queue: string[] = [];
        for (const [id, degree] of inDegree) {
            if (degree === 0) {
                queue.push(id);
            }
        }

        let processed = 0;
        while (queue.length > 0) {
            const node = queue.shift()!;
            processed++;
            for (const neighbor of adjacency.get(node) || []) {
                const newDegree = (inDegree.get(neighbor) || 1) - 1;
                inDegree.set(neighbor, newDegree);
                if (newDegree === 0) {
                    queue.push(neighbor);
                }
            }
        }

        if (processed < specNodeIds.size) {
            const cycleNodes = Array.from(inDegree.entries())
                .filter(([, degree]) => degree > 0)
                .map(([id]) => id);
            vscode.window.showErrorMessage(`순환 참조 감지: ${cycleNodes.join(', ')}`);
        }
    }

    /** 손상 스펙 진단 레코드 반환 (F-025-C2) */
    getBrokenSpecs(): BrokenSpecDiagRecord[] {
        return getUnresolvedRecords(this.workspaceRoot);
    }

    dispose(): void {
        if (this.watcherDebounceTimer) { clearTimeout(this.watcherDebounceTimer); }
        this.watcher?.dispose();
        this._onDidChange.dispose();
        this._onDidStateChange.dispose();
    }
}
