/**
 * SpecLoader - docs/specs/*.json 파일을 읽어 그래프 데이터를 구성
 */
import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { Spec, Condition, GraphNode, GraphEdge, SpecGraph, SpecStatus } from './types';

export class SpecLoader {
    private specsDir: string;
    private specs: Spec[] = [];
    private graph: SpecGraph | null = null;

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
        // 1. 로컬 docs/specs/ 에 스펙 파일이 있으면 그것을 우선 사용
        const localSpecsDir = path.join(workspaceRoot, 'docs', 'specs');
        if (fs.existsSync(localSpecsDir)) {
            const jsonFiles = fs.readdirSync(localSpecsDir).filter(f => f.endsWith('.json'));
            if (jsonFiles.length > 0) {
                return localSpecsDir;
            }
        }

        // 2. specRepository가 설정된 경우, runner와 동일한 .flow/spec-cache/specs/ 경로 확인
        // runner가 git pull로 동기화하는 위치와 같은 경로를 사용해야 칸반 변경사항이 runner에 반영됨
        const specRepository = this.readSpecRepository(workspaceRoot);
        if (specRepository) {
            const specCacheSpecsDir = path.join(workspaceRoot, '.flow', 'spec-cache', 'specs');
            if (fs.existsSync(specCacheSpecsDir)) {
                return specCacheSpecsDir;
            }

            // 3. 폴백: 사용자 홈의 .flow/specs/{repoName}/
            const repoName = this.extractRepoName(specRepository);
            const userHome = process.env.HOME
                || process.env.USERPROFILE
                || require('os').homedir();
            const repoDir = path.join(userHome, '.flow', 'specs', repoName);
            if (fs.existsSync(repoDir)) {
                // 리포 내 specs/ 서브디렉토리 우선, 없으면 리포 루트 사용
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
        this.watcher.onDidChange(() => this.reload());
        this.watcher.onDidCreate(() => this.reload());
        this.watcher.onDidDelete(() => this.reload());
    }

    /** 스펙 파일 로드 및 그래프 빌드 */
    async load(): Promise<SpecGraph> {
        this.specs = [];
        const files = await this.findSpecFiles();

        for (const file of files) {
            try {
                const content = fs.readFileSync(file, 'utf-8');
                const spec = JSON.parse(content) as Spec;
                if (spec.id && spec.nodeType === 'feature') {
                    this.specs.push(spec);
                }
            } catch (e) {
                // JSON 파싱 실패 시 skip + warning
                vscode.window.showWarningMessage(
                    `스펙 파일 파싱 실패: ${path.basename(file)} - ${e}`
                );
            }
        }

        this.graph = this.buildGraph();
        return this.graph;
    }

    /** 캐시된 그래프 반환 또는 새로 로드 */
    async getGraph(): Promise<SpecGraph> {
        if (!this.graph) {
            return this.load();
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
            for (const c of spec.conditions) {
                statuses.add(c.status);
            }
        }
        return Array.from(statuses);
    }

    /** 새로고침 */
    async reload(): Promise<void> {
        await this.load();
        this._onDidChange.fire();
    }

    /** docs/specs/ 하위 JSON 파일 목록 */
    private async findSpecFiles(): Promise<string[]> {
        if (!fs.existsSync(this.specsDir)) {
            return [];
        }
        const entries = fs.readdirSync(this.specsDir);
        return entries
            .filter(e => e.endsWith('.json') && !e.startsWith('.'))
            .map(e => path.join(this.specsDir, e));
    }

    /** Spec 배열로부터 그래프 데이터 구성 */
    private buildGraph(): SpecGraph {
        const nodes: GraphNode[] = [];
        const edges: GraphEdge[] = [];

        for (const spec of this.specs) {
            // Feature 노드
            nodes.push({
                id: spec.id,
                nodeType: 'feature',
                label: spec.title,
                description: spec.description,
                status: spec.status,
                parent: spec.parent,
                tags: spec.tags,
                codeRefs: spec.codeRefs,
                evidence: spec.evidence,
                githubRefs: spec.githubRefs,
                docLinks: spec.docLinks,
            });

            // Parent 엣지
            if (spec.parent) {
                edges.push({
                    source: spec.parent,
                    target: spec.id,
                    type: 'parent',
                });
            }

            // Dependency 엣지
            for (const dep of spec.dependencies) {
                edges.push({
                    source: dep,
                    target: spec.id,
                    type: 'dependency',
                });
            }

            // Condition 노드 + 엣지
            for (const cond of spec.conditions) {
                nodes.push({
                    id: cond.id,
                    nodeType: 'condition',
                    label: cond.id,
                    description: cond.description,
                    status: cond.status,
                    parent: spec.id,
                    tags: [],
                    codeRefs: cond.codeRefs,
                    evidence: cond.evidence,
                    githubRefs: cond.githubRefs,
                    docLinks: cond.docLinks,
                    featureId: spec.id,
                });

                edges.push({
                    source: spec.id,
                    target: cond.id,
                    type: 'condition',
                });
            }
        }

        // 순환 참조 감지 (Kahn's algorithm)
        this.detectCycles(nodes, edges);

        return { nodes, edges, specs: this.specs };
    }

    /** Kahn 알고리즘으로 순환 참조 감지 */
    private detectCycles(nodes: GraphNode[], edges: GraphEdge[]): void {
        // dependency 엣지만으로 순환 검사
        const depEdges = edges.filter(e => e.type === 'dependency');
        const featureIds = new Set(nodes.filter(n => n.nodeType === 'feature').map(n => n.id));

        const inDegree = new Map<string, number>();
        const adj = new Map<string, string[]>();

        for (const id of featureIds) {
            inDegree.set(id, 0);
            adj.set(id, []);
        }

        for (const edge of depEdges) {
            if (featureIds.has(edge.source) && featureIds.has(edge.target)) {
                adj.get(edge.source)!.push(edge.target);
                inDegree.set(edge.target, (inDegree.get(edge.target) || 0) + 1);
            }
        }

        const queue: string[] = [];
        for (const [id, deg] of inDegree) {
            if (deg === 0) {
                queue.push(id);
            }
        }

        let processed = 0;
        while (queue.length > 0) {
            const node = queue.shift()!;
            processed++;
            for (const neighbor of adj.get(node) || []) {
                const newDeg = (inDegree.get(neighbor) || 1) - 1;
                inDegree.set(neighbor, newDeg);
                if (newDeg === 0) {
                    queue.push(neighbor);
                }
            }
        }

        if (processed < featureIds.size) {
            const cycleNodes = Array.from(inDegree.entries())
                .filter(([, deg]) => deg > 0)
                .map(([id]) => id);
            vscode.window.showErrorMessage(
                `순환 참조 감지: ${cycleNodes.join(', ')}`
            );
        }
    }

    dispose(): void {
        this.watcher?.dispose();
        this._onDidChange.dispose();
    }
}
