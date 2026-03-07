/**
 * SpecLoader - docs/specs/*.json 파일을 읽어 그래프 데이터를 구성
 */
import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
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

    private watcher: vscode.FileSystemWatcher | undefined;

    constructor(private workspaceRoot: string) {
        this.specsDir = this.resolveSpecsDir(workspaceRoot);
        this.setupWatcher();
    }

    /**
     * specRepository 설정이 있으면 .flow/spec-cache/specs/ (runner와 동일 경로)를 우선 반환한다.
     * 설정은 .flow/config.json 에서 읽는다. (runner-config.json은 config.json으로 통합됨)
     * 로컬 docs/specs/에 파일이 있으면 그것을 우선 사용한다.
     */
    private resolveSpecsDir(workspaceRoot: string): string {
        const localSpecsDir = path.join(workspaceRoot, 'docs', 'specs');
        if (fs.existsSync(localSpecsDir)) {
            const jsonFiles = fs.readdirSync(localSpecsDir).filter(f => f.endsWith('.json'));
            if (jsonFiles.length > 0) {
                return localSpecsDir;
            }
        }

        const specRepository = this.readSpecRepository(workspaceRoot);
        if (specRepository) {
            const specCacheSpecsDir = path.join(workspaceRoot, '.flow', 'spec-cache', 'specs');
            if (fs.existsSync(specCacheSpecsDir)) {
                return specCacheSpecsDir;
            }

            const repoName = this.extractRepoName(specRepository);
            const userHome = process.env.HOME
                || process.env.USERPROFILE
                || require('os').homedir();
            const repoDir = path.join(userHome, '.flow', 'specs', repoName);
            if (fs.existsSync(repoDir)) {
                const subDir = path.join(repoDir, 'specs');
                return fs.existsSync(subDir) ? subDir : repoDir;
            }
        }

        return localSpecsDir;
    }

    /**
     * .flow/config.json 에서 specRepository를 읽는다.
     * (runner-config.json은 config.json으로 통합됨)
     */
    private readSpecRepository(workspaceRoot: string): string | undefined {
        const configPath = path.join(workspaceRoot, '.flow', 'config.json');
        if (fs.existsSync(configPath)) {
            try {
                const config = JSON.parse(fs.readFileSync(configPath, 'utf-8'));
                if (config.specRepository) { return config.specRepository as string; }
            } catch { /* 파싱 실패 시 무시 */ }
        }
        return undefined;
    }

    /**
     * git URL에서 저장소 이름을 추출한다.
     * 예: "https://github.com/user/flow-spec.git" → "flow-spec"
     */
    private extractRepoName(gitUrl: string): string {
        let url = gitUrl.trimEnd();
        if (url.endsWith('/')) { url = url.slice(0, -1); }
        if (url.toLowerCase().endsWith('.git')) { url = url.slice(0, -4); }
        const lastSep = Math.max(url.lastIndexOf('/'), url.lastIndexOf('\\'), url.lastIndexOf(':'));
        const name = lastSep >= 0 ? url.slice(lastSep + 1) : url;
        return name || 'spec-repo';
    }

    /** 현재 사용 중인 스펙 디렉토리 경로 */
    get specsDirectory(): string {
        return this.specsDir;
    }

    private setupWatcher(): void {
        const pattern = new vscode.RelativePattern(this.specsDir, '*.json');
        this.watcher = vscode.workspace.createFileSystemWatcher(pattern);
        this.watcher.onDidChange(() => void this.reload());
        this.watcher.onDidCreate(() => void this.reload());
        this.watcher.onDidDelete(() => void this.reload());
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
                await this.load();
                this._onDidChange.fire();
            } while (this.reloadQueued);
        })();

        try {
            await this.reloadPromise;
        } finally {
            this.reloadPromise = null;
        }
    }

    /** docs/specs/ 하위 JSON 파일 목록 */
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
            this.specs = nextSpecs;
            this.graph = nextGraph;
            this.appliedRevision = revision;
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
        this.watcher?.dispose();
        this._onDidChange.dispose();
    }
}
