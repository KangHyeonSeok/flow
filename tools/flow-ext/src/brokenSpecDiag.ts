/**
 * brokenSpecDiag - 손상 스펙 JSON 진단 캐시 관리 (F-025)
 *
 * 파싱 불가 스펙 파일의 진단 정보를 .flow/spec-cache/broken-spec-diag.json 에 기록.
 */
import * as fs from 'fs';
import * as path from 'path';

export interface BrokenSpecDiagRecord {
    specId: string;
    filePath: string;
    errorMessage: string;
    line: number | null;
    column: number | null;
    detectedAt: string;
    fileMtime: string | null;
    status: 'unresolved' | 'resolved' | 'escalated';
    resolvedAt: string | null;
    lastCheckedAt: string | null;
    failReason: string | null;
    repairAttempts: number;
}

export interface BrokenSpecDiagCache {
    version: number;
    records: BrokenSpecDiagRecord[];
}

/** .flow/spec-cache/broken-spec-diag.json 경로 반환 */
export function getDiagCachePath(workspaceRoot: string): string {
    return path.join(workspaceRoot, '.flow', 'spec-cache', 'broken-spec-diag.json');
}

/** 진단 캐시 로드. 없으면 빈 캐시 반환. */
export function loadDiagCache(workspaceRoot: string): BrokenSpecDiagCache {
    const cachePath = getDiagCachePath(workspaceRoot);
    try {
        if (fs.existsSync(cachePath)) {
            const raw = JSON.parse(fs.readFileSync(cachePath, 'utf-8')) as Record<string, unknown>;
            // C# runner는 PascalCase로 저장하므로 양쪽 모두 지원
            const rawRecords = (raw['records'] ?? raw['Records'] ?? []) as unknown[];
            const records: BrokenSpecDiagRecord[] = rawRecords.map((r) => {
                const rec = r as Record<string, unknown>;
                return {
                    specId: (rec['specId'] ?? rec['SpecId'] ?? '') as string,
                    filePath: (rec['filePath'] ?? rec['FilePath'] ?? '') as string,
                    errorMessage: (rec['errorMessage'] ?? rec['ErrorMessage'] ?? '') as string,
                    line: (rec['line'] ?? rec['Line'] ?? null) as number | null,
                    column: (rec['column'] ?? rec['Column'] ?? null) as number | null,
                    detectedAt: (rec['detectedAt'] ?? rec['DetectedAt'] ?? '') as string,
                    fileMtime: (rec['fileMtime'] ?? rec['FileMtime'] ?? null) as string | null,
                    status: (rec['status'] ?? rec['Status'] ?? 'unresolved') as BrokenSpecDiagRecord['status'],
                    resolvedAt: (rec['resolvedAt'] ?? rec['ResolvedAt'] ?? null) as string | null,
                    lastCheckedAt: (rec['lastCheckedAt'] ?? rec['LastCheckedAt'] ?? null) as string | null,
                    failReason: (rec['failReason'] ?? rec['FailReason'] ?? null) as string | null,
                    repairAttempts: (rec['repairAttempts'] ?? rec['RepairAttempts'] ?? 0) as number,
                };
            });
            return { version: (raw['version'] ?? raw['Version'] ?? 1) as number, records };
        }
    } catch { /* ignore read/parse errors */ }
    return { version: 1, records: [] };
}

/** 진단 캐시 저장 */
export function saveDiagCache(workspaceRoot: string, cache: BrokenSpecDiagCache): void {
    const cachePath = getDiagCachePath(workspaceRoot);
    try {
        const dir = path.dirname(cachePath);
        if (!fs.existsSync(dir)) { fs.mkdirSync(dir, { recursive: true }); }
        fs.writeFileSync(cachePath, JSON.stringify(cache, null, 2), 'utf-8');
    } catch { /* ignore write errors */ }
}

/**
 * SyntaxError 메시지와 파일 내용에서 line/column 추출 시도
 * Node.js JSON.parse 오류 형식: "Unexpected token ... at position N"
 */
function extractLineCol(content: string, errorMsg: string): { line: number | null; column: number | null } {
    const posMatch = errorMsg.match(/at position (\d+)/i);
    if (posMatch) {
        const pos = parseInt(posMatch[1], 10);
        if (!isNaN(pos) && pos >= 0 && pos <= content.length) {
            const before = content.substring(0, pos);
            const lines = before.split('\n');
            return { line: lines.length, column: lines[lines.length - 1].length + 1 };
        }
    }
    // 다른 형식 시도: "line N column M"
    const lcMatch = errorMsg.match(/line\s+(\d+)\s+column\s+(\d+)/i);
    if (lcMatch) {
        return { line: parseInt(lcMatch[1], 10), column: parseInt(lcMatch[2], 10) };
    }
    return { line: null, column: null };
}

/**
 * 손상 스펙 레코드를 진단 캐시에 기록/갱신.
 * 이미 레코드가 있으면 갱신, 없으면 새로 추가.
 */
export function recordBrokenSpec(
    workspaceRoot: string,
    filePath: string,
    error: unknown,
    fileContent?: string
): void {
    const specId = path.basename(filePath, '.json');
    const now = new Date().toISOString();
    const errorMessage = String(error);

    let line: number | null = null;
    let column: number | null = null;
    if (fileContent) {
        const extracted = extractLineCol(fileContent, errorMessage);
        line = extracted.line;
        column = extracted.column;
    }

    let fileMtime: string | null = null;
    try {
        const stat = fs.statSync(filePath);
        fileMtime = stat.mtime.toISOString();
    } catch { /* ignore */ }

    const cache = loadDiagCache(workspaceRoot);
    const existing = cache.records.find(r => r.filePath === filePath);

    if (existing) {
        existing.errorMessage = errorMessage;
        existing.line = line;
        existing.column = column;
        existing.detectedAt = now;
        existing.fileMtime = fileMtime;
        existing.status = 'unresolved';
        existing.lastCheckedAt = now;
    } else {
        cache.records.push({
            specId,
            filePath,
            errorMessage,
            line,
            column,
            detectedAt: now,
            fileMtime,
            status: 'unresolved',
            resolvedAt: null,
            lastCheckedAt: now,
            failReason: null,
            repairAttempts: 0,
        });
    }

    saveDiagCache(workspaceRoot, cache);
}

/**
 * 이전에 손상된 스펙이 복구되었을 때 resolved로 마킹.
 * stale 레코드(unresolved였다가 이제 정상)를 정리.
 */
export function markSpecResolved(workspaceRoot: string, filePath: string): void {
    const cache = loadDiagCache(workspaceRoot);
    const record = cache.records.find(r => r.filePath === filePath && r.status === 'unresolved');
    if (record) {
        record.status = 'resolved';
        record.resolvedAt = new Date().toISOString();
        record.lastCheckedAt = new Date().toISOString();
        saveDiagCache(workspaceRoot, cache);
    }
}

/** unresolved 진단 레코드 목록 반환 */
export function getUnresolvedRecords(workspaceRoot: string): BrokenSpecDiagRecord[] {
    return loadDiagCache(workspaceRoot).records.filter(r => r.status === 'unresolved');
}
