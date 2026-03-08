import * as fs from 'fs';
import * as path from 'path';
import { getUserFeedbackState, UserQuestion } from './reviewState';

type JsonRecord = Record<string, unknown>;

export async function saveQuestionAnswer(specsDirectory: string, specId: string, question: UserQuestion, answer: string): Promise<void> {
    const trimmedAnswer = answer.trim();
    if (!trimmedAnswer) {
        throw new Error('빈 답변은 저장할 수 없습니다.');
    }

    const filePath = path.join(specsDirectory, `${specId}.json`);
    const raw = fs.readFileSync(filePath, 'utf-8');
    const spec = JSON.parse(raw) as JsonRecord;
    const metadata = ensureRecord(spec, 'metadata');
    const answeredAt = new Date().toISOString();

    const metadataQuestions = ensureArray(metadata, 'questions');
    const reviewMetadata = getOptionalRecord(metadata.review);
    const reviewQuestions = reviewMetadata ? ensureArray(reviewMetadata, 'questions') : null;
    const additionalInformationRequests = reviewMetadata && Array.isArray(reviewMetadata.additionalInformationRequests)
        ? reviewMetadata.additionalInformationRequests
        : null;

    let matchedAny = false;

    matchedAny = updateQuestionEntries(metadataQuestions, question, trimmedAnswer, answeredAt) || matchedAny;

    if (reviewQuestions) {
        matchedAny = updateQuestionEntries(reviewQuestions, question, trimmedAnswer, answeredAt) || matchedAny;
    }

    if (additionalInformationRequests) {
        const remaining = additionalInformationRequests.filter((entry) => {
            if (typeof entry !== 'string') {
                return true;
            }

            return !matchesQuestion(entry, question);
        });

        if (remaining.length !== additionalInformationRequests.length) {
            reviewMetadata!.additionalInformationRequests = remaining;
            matchedAny = true;
        }
    }

    upsertAnsweredQuestion(metadataQuestions, question, trimmedAnswer, answeredAt);

    if (!matchedAny && metadataQuestions.length === 0) {
        throw new Error(`질문 '${question.id || question.question}'을(를) 찾을 수 없습니다.`);
    }

    metadata.lastAnsweredAt = answeredAt;

    const nextFeedback = getUserFeedbackState({ metadata });
    const stillNeedsInput = nextFeedback.openQuestionCount > 0;

    metadata.reviewDisposition = stillNeedsInput ? 'needs-user-decision' : 'retry-queued';
    metadata.plannerState = stillNeedsInput ? 'waiting-user-input' : 'standby';
    delete metadata.requiresUserInput;

    if (stillNeedsInput) {
        metadata.questionStatus = 'waiting-user-input';
    } else {
        delete metadata.questionStatus;
    }

    if (reviewMetadata) {
        delete reviewMetadata.requiresUserInput;
    }

    spec.updatedAt = answeredAt;
    fs.writeFileSync(filePath, `${JSON.stringify(spec, null, 2)}\n`, 'utf-8');
}

function updateQuestionEntries(entries: unknown[], question: UserQuestion, answer: string, answeredAt: string): boolean {
    let updated = false;

    for (const entry of entries) {
        if (!isRecord(entry)) {
            continue;
        }

        if (!matchesQuestion(entry, question)) {
            continue;
        }

        entry.status = 'answered';
        entry.answer = answer;
        entry.answeredAt = answeredAt;

        if (question.id && typeof entry.id !== 'string') {
            entry.id = question.id;
        }

        if (question.type && typeof entry.type !== 'string') {
            entry.type = question.type;
        }

        if (question.why && typeof entry.why !== 'string') {
            entry.why = question.why;
        }

        updated = true;
    }

    return updated;
}

function upsertAnsweredQuestion(entries: unknown[], question: UserQuestion, answer: string, answeredAt: string): void {
    for (const entry of entries) {
        if (!isRecord(entry) || !matchesQuestion(entry, question)) {
            continue;
        }

        entry.status = 'answered';
        entry.answer = answer;
        entry.answeredAt = answeredAt;
        return;
    }

    const nextEntry: JsonRecord = {
        id: question.id,
        type: question.type,
        question: question.question,
        why: question.why,
        status: 'answered',
        answer,
        answeredAt,
    };

    if (question.answerSuggestions && question.answerSuggestions.length > 0) {
        nextEntry.options = question.answerSuggestions;
    }

    entries.push(stripUndefined(nextEntry));
}

function matchesQuestion(entry: unknown, question: UserQuestion): boolean {
    if (typeof entry === 'string') {
        return normalizeText(entry) === normalizeText(question.question)
            || (!!question.id && normalizeText(entry) === normalizeText(question.id));
    }

    if (!isRecord(entry)) {
        return false;
    }

    const entryId = typeof entry.id === 'string' ? entry.id : '';
    const entryQuestion = typeof entry.question === 'string'
        ? entry.question
        : typeof entry.text === 'string'
            ? entry.text
            : '';

    if (question.id && entryId && normalizeText(entryId) === normalizeText(question.id)) {
        return true;
    }

    return !!entryQuestion && normalizeText(entryQuestion) === normalizeText(question.question);
}

function ensureRecord(holder: JsonRecord, key: string): JsonRecord {
    const current = holder[key];
    if (isRecord(current)) {
        return current;
    }

    const next: JsonRecord = {};
    holder[key] = next;
    return next;
}

function getOptionalRecord(value: unknown): JsonRecord | null {
    return isRecord(value) ? value : null;
}

function ensureArray(holder: JsonRecord, key: string): unknown[] {
    const current = holder[key];
    if (Array.isArray(current)) {
        return current;
    }

    const next: unknown[] = [];
    holder[key] = next;
    return next;
}

function stripUndefined(record: JsonRecord): JsonRecord {
    return Object.fromEntries(Object.entries(record).filter(([, value]) => value !== undefined));
}

function normalizeText(value: string): string {
    return value.trim().replace(/\s+/g, ' ').toLowerCase();
}

function isRecord(value: unknown): value is JsonRecord {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}