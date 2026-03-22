import { useEffect, useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  Clock, CheckCircle, ListChecks, GitBranch, MessageSquare, FileSearch,
  FileCode, Link2, BarChart3, Info, AlertTriangle, Shield, Tag,
  User, Calendar, Hash, CircleCheck, CircleX, CircleDot, CircleMinus,
  Pencil, Download, Save, X, ArrowUpRight, Bot, Layers3, Send, Sparkles,
} from 'lucide-react'
import { ApiError } from '@/api/client'
import { useSpec, useSpecs, useAssignments, useReviewRequests, useSubmitReviewResponse, useActivity, useEvidence, useUpdateSpec, useValidateSpec } from '@/hooks/useSpecs'
import { StateBadge, StatusBadge } from '@/components/StateBadge'
import { Cell } from '@/components/Cell'
import type { FlowState, TestDefinition, TestStatus, AcceptanceCriterion, RiskLevel, UpdateSpecRequest, Spec } from '@/types/flow'

interface EditableAcceptanceCriterion {
  id: string
  text: string
  testable: boolean
  notes: string
}

type ReviewDraftState = Record<string, { selectedOptionId?: string; comment: string; message?: string; error?: string }>
type ValidationOutcomeOption = { value: string; label: string; hint: string }

// State flow pipeline
const PIPELINE: FlowState[] = [
  'draft', 'queued', 'architectureReview', 'testGeneration',
  'implementation', 'review', 'active',
]

function PipelineView({ current }: { current: FlowState }) {
  const idx = PIPELINE.indexOf(current)
  return (
    <div className="flex items-center gap-0.5 overflow-x-auto py-2">
      {PIPELINE.map((s, i) => {
        const isCurrent = s === current
        const isPast = idx >= 0 && i < idx
        const bg = isCurrent
          ? 'bg-[var(--color-primary)] text-white shadow-[0_0_8px_rgba(59,130,246,0.4)]'
          : isPast
            ? 'bg-green-900/40 text-green-400'
            : 'bg-[var(--color-bg-elevated)] text-[var(--color-text-muted)]'
        return (
          <div key={s} className="flex items-center gap-0.5">
            {i > 0 && <div className={`w-3 h-0.5 ${isPast || isCurrent ? 'bg-green-700' : 'bg-[var(--color-border)]'}`} />}
            <div className={`px-2 py-1 rounded text-[10px] font-medium whitespace-nowrap ${bg}`}>
              {s === 'architectureReview' ? 'Arch' : s === 'testGeneration' ? 'TestGen' : s.charAt(0).toUpperCase() + s.slice(1)}
            </div>
          </div>
        )
      })}
      {(current === 'failed' || current === 'completed' || current === 'archived') && (
        <>
          <div className="w-3 h-0.5 bg-[var(--color-border)]" />
          <StateBadge state={current} />
        </>
      )}
    </div>
  )
}

function formatTime(iso?: string) {
  if (!iso) return '-'
  return new Date(iso).toLocaleString()
}

function relativeTime(iso?: string) {
  if (!iso) return ''
  const diff = Date.now() - new Date(iso).getTime()
  const mins = Math.floor(diff / 60000)
  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins}m ago`
  const hours = Math.floor(mins / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  return `${days}d ago`
}

const testStatusIcon: Record<TestStatus, typeof CircleCheck> = {
  passed: CircleCheck,
  failed: CircleX,
  notRun: CircleDot,
  skipped: CircleMinus,
}

const testStatusColor: Record<TestStatus, string> = {
  passed: 'text-green-400',
  failed: 'text-red-400',
  notRun: 'text-[var(--color-text-muted)]',
  skipped: 'text-yellow-400',
}

function createEditableCriteria(items: AcceptanceCriterion[] = []): EditableAcceptanceCriterion[] {
  return items.map((item) => ({
    id: item.id,
    text: item.text,
    testable: item.testable,
    notes: item.notes ?? '',
  }))
}

function renderStructuredText(value: string) {
  const lines = value.split(/\r?\n/)
  const blocks: Array<
    | { type: 'paragraph'; lines: string[] }
    | { type: 'ul'; items: string[] }
    | { type: 'ol'; items: string[] }
  > = []

  let index = 0
  while (index < lines.length) {
    const line = lines[index].trim()

    if (!line) {
      index++
      continue
    }

    if (/^-\s+/.test(line)) {
      const items: string[] = []
      while (index < lines.length) {
        const current = lines[index].trim()
        if (!/^-\s+/.test(current)) break
        items.push(current.replace(/^-\s+/, ''))
        index++
      }
      blocks.push({ type: 'ul', items })
      continue
    }

    if (/^\d+\.\s+/.test(line)) {
      const items: string[] = []
      while (index < lines.length) {
        const current = lines[index].trim()
        if (!/^\d+\.\s+/.test(current)) break
        items.push(current.replace(/^\d+\.\s+/, ''))
        index++
      }
      blocks.push({ type: 'ol', items })
      continue
    }

    const paragraphLines: string[] = []
    while (index < lines.length) {
      const current = lines[index].trim()
      if (!current || /^-\s+/.test(current) || /^\d+\.\s+/.test(current)) break
      paragraphLines.push(current)
      index++
    }
    blocks.push({ type: 'paragraph', lines: paragraphLines })
  }

  return (
    <div className="space-y-4">
      {blocks.map((block, blockIndex) => {
        if (block.type === 'paragraph') {
          return (
            <p key={`paragraph-${blockIndex}`} className="whitespace-pre-wrap text-sm leading-7 text-[var(--color-text)]">
              {block.lines.join(' ')}
            </p>
          )
        }

        if (block.type === 'ul') {
          return (
            <ul key={`ul-${blockIndex}`} className="space-y-2 rounded-xl border border-emerald-900/25 bg-emerald-950/12 px-4 py-3 text-sm text-[var(--color-text)]">
              {block.items.map((item, itemIndex) => (
                <li key={itemIndex} className="flex gap-3 leading-6">
                  <span className="mt-2 h-1.5 w-1.5 rounded-full bg-emerald-300/80" />
                  <span>{item}</span>
                </li>
              ))}
            </ul>
          )
        }

        return (
          <ol key={`ol-${blockIndex}`} className="space-y-2 rounded-xl border border-sky-900/25 bg-sky-950/12 px-4 py-3 text-sm text-[var(--color-text)]">
            {block.items.map((item, itemIndex) => (
              <li key={itemIndex} className="grid grid-cols-[auto_1fr] gap-3 leading-6">
                <span className="inline-flex h-6 min-w-6 items-center justify-center rounded-full border border-sky-800/40 bg-sky-900/35 px-1 text-[11px] font-semibold text-sky-200">
                  {itemIndex + 1}
                </span>
                <span>{item}</span>
              </li>
            ))}
          </ol>
        )
      })}
    </div>
  )
}

function DocumentSectionCell({
  title,
  value,
  draft,
  isEditMode,
  icon,
  accentColor,
  sectionId,
  defaultCollapsed = false,
  rows = 6,
  onChange,
}: {
  title: string
  value: string
  draft: string
  isEditMode: boolean
  icon: ReactNode
  accentColor: string
  sectionId: string
  defaultCollapsed?: boolean
  rows?: number
  onChange: (value: string) => void
}) {
  if (!isEditMode && !value) return null

  return (
    <Cell
      title={title}
      icon={icon}
      accentColor={accentColor}
      sectionId={sectionId}
      defaultCollapsed={defaultCollapsed}
    >
      {isEditMode ? (
        <textarea
          value={draft}
          onChange={(event) => onChange(event.target.value)}
          rows={rows}
          className="w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-input)] px-3 py-2 text-sm leading-relaxed text-[var(--color-text)] outline-none focus:border-[var(--color-primary)]"
        />
      ) : (
        <div className="rounded-xl border border-[var(--color-border-subtle)] bg-[var(--color-bg)]/45 px-4 py-3">
          {renderStructuredText(value)}
        </div>
      )}
    </Cell>
  )
}

function buildSpecMarkdown(spec: {
  id: string
  title: string
  state: string
  processingStatus: string
  type: string
  riskLevel: string
  version: number
  updatedAt: string
  problem?: string
  goal?: string
  context?: string
  nonGoals?: string
  implementationNotes?: string
  testPlan?: string
  dependencies?: { dependsOn: string[]; blocks: string[] }
  acceptanceCriteria: AcceptanceCriterion[]
  tests?: TestDefinition[]
}) {
  const sections: string[] = []

  sections.push(`# ${spec.title}`)
  sections.push('')
  sections.push(`- ID: ${spec.id}`)
  sections.push(`- State: ${spec.state}`)
  sections.push(`- Processing Status: ${spec.processingStatus}`)
  sections.push(`- Type: ${spec.type}`)
  sections.push(`- Risk: ${spec.riskLevel}`)
  sections.push(`- Version: ${spec.version}`)
  sections.push(`- Updated At: ${formatTime(spec.updatedAt)}`)

  if (spec.problem) {
    sections.push('')
    sections.push('## Problem')
    sections.push(spec.problem)
  }

  if (spec.goal) {
    sections.push('')
    sections.push('## Goal')
    sections.push(spec.goal)
  }

  if (spec.context) {
    sections.push('')
    sections.push('## Context')
    sections.push(spec.context)
  }

  if (spec.nonGoals) {
    sections.push('')
    sections.push('## Non-Goals')
    sections.push(spec.nonGoals)
  }

  if (spec.implementationNotes) {
    sections.push('')
    sections.push('## Implementation Notes')
    sections.push(spec.implementationNotes)
  }

  if (spec.testPlan) {
    sections.push('')
    sections.push('## Test Plan')
    sections.push(spec.testPlan)
  }

  if (spec.dependencies && (spec.dependencies.dependsOn.length > 0 || spec.dependencies.blocks.length > 0)) {
    sections.push('')
    sections.push('## Dependencies')
    if (spec.dependencies.dependsOn.length > 0) {
      sections.push('')
      sections.push('### Depends On')
      sections.push(...spec.dependencies.dependsOn.map((item) => `- ${item}`))
    }
    if (spec.dependencies.blocks.length > 0) {
      sections.push('')
      sections.push('### Blocks')
      sections.push(...spec.dependencies.blocks.map((item) => `- ${item}`))
    }
  }

  sections.push('')
  sections.push('## Acceptance Criteria')
  if (spec.acceptanceCriteria.length === 0) {
    sections.push('- None')
  } else {
    for (const item of spec.acceptanceCriteria) {
      sections.push(`- [${item.testable ? 'x' : ' '}] ${item.text}`)
      if (item.notes) {
        sections.push(`  - Notes: ${item.notes}`)
      }
    }
  }

  if (spec.tests && spec.tests.length > 0) {
    sections.push('')
    sections.push('## BDD Scenarios')
    for (const test of spec.tests) {
      sections.push(`- ${test.id} | ${test.type} | ${test.status}${test.title ? ` | ${test.title}` : ''}`)
    }
  }

  return sections.join('\n')
}

type DependencyRow = {
  id: string
  relation: 'dependsOn' | 'blocks'
  summary: string
  targetSpec?: Spec
}

type ScenarioStep = {
  keyword: 'Given' | 'When' | 'Then' | 'And' | 'But'
  text: string
}

const SCENARIO_KEYWORDS = ['Given', 'When', 'Then', 'And', 'But'] as const
const SCENARIO_PATTERN = new RegExp(`(${SCENARIO_KEYWORDS.join('|')})\\s+([^]*?)(?=(?:\\s+(?:${SCENARIO_KEYWORDS.join('|')})\\s+)|$)`, 'g')

function buildDependencyRows(
  dependencies: { dependsOn: string[]; blocks: string[] } | undefined,
  specIndex: Map<string, Spec>,
): DependencyRow[] {
  if (!dependencies) return []

  return [
    ...dependencies.dependsOn.map((id) => ({
      id,
      relation: 'dependsOn' as const,
      summary: 'Upstream prerequisite. This item should be ready before this spec can complete cleanly.',
      targetSpec: specIndex.get(id),
    })),
    ...dependencies.blocks.map((id) => ({
      id,
      relation: 'blocks' as const,
      summary: 'Downstream impact. Work mapped to this item is waiting on the current spec.',
      targetSpec: specIndex.get(id),
    })),
  ]
}

function parseScenarioSteps(title?: string): ScenarioStep[] {
  if (!title) return []

  const normalized = title.replace(/\r\n/g, '\n').trim()
  if (!normalized) return []

  return normalized
    .split('\n')
    .flatMap((line) => {
      const trimmed = line.trim()
      if (!trimmed) return []

      const matches = [...trimmed.matchAll(SCENARIO_PATTERN)]
      if (matches.length === 0) return []

      return matches
        .map((match) => {
          const keyword = match[1] as ScenarioStep['keyword']
          const text = match[2]?.trim()
          return text ? { keyword, text } : null
        })
        .filter((step): step is ScenarioStep => step !== null)
    })
}

function scenarioKeywordClass(keyword: ScenarioStep['keyword']) {
  switch (keyword) {
    case 'Given':
      return 'border-sky-900/40 bg-sky-950/30 text-sky-300'
    case 'When':
      return 'border-amber-900/40 bg-amber-950/30 text-amber-300'
    case 'Then':
      return 'border-emerald-900/40 bg-emerald-950/30 text-emerald-300'
    case 'And':
      return 'border-indigo-900/40 bg-indigo-950/30 text-indigo-300'
    case 'But':
      return 'border-rose-900/40 bg-rose-950/30 text-rose-300'
  }
}

function formatActionLabel(action: string) {
  return action
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[-_]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}

function activityTone(action: string) {
  const normalized = action.toLowerCase()

  if (normalized.includes('fail') || normalized.includes('error') || normalized.includes('reject')) {
    return {
      dotClass: 'bg-red-400 shadow-[0_0_0_4px_rgba(127,29,29,0.25)]',
      badgeClass: 'border-red-900/40 bg-red-950/30 text-red-300',
    }
  }

  if (normalized.includes('complete') || normalized.includes('done') || normalized.includes('pass') || normalized.includes('approve')) {
    return {
      dotClass: 'bg-emerald-400 shadow-[0_0_0_4px_rgba(6,78,59,0.25)]',
      badgeClass: 'border-emerald-900/40 bg-emerald-950/30 text-emerald-300',
    }
  }

  if (normalized.includes('review') || normalized.includes('question')) {
    return {
      dotClass: 'bg-amber-400 shadow-[0_0_0_4px_rgba(120,53,15,0.25)]',
      badgeClass: 'border-amber-900/40 bg-amber-950/30 text-amber-300',
    }
  }

  return {
    dotClass: 'bg-sky-400 shadow-[0_0_0_4px_rgba(12,74,110,0.25)]',
    badgeClass: 'border-sky-900/40 bg-sky-950/30 text-sky-300',
  }
}

function evidenceKindClass(kind: string) {
  const normalized = kind.toLowerCase()

  if (normalized.includes('test')) return 'border-emerald-900/40 bg-emerald-950/30 text-emerald-300'
  if (normalized.includes('log')) return 'border-amber-900/40 bg-amber-950/30 text-amber-300'
  if (normalized.includes('cover')) return 'border-sky-900/40 bg-sky-950/30 text-sky-300'
  if (normalized.includes('image') || normalized.includes('screenshot')) return 'border-fuchsia-900/40 bg-fuchsia-950/30 text-fuchsia-300'
  if (normalized.includes('doc')) return 'border-indigo-900/40 bg-indigo-950/30 text-indigo-300'
  return 'border-[var(--color-border)] bg-[var(--color-bg-elevated)] text-[var(--color-text-muted)]'
}

export function SpecDetailPage() {
  const { projectId, specId } = useParams<{ projectId: string; specId: string }>()
  const { data: spec, isLoading, refetch: refetchSpec } = useSpec(projectId!, specId!)
  const { data: projectSpecs } = useSpecs(projectId!)
  const { data: assignments } = useAssignments(projectId!, specId!)
  const { data: reviewRequests } = useReviewRequests(projectId!, specId!)
  const submitReviewResponse = useSubmitReviewResponse(projectId!, specId!)
  const { data: activity } = useActivity(projectId!, specId!)
  const { data: evidence } = useEvidence(projectId!, specId!)
  const updateSpec = useUpdateSpec(projectId!, specId!)
  const validateSpec = useValidateSpec(projectId!, specId!)
  const [isEditMode, setIsEditMode] = useState(false)
  const [titleDraft, setTitleDraft] = useState('')
  const [problemDraft, setProblemDraft] = useState('')
  const [goalDraft, setGoalDraft] = useState('')
  const [contextDraft, setContextDraft] = useState('')
  const [nonGoalsDraft, setNonGoalsDraft] = useState('')
  const [implementationNotesDraft, setImplementationNotesDraft] = useState('')
  const [testPlanDraft, setTestPlanDraft] = useState('')
  const [riskLevelDraft, setRiskLevelDraft] = useState<RiskLevel>('low')
  const [acceptanceCriteriaDraft, setAcceptanceCriteriaDraft] = useState<EditableAcceptanceCriterion[]>([])
  const [saveMessage, setSaveMessage] = useState<string | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [validateMessage, setValidateMessage] = useState<string | null>(null)
  const [validateError, setValidateError] = useState<string | null>(null)
  const [validationOutcome, setValidationOutcome] = useState<string>('pass')
  const [reviewDrafts, setReviewDrafts] = useState<ReviewDraftState>({})

  useEffect(() => {
    if (!spec || isEditMode) return
    setTitleDraft(spec.title)
    setProblemDraft(spec.problem ?? '')
    setGoalDraft(spec.goal ?? '')
    setContextDraft(spec.context ?? '')
    setNonGoalsDraft(spec.nonGoals ?? '')
    setImplementationNotesDraft(spec.implementationNotes ?? '')
    setTestPlanDraft(spec.testPlan ?? '')
    setRiskLevelDraft(spec.riskLevel)
    setAcceptanceCriteriaDraft(createEditableCriteria(spec.acceptanceCriteria))
  }, [spec, isEditMode])

  useEffect(() => {
    if (!spec) return

    if (spec.state === 'draft') {
      setValidationOutcome('pass')
      return
    }

    if (spec.state === 'review' && spec.processingStatus === 'inReview') {
      setValidationOutcome('pass')
    }
  }, [spec?.id, spec?.state, spec?.processingStatus])

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="w-6 h-6 border-2 border-[var(--color-primary)] border-t-transparent rounded-full animate-spin" />
      </div>
    )
  }
  if (!spec) return <p className="text-[var(--color-danger)]">Spec not found</p>

  const resetDrafts = () => {
    setTitleDraft(spec.title)
    setProblemDraft(spec.problem ?? '')
    setGoalDraft(spec.goal ?? '')
    setContextDraft(spec.context ?? '')
    setNonGoalsDraft(spec.nonGoals ?? '')
    setImplementationNotesDraft(spec.implementationNotes ?? '')
    setTestPlanDraft(spec.testPlan ?? '')
    setRiskLevelDraft(spec.riskLevel)
    setAcceptanceCriteriaDraft(createEditableCriteria(spec.acceptanceCriteria))
  }

  const handleExportMarkdown = () => {
    const markdown = buildSpecMarkdown(spec)
    const blob = new Blob([markdown], { type: 'text/markdown;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `${spec.id}-${spec.title.replace(/[^a-zA-Z0-9-_]+/g, '-').replace(/-+/g, '-').replace(/^-|-$/g, '') || 'spec'}.md`
    link.click()
    URL.revokeObjectURL(url)
  }

  const handleSave = async () => {
    setSaveMessage(null)
    setSaveError(null)
    setValidateMessage(null)
    setValidateError(null)

    const payload: UpdateSpecRequest = {
      version: spec.version,
      title: titleDraft.trim(),
      problem: problemDraft.trim() || undefined,
      goal: goalDraft.trim() || undefined,
      context: contextDraft.trim() || undefined,
      nonGoals: nonGoalsDraft.trim() || undefined,
      implementationNotes: implementationNotesDraft.trim() || undefined,
      testPlan: testPlanDraft.trim() || undefined,
      riskLevel: riskLevelDraft,
      acceptanceCriteria: acceptanceCriteriaDraft
        .filter((item) => item.text.trim().length > 0)
        .map((item) => ({
          text: item.text.trim(),
          testable: item.testable,
          notes: item.notes.trim() || undefined,
        })),
    }

    if (!payload.title) {
      setSaveError('Title is required.')
      return
    }

    try {
      await updateSpec.mutateAsync(payload)
      setSaveMessage('Saved successfully.')
      setIsEditMode(false)
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        await refetchSpec()
        setSaveError('Version conflict detected. Latest spec data was reloaded; re-open edit mode and retry.')
        setIsEditMode(false)
        return
      }

      setSaveError(error instanceof Error ? error.message : 'Failed to save changes.')
    }
  }

  const updateCriterion = (id: string, updater: (item: EditableAcceptanceCriterion) => EditableAcceptanceCriterion) => {
    setAcceptanceCriteriaDraft((items) => items.map((item) => item.id === id ? updater(item) : item))
  }

  const canValidate = spec.state === 'draft' || (spec.state === 'review' && spec.processingStatus === 'inReview')
  const validateLabel = spec.state === 'draft' ? 'Run Precheck' : 'Validate Spec'
  const validationOptions: ValidationOutcomeOption[] = spec.state === 'draft'
    ? [
        { value: 'pass', label: 'Pass', hint: 'Move draft to queued when AC precheck is clean.' },
        { value: 'reject', label: 'Reject', hint: 'Keep draft in place and request planner follow-up.' },
      ]
    : [
        { value: 'pass', label: 'Pass', hint: 'Promote reviewed work to active.' },
        { value: 'rework', label: 'Rework', hint: 'Send the spec back to implementation.' },
        { value: 'userReview', label: 'User Review', hint: 'Pause for explicit user input.' },
        { value: 'fail', label: 'Fail', hint: 'Mark validation as terminally failed.' },
      ]
  const selectedValidationOption = validationOptions.find((option) => option.value === validationOutcome) ?? validationOptions[0]

  const handleValidate = async () => {
    setValidateMessage(null)
    setValidateError(null)
    setSaveMessage(null)
    setSaveError(null)

    try {
      const result = await validateSpec.mutateAsync({ version: spec.version, outcome: validationOutcome })
      const eventName = result.eventName.replace(/([a-z0-9])([A-Z])/g, '$1 $2')
      setValidateMessage(`${eventName} accepted.`)
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        await refetchSpec()
        setValidateError('Version conflict detected. Latest spec data was reloaded; retry validation on the refreshed document.')
        return
      }

      setValidateError(error instanceof Error ? error.message : 'Validation failed.')
    }
  }

  const addCriterion = () => {
    setAcceptanceCriteriaDraft((items) => [
      ...items,
      {
        id: `draft-${items.length + 1}`,
        text: '',
        testable: true,
        notes: '',
      },
    ])
  }

  const removeCriterion = (id: string) => {
    setAcceptanceCriteriaDraft((items) => items.filter((item) => item.id !== id))
  }

  const updateReviewDraft = (rrId: string, updater: (draft: ReviewDraftState[string]) => ReviewDraftState[string]) => {
    setReviewDrafts((current) => {
      const existing = current[rrId] ?? { comment: '' }
      return {
        ...current,
        [rrId]: updater(existing),
      }
    })
  }

  const handleReviewSubmit = async (rrId: string, hasOptions: boolean) => {
    const draft = reviewDrafts[rrId] ?? { comment: '' }
    const trimmedComment = draft.comment.trim()

    if (hasOptions && !draft.selectedOptionId) {
      updateReviewDraft(rrId, (current) => ({ ...current, error: 'Select an option before submitting.', message: undefined }))
      return
    }

    if (!hasOptions && !trimmedComment) {
      updateReviewDraft(rrId, (current) => ({ ...current, error: 'Enter a comment before submitting.', message: undefined }))
      return
    }

    try {
      await submitReviewResponse.mutateAsync({
        rrId,
        type: hasOptions ? 'approve' : 'reject',
        selectedOptionId: hasOptions ? draft.selectedOptionId : undefined,
        comment: trimmedComment || undefined,
      })

      setReviewDrafts((current) => ({
        ...current,
        [rrId]: {
          selectedOptionId: hasOptions ? draft.selectedOptionId : undefined,
          comment: '',
          message: 'Response submitted.',
          error: undefined,
        },
      }))
    } catch (error) {
      updateReviewDraft(rrId, (current) => ({
        ...current,
        message: undefined,
        error: error instanceof Error ? error.message : 'Failed to submit review response.',
      }))
    }
  }

  const tests = spec.tests ?? []
  const deps = spec.dependencies
  const hasDeps = deps && (deps.dependsOn?.length > 0 || deps.blocks?.length > 0)
  const specIndex = new Map((projectSpecs ?? []).map((item) => [item.id, item]))
  const dependencyRows = buildDependencyRows(deps, specIndex)
  const evidenceCount = evidence?.reduce((sum, manifest) => sum + manifest.refs.length, 0) ?? 0
  const latestEvidence = evidence?.[0]

  // Calculate completion stats
  const totalAssignments = assignments?.length ?? 0
  const completedAssignments = assignments?.filter(a => a.status === 'completed').length ?? 0
  const progress = totalAssignments > 0 ? Math.round((completedAssignments / totalAssignments) * 100) : 0

  const hasDocumentSections = Boolean(
    spec.problem
    || spec.goal
    || spec.context
    || spec.nonGoals
    || spec.implementationNotes
    || spec.testPlan
    || isEditMode,
  )

  // Count tests linked to each AC
  const acTestCounts = new Map<string, { total: number; passed: number }>()
  for (const t of tests) {
    for (const acId of t.acIds) {
      const entry = acTestCounts.get(acId) ?? { total: 0, passed: 0 }
      entry.total++
      if (t.status === 'passed') entry.passed++
      acTestCounts.set(acId, entry)
    }
  }

  return (
    <div className="max-w-4xl mx-auto space-y-3">
      <section className="rounded-2xl border border-[var(--color-border)] bg-[linear-gradient(135deg,rgba(20,29,47,0.95),rgba(11,19,34,0.98))] px-5 py-4 shadow-[0_18px_60px_rgba(0,0,0,0.22)]">
        <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
          <div className="space-y-2 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <span className="text-[10px] font-mono uppercase tracking-[0.22em] text-[var(--color-text-muted)]">Spec Document</span>
              <span className="rounded-full border border-[var(--color-border)] bg-[var(--color-bg)]/70 px-2 py-0.5 text-[10px] font-mono text-[var(--color-text-muted)]">{spec.id}</span>
            </div>
            <div className="flex items-center gap-3 flex-wrap">
              <h1 className="text-2xl font-semibold text-[var(--color-text-bright)]">{spec.title}</h1>
              <StateBadge state={spec.state as FlowState} />
              <StatusBadge status={spec.processingStatus} />
            </div>
            <p className="text-sm text-[var(--color-text-muted)]">
              Last updated {formatTime(spec.updatedAt)}
            </p>

            <PipelineView current={spec.state as FlowState} />

            <div className="flex flex-wrap gap-2">
              <MetaChip icon={<Tag className="w-3 h-3" />} label={spec.type} />
              <MetaChip
                icon={<AlertTriangle className="w-3 h-3" />}
                label={spec.riskLevel}
                className={spec.riskLevel === 'critical' ? 'text-red-400 border-red-900/50' : spec.riskLevel === 'high' ? 'text-orange-400 border-orange-900/50' : ''}
              />
              <MetaChip icon={<Hash className="w-3 h-3" />} label={`v${spec.version}`} />
              <MetaChip icon={<Calendar className="w-3 h-3" />} label={formatTime(spec.createdAt)} />
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2 md:justify-end">
            {isEditMode ? (
              <>
                <button
                  onClick={handleSave}
                  disabled={updateSpec.isPending}
                  className="inline-flex items-center gap-2 rounded-lg bg-[var(--color-primary)] px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-[var(--color-primary-hover)] disabled:opacity-50"
                >
                  <Save className="w-4 h-4" />
                  {updateSpec.isPending ? 'Saving...' : 'Save'}
                </button>
                <button
                  onClick={() => {
                    resetDrafts()
                    setSaveError(null)
                    setSaveMessage(null)
                    setIsEditMode(false)
                  }}
                  disabled={updateSpec.isPending}
                  className="inline-flex items-center gap-2 rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-card)] px-3 py-2 text-sm font-medium text-[var(--color-text)] transition-colors hover:bg-[var(--color-bg-card-hover)] disabled:opacity-50"
                >
                  <X className="w-4 h-4" />
                  Cancel
                </button>
              </>
            ) : (
              <button
                onClick={() => {
                  resetDrafts()
                  setSaveError(null)
                  setSaveMessage(null)
                  setValidateError(null)
                  setValidateMessage(null)
                  setIsEditMode(true)
                }}
                className="inline-flex items-center gap-2 rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-card)] px-3 py-2 text-sm font-medium text-[var(--color-text)] transition-colors hover:bg-[var(--color-bg-card-hover)]"
              >
                <Pencil className="w-4 h-4" />
                Edit Document
              </button>
            )}

            {canValidate && !isEditMode && (
              <div className="flex flex-col gap-2 rounded-xl border border-emerald-900/30 bg-emerald-950/15 p-2">
                <div className="flex items-center gap-2">
                  <Sparkles className="h-4 w-4 text-emerald-300" />
                  <span className="text-[10px] font-semibold uppercase tracking-[0.18em] text-emerald-300">Validation</span>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <select
                    value={validationOutcome}
                    onChange={(event) => setValidationOutcome(event.target.value)}
                    disabled={validateSpec.isPending}
                    className="min-w-36 rounded-lg border border-emerald-900/40 bg-[var(--color-bg-input)] px-3 py-2 text-sm text-[var(--color-text)] outline-none focus:border-[var(--color-primary)] disabled:opacity-50"
                  >
                    {validationOptions.map((option) => (
                      <option key={option.value} value={option.value}>{option.label}</option>
                    ))}
                  </select>
                  <button
                    onClick={handleValidate}
                    disabled={validateSpec.isPending}
                    className="inline-flex items-center gap-2 rounded-lg border border-emerald-800/50 bg-emerald-950/30 px-3 py-2 text-sm font-medium text-emerald-300 transition-colors hover:bg-emerald-950/50 disabled:opacity-50"
                  >
                    <Sparkles className="w-4 h-4" />
                    {validateSpec.isPending ? 'Validating...' : validateLabel}
                  </button>
                </div>
                <p className="max-w-72 text-xs leading-relaxed text-[var(--color-text-muted)]">
                  {selectedValidationOption.hint}
                </p>
              </div>
            )}

            <button
              onClick={handleExportMarkdown}
              className="inline-flex items-center gap-2 rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-card)] px-3 py-2 text-sm font-medium text-[var(--color-text)] transition-colors hover:bg-[var(--color-bg-card-hover)]"
            >
              <Download className="w-4 h-4" />
              Export Markdown
            </button>
          </div>
        </div>

        {(saveMessage || saveError || validateMessage || validateError) && (
          <div className={`mt-4 rounded-xl border px-3 py-2 text-sm ${
            (saveError || validateError)
              ? 'border-red-900/40 bg-red-950/20 text-red-300'
              : 'border-green-900/40 bg-green-950/20 text-green-300'
          }`}>
            {saveError ?? validateError ?? saveMessage ?? validateMessage}
          </div>
        )}

        {isEditMode && (
          <div className="mt-4 grid gap-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-bg)]/40 p-4 md:grid-cols-[1fr_auto] md:items-end">
            <label className="grid gap-1.5 text-sm">
              <span className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--color-text-muted)]">Title</span>
              <input
                value={titleDraft}
                onChange={(event) => setTitleDraft(event.target.value)}
                className="w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-input)] px-3 py-2 text-sm text-[var(--color-text)] outline-none focus:border-[var(--color-primary)]"
              />
            </label>

            <label className="grid gap-1.5 text-sm md:min-w-48">
              <span className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--color-text-muted)]">Risk Level</span>
              <select
                value={riskLevelDraft}
                onChange={(event) => setRiskLevelDraft(event.target.value as RiskLevel)}
                className="rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-input)] px-3 py-2 text-sm text-[var(--color-text)] outline-none focus:border-[var(--color-primary)]"
              >
                <option value="low">low</option>
                <option value="medium">medium</option>
                <option value="high">high</option>
                <option value="critical">critical</option>
              </select>
            </label>
          </div>
        )}
      </section>

      <Cell title="Document Summary" icon={<Info className="w-4 h-4" />} accentColor="var(--cell-overview)" sectionId="section-document-summary">
        <div className="rounded-xl border border-[var(--color-border-subtle)] bg-[linear-gradient(135deg,rgba(18,34,58,0.92),rgba(11,18,30,0.96))] px-4 py-3">
          <p className="max-w-3xl text-sm leading-7 text-[var(--color-text)]">
            {spec.goal ?? spec.problem ?? 'No document summary available yet.'}
          </p>
        </div>
      </Cell>

      {hasDocumentSections && (
        <div className="space-y-3">
          <DocumentSectionCell
            title="Problem"
            value={spec.problem ?? ''}
            draft={problemDraft}
            isEditMode={isEditMode}
            icon={<AlertTriangle className="w-4 h-4" />}
            accentColor="var(--cell-overview)"
            sectionId="section-problem"
            onChange={setProblemDraft}
          />
          <DocumentSectionCell
            title="Goal"
            value={spec.goal ?? ''}
            draft={goalDraft}
            isEditMode={isEditMode}
            icon={<CircleCheck className="w-4 h-4" />}
            accentColor="var(--cell-overview)"
            sectionId="section-goal"
            onChange={setGoalDraft}
          />
          <DocumentSectionCell
            title="Context"
            value={spec.context ?? ''}
            draft={contextDraft}
            isEditMode={isEditMode}
            icon={<Layers3 className="w-4 h-4" />}
            accentColor="var(--cell-dependencies)"
            sectionId="section-context"
            defaultCollapsed
            onChange={setContextDraft}
          />
          <DocumentSectionCell
            title="Non-Goals"
            value={spec.nonGoals ?? ''}
            draft={nonGoalsDraft}
            isEditMode={isEditMode}
            icon={<Shield className="w-4 h-4" />}
            accentColor="var(--cell-review)"
            sectionId="section-non-goals"
            defaultCollapsed
            onChange={setNonGoalsDraft}
          />
          <DocumentSectionCell
            title="Implementation Notes"
            value={spec.implementationNotes ?? ''}
            draft={implementationNotesDraft}
            isEditMode={isEditMode}
            icon={<FileCode className="w-4 h-4" />}
            accentColor="var(--cell-implementation)"
            sectionId="section-implementation-notes"
            defaultCollapsed
            rows={8}
            onChange={setImplementationNotesDraft}
          />
          <DocumentSectionCell
            title="Test Plan"
            value={spec.testPlan ?? ''}
            draft={testPlanDraft}
            isEditMode={isEditMode}
            icon={<FileSearch className="w-4 h-4" />}
            accentColor="var(--cell-evidence)"
            sectionId="section-test-plan"
            defaultCollapsed
            rows={8}
            onChange={setTestPlanDraft}
          />
        </div>
      )}

      {/* ── Dependencies Cell ── */}
      {hasDeps && (
        <Cell title="Dependencies" icon={<Link2 className="w-4 h-4" />} accentColor="var(--cell-dependencies)" sectionId="section-dependencies">
          <div className="space-y-4">
            <div className="grid gap-3 md:grid-cols-3">
              <div className="rounded-xl border border-sky-900/30 bg-sky-950/20 p-3">
                <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-sky-300">Upstream</p>
                <p className="mt-2 text-2xl font-semibold text-[var(--color-text-bright)]">{deps.dependsOn.length}</p>
                <p className="mt-1 text-xs text-[var(--color-text-muted)]">Specs this document needs before delivery is truly unblocked.</p>
              </div>
              <div className="rounded-xl border border-rose-900/30 bg-rose-950/20 p-3">
                <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-rose-300">Downstream</p>
                <p className="mt-2 text-2xl font-semibold text-[var(--color-text-bright)]">{deps.blocks.length}</p>
                <p className="mt-1 text-xs text-[var(--color-text-muted)]">Specs waiting on this item to move forward.</p>
              </div>
              <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-bg)]/50 p-3">
                <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--color-text-muted)]">Readiness</p>
                <p className="mt-2 text-sm font-medium text-[var(--color-text-bright)]">
                  {deps.dependsOn.length > 0 ? 'Blocked by upstream work' : 'No upstream blockers recorded'}
                </p>
                <p className="mt-1 text-xs text-[var(--color-text-muted)]">
                  {deps.blocks.length > 0 ? 'This spec also acts as a prerequisite for downstream items.' : 'No downstream dependency pressure recorded yet.'}
                </p>
              </div>
            </div>

            <div className="overflow-hidden rounded-xl border border-[var(--color-border-subtle)] bg-[var(--color-bg)]/40">
              <div className="grid grid-cols-[120px_1fr_2fr] gap-3 border-b border-[var(--color-border-subtle)] px-3 py-2 text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--color-text-muted)]">
                <span>Relation</span>
                <span>Spec</span>
                <span>Impact</span>
              </div>
              <div className="divide-y divide-[var(--color-border-subtle)]">
                {dependencyRows.map((row) => (
                  <div key={`${row.relation}-${row.id}`} className="grid grid-cols-[120px_1fr_2fr] gap-3 px-3 py-3 text-sm">
                    <div>
                      <span className={`inline-flex items-center gap-1 rounded-full border px-2 py-1 text-[10px] font-semibold uppercase tracking-[0.12em] ${row.relation === 'dependsOn' ? 'border-sky-900/40 bg-sky-950/30 text-sky-300' : 'border-rose-900/40 bg-rose-950/30 text-rose-300'}`}>
                        {row.relation === 'dependsOn' ? <Link2 className="h-3 w-3" /> : <Shield className="h-3 w-3" />}
                        {row.relation === 'dependsOn' ? 'Depends On' : 'Blocks'}
                      </span>
                    </div>
                    <div className="min-w-0 space-y-1">
                      {row.targetSpec ? (
                        <Link
                          to={`/projects/${projectId}/specs/${row.id}`}
                          className="inline-flex items-center gap-1 text-xs font-semibold text-[var(--color-text-bright)] transition-colors hover:text-[var(--color-primary)]"
                        >
                          <span className="font-mono">{row.id}</span>
                          <ArrowUpRight className="h-3 w-3" />
                        </Link>
                      ) : (
                        <span className="font-mono text-xs text-[var(--color-text-bright)]">{row.id}</span>
                      )}
                      <p className="truncate text-xs text-[var(--color-text-muted)]">
                        {row.targetSpec?.title ?? 'Spec details unavailable in current project index.'}
                      </p>
                    </div>
                    <div className="space-y-1">
                      <p className="text-xs leading-relaxed text-[var(--color-text-muted)]">{row.summary}</p>
                      {row.targetSpec ? (
                        <div className="flex flex-wrap items-center gap-2">
                          <StateBadge state={row.targetSpec.state as FlowState} />
                          <StatusBadge status={row.targetSpec.processingStatus} />
                          <span className="text-[10px] text-[var(--color-text-muted)]">v{row.targetSpec.version}</span>
                        </div>
                      ) : (
                        <span className="text-[10px] text-yellow-400">Referenced spec metadata not found in loaded project list.</span>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            </div>

            <div className="grid gap-3 md:grid-cols-2">
              {deps.dependsOn?.length > 0 && (
                <div>
                  <h4 className="mb-2 text-[10px] font-semibold uppercase text-[var(--color-text-muted)]">Depends On</h4>
                  <div className="flex flex-wrap gap-2">
                    {deps.dependsOn.map((d: string) => (
                      <Link
                        key={d}
                        to={`/projects/${projectId}/specs/${d}`}
                        className="inline-flex items-center gap-1 rounded-md border border-sky-900/30 bg-sky-950/20 px-2 py-1 text-xs font-mono text-sky-300 transition-colors hover:border-sky-700/50 hover:bg-sky-950/40"
                      >
                        <Link2 className="w-3 h-3" /> {d}
                      </Link>
                    ))}
                  </div>
                </div>
              )}
              {deps.blocks?.length > 0 && (
                <div>
                  <h4 className="mb-2 text-[10px] font-semibold uppercase text-[var(--color-text-muted)]">Blocks</h4>
                  <div className="flex flex-wrap gap-2">
                    {deps.blocks.map((d: string) => (
                      <Link
                        key={d}
                        to={`/projects/${projectId}/specs/${d}`}
                        className="inline-flex items-center gap-1 rounded-md border border-rose-900/30 bg-rose-950/20 px-2 py-1 text-xs font-mono text-rose-300 transition-colors hover:border-rose-700/50 hover:bg-rose-950/40"
                      >
                        <Shield className="w-3 h-3" /> {d}
                      </Link>
                    ))}
                  </div>
                </div>
              )}
            </div>
          </div>
        </Cell>
      )}

      {/* ── Acceptance Criteria Cell ── */}
      {(spec.acceptanceCriteria?.length > 0 || isEditMode) && (
        <Cell
          title="Acceptance Criteria"
          icon={<ListChecks className="w-4 h-4" />}
          accentColor="var(--cell-ac)"
          sectionId="section-acceptance-criteria"
          badge={<span className="text-[10px] text-[var(--color-text-muted)] bg-[var(--color-bg-elevated)] px-1.5 py-0.5 rounded">{isEditMode ? acceptanceCriteriaDraft.length : spec.acceptanceCriteria.length}</span>}
        >
          <div className="space-y-2">
            {isEditMode && (
              <div className="flex items-center justify-between rounded-md border border-dashed border-[var(--color-border)] bg-[var(--color-bg)]/40 px-3 py-2">
                <div>
                  <p className="text-sm font-medium text-[var(--color-text)]">Editable acceptance criteria</p>
                  <p className="text-xs text-[var(--color-text-muted)]">Add, remove, and update AC text, notes, and testability.</p>
                </div>
                <button
                  onClick={addCriterion}
                  className="rounded-md bg-[var(--color-primary)] px-3 py-1.5 text-xs font-medium text-white transition-colors hover:bg-[var(--color-primary-hover)]"
                >
                  Add AC
                </button>
              </div>
            )}

            {(isEditMode ? acceptanceCriteriaDraft : spec.acceptanceCriteria).map((ac) => {
              const testInfo = acTestCounts.get(ac.id)
              return (
                <div key={ac.id} className="rounded-md p-2 transition-colors hover:bg-[var(--color-bg)]/40">
                  {isEditMode ? (
                    <div className="grid gap-3 rounded-lg border border-[var(--color-border-subtle)] bg-[var(--color-bg)]/50 p-3">
                      <div className="flex items-start gap-3">
                        <label className="mt-1 inline-flex items-center gap-2 text-xs text-[var(--color-text-muted)]">
                          <input
                            type="checkbox"
                            checked={ac.testable}
                            onChange={(event) => updateCriterion(ac.id, (item) => ({ ...item, testable: event.target.checked }))}
                            className="accent-[var(--color-primary)]"
                          />
                          Testable
                        </label>
                        <textarea
                          value={ac.text}
                          onChange={(event) => updateCriterion(ac.id, (item) => ({ ...item, text: event.target.value }))}
                          rows={3}
                          placeholder="Acceptance criteria text"
                          className="flex-1 rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-input)] px-3 py-2 text-sm text-[var(--color-text)] outline-none focus:border-[var(--color-primary)]"
                        />
                        <button
                          onClick={() => removeCriterion(ac.id)}
                          className="rounded-md border border-[var(--color-border)] px-2 py-1 text-xs text-[var(--color-text-muted)] transition-colors hover:bg-[var(--color-bg-card-hover)] hover:text-[var(--color-text)]"
                        >
                          Remove
                        </button>
                      </div>
                      <textarea
                        value={ac.notes}
                        onChange={(event) => updateCriterion(ac.id, (item) => ({ ...item, notes: event.target.value }))}
                        rows={2}
                        placeholder="Notes (optional)"
                        className="rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-input)] px-3 py-2 text-sm text-[var(--color-text)] outline-none focus:border-[var(--color-primary)]"
                      />
                    </div>
                  ) : (
                    <div className="flex items-start gap-3">
                      <CheckCircle className={`w-4 h-4 mt-0.5 flex-shrink-0 ${ac.testable ? 'text-green-500' : 'text-[var(--color-text-muted)]'}`} />
                      <div className="flex-1 min-w-0">
                        <p className="text-sm">{ac.text}</p>
                        {ac.notes && <p className="text-xs text-[var(--color-text-muted)] mt-0.5">{ac.notes}</p>}
                      </div>
                      {testInfo && (
                        <span className={`text-[10px] px-1.5 py-0.5 rounded-full flex-shrink-0 ${
                          testInfo.passed === testInfo.total
                            ? 'bg-green-900/30 text-green-400'
                            : 'bg-yellow-900/30 text-yellow-400'
                        }`}>
                          {testInfo.passed}/{testInfo.total} tests
                        </span>
                      )}
                    </div>
                  )}
                </div>
              )
            })}

            {isEditMode && acceptanceCriteriaDraft.length === 0 && (
              <p className="rounded-md border border-dashed border-[var(--color-border)] px-3 py-4 text-sm text-[var(--color-text-muted)]">
                No acceptance criteria yet. Use "Add AC" to create the first item.
              </p>
            )}
          </div>
        </Cell>
      )}

      {/* ── BDD Scenarios Cell ── */}
      {tests.length > 0 && (
        <Cell
          title="BDD Scenarios"
          icon={<FileCode className="w-4 h-4" />}
          accentColor="var(--cell-bdd)"
          sectionId="section-bdd-scenarios"
          badge={<span className="text-[10px] text-[var(--color-text-muted)] bg-[var(--color-bg-elevated)] px-1.5 py-0.5 rounded">{tests.length}</span>}
        >
          <div className="space-y-2">
            {tests.map((test: TestDefinition) => {
              const StatusIcon = testStatusIcon[test.status] ?? CircleDot
              const color = testStatusColor[test.status] ?? 'text-[var(--color-text-muted)]'
              const scenarioSteps = parseScenarioSteps(test.title)
              return (
                <div key={test.id} className="p-2.5 rounded-md bg-[var(--color-bg)]/60 border border-[var(--color-border-subtle)]">
                  <div className="flex items-center gap-2 mb-1">
                    <StatusIcon className={`w-3.5 h-3.5 ${color}`} />
                    <span className="text-xs font-mono text-[var(--color-text-muted)]">{test.id}</span>
                    <span className={`text-[10px] px-1.5 py-0.5 rounded uppercase font-medium ${color}`}>{test.status}</span>
                    <span className="text-[10px] text-[var(--color-text-muted)] ml-auto">{test.type}</span>
                  </div>
                  {test.title && (
                    scenarioSteps.length > 0 ? (
                      <div className="space-y-1.5 pl-5.5">
                        {scenarioSteps.map((step, index) => (
                          <div key={`${test.id}-${step.keyword}-${index}`} className="flex items-start gap-2 rounded-lg border border-[var(--color-border-subtle)] bg-[var(--color-bg)]/45 px-2.5 py-2">
                            <span className={`inline-flex min-w-14 justify-center rounded-full border px-2 py-0.5 text-[10px] font-semibold uppercase tracking-[0.12em] ${scenarioKeywordClass(step.keyword)}`}>
                              {step.keyword}
                            </span>
                            <p className="text-sm leading-relaxed text-[var(--color-text)]">{step.text}</p>
                          </div>
                        ))}
                      </div>
                    ) : (
                      <p className="text-sm font-mono pl-5.5 leading-relaxed">{test.title}</p>
                    )
                  )}
                  {test.acIds?.length > 0 && (
                    <div className="flex gap-1 mt-1.5 pl-5.5">
                      {test.acIds.map(acId => (
                        <span key={acId} className="text-[10px] px-1.5 py-0.5 rounded bg-green-900/20 text-green-400 font-mono">
                          {acId}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        </Cell>
      )}

      {/* ── Implementation Status Cell ── */}
      {assignments && assignments.length > 0 && (
        <Cell
          title="Implementation Status"
          icon={<BarChart3 className="w-4 h-4" />}
          accentColor="var(--cell-status)"
          sectionId="section-implementation-status"
          badge={<span className="text-[10px] text-[var(--color-text-muted)] bg-[var(--color-bg-elevated)] px-1.5 py-0.5 rounded">{progress}%</span>}
        >
          <div className="space-y-3">
            {/* Progress bar */}
            <div>
              <div className="flex items-center justify-between text-xs text-[var(--color-text-muted)] mb-1.5">
                <span>{completedAssignments}/{totalAssignments} assignments completed</span>
                <span>{progress}%</span>
              </div>
              <div className="h-1.5 rounded-full bg-[var(--color-bg-elevated)] overflow-hidden">
                <div
                  className="h-full rounded-full bg-gradient-to-r from-[var(--color-primary)] to-green-500 transition-all duration-500"
                  style={{ width: `${progress}%` }}
                />
              </div>
            </div>

            {/* Assignment table */}
            <div className="space-y-1">
              {assignments.map((a) => (
                <div key={a.id} className="flex items-center gap-3 text-xs p-2 rounded-md bg-[var(--color-bg)]/40 hover:bg-[var(--color-bg)]/60 transition-colors">
                  <User className="w-3 h-3 text-[var(--color-text-muted)]" />
                  <span className="text-[var(--color-text-muted)]">{a.agentRole}</span>
                  <span className="text-[var(--color-text)]">{a.type}</span>
                  {a.worktree?.branch && (
                    <span className="text-[10px] font-mono text-purple-400">
                      <GitBranch className="w-3 h-3 inline mr-0.5" />{a.worktree.branch}
                    </span>
                  )}
                  <span className={`ml-auto text-[10px] px-2 py-0.5 rounded-full font-medium ${
                    a.status === 'completed' ? 'bg-green-900/30 text-green-400' :
                    a.status === 'running' ? 'bg-blue-900/30 text-blue-400' :
                    a.status === 'failed' ? 'bg-red-900/30 text-red-400' :
                    a.status === 'cancelled' ? 'bg-yellow-900/30 text-yellow-400' :
                    'bg-[var(--color-bg-elevated)] text-[var(--color-text-muted)]'
                  }`}>
                    {a.status}
                  </span>
                </div>
              ))}
            </div>
          </div>
        </Cell>
      )}

      {/* ── Review Requests Cell ── */}
      {reviewRequests && reviewRequests.length > 0 && (
        <Cell
          title="Review Requests"
          icon={<MessageSquare className="w-4 h-4" />}
          accentColor="var(--cell-review)"
          sectionId="section-review-requests"
          badge={
            reviewRequests.some(r => r.status === 'open')
              ? <span className="text-[10px] px-1.5 py-0.5 rounded-full bg-yellow-900/30 text-yellow-400 font-medium">
                  {reviewRequests.filter(r => r.status === 'open').length} open
                </span>
              : undefined
          }
        >
          <div className="space-y-2">
            {reviewRequests.map((rr) => (
              <div key={rr.id} className="p-3 rounded-md bg-[var(--color-bg)]/60 border border-[var(--color-border-subtle)]">
                <div className="flex items-center gap-2 mb-1.5">
                  <span className="font-mono text-[10px] text-[var(--color-text-muted)]">{rr.id}</span>
                  <span className={`text-[10px] px-1.5 py-0.5 rounded-full font-medium ${
                    rr.status === 'open' ? 'bg-yellow-900/30 text-yellow-400' :
                    rr.status === 'answered' ? 'bg-green-900/30 text-green-400' :
                    'bg-[var(--color-bg-elevated)] text-[var(--color-text-muted)]'
                  }`}>{rr.status}</span>
                  {rr.createdAt && <span className="text-[10px] text-[var(--color-text-muted)] ml-auto">{relativeTime(rr.createdAt)}</span>}
                </div>
                {rr.reason && <p className="text-xs text-[var(--color-text-muted)] mb-1">{rr.reason}</p>}
                {rr.summary && <p className="text-sm">{rr.summary}</p>}
                {rr.questions && rr.questions.length > 0 && (
                  <div className="mt-2 space-y-1">
                    {rr.questions.map((q, i) => (
                      <p key={i} className="text-xs text-[var(--color-info)] pl-2 border-l-2 border-[var(--color-info)]/30">{q}</p>
                    ))}
                  </div>
                )}
                {rr.options && rr.options.length > 0 && (
                  <div className="mt-2 flex flex-wrap gap-1.5">
                    {rr.options.map(opt => (
                      <span key={opt.id} className={`text-xs px-2 py-1 rounded-md border ${
                        rr.response?.selectedOptionId === opt.id
                          ? 'bg-[var(--color-primary)]/10 border-[var(--color-primary)]/30 text-[var(--color-primary)]'
                          : 'border-[var(--color-border)] text-[var(--color-text-muted)]'
                      }`}>
                        {opt.label}
                      </span>
                    ))}
                  </div>
                )}
                {rr.response && (
                  <div className="mt-2 p-2 rounded bg-green-900/10 border border-green-800/20 text-xs">
                    <span className="text-green-400 font-medium">{rr.response.type}</span>
                    {rr.response.comment && <p className="text-[var(--color-text-muted)] mt-1">{rr.response.comment}</p>}
                  </div>
                )}

                {rr.status === 'open' && !rr.response && (() => {
                  const draft = reviewDrafts[rr.id] ?? { comment: '' }
                  const hasOptions = (rr.options?.length ?? 0) > 0
                  const isSubmitting = submitReviewResponse.isPending

                  return (
                    <div className="mt-3 rounded-xl border border-[var(--color-border)] bg-[var(--color-bg)]/40 p-3 space-y-3">
                      <div>
                        <p className="text-sm font-medium text-[var(--color-text)]">Respond to review</p>
                        <p className="text-xs text-[var(--color-text-muted)]">
                          {hasOptions ? 'Select the best option and optionally leave context for the reviewer.' : 'This request expects a written comment explaining what should change.'}
                        </p>
                      </div>

                      {hasOptions ? (
                        <div className="grid gap-2">
                          {rr.options?.map((opt) => (
                            <label
                              key={opt.id}
                              className={`flex cursor-pointer items-start gap-3 rounded-lg border px-3 py-2 transition-colors ${draft.selectedOptionId === opt.id ? 'border-[var(--color-primary)] bg-[var(--color-primary)]/10' : 'border-[var(--color-border-subtle)] bg-[var(--color-bg)]/40 hover:bg-[var(--color-bg)]/60'}`}
                            >
                              <input
                                type="radio"
                                name={`review-${rr.id}`}
                                checked={draft.selectedOptionId === opt.id}
                                onChange={() => updateReviewDraft(rr.id, (current) => ({ ...current, selectedOptionId: opt.id, error: undefined, message: undefined }))}
                                className="mt-1 accent-[var(--color-primary)]"
                              />
                              <span className="min-w-0">
                                <span className="block text-sm text-[var(--color-text)]">{opt.label}</span>
                                {opt.description && <span className="mt-0.5 block text-xs text-[var(--color-text-muted)]">{opt.description}</span>}
                              </span>
                            </label>
                          ))}
                        </div>
                      ) : null}

                      <label className="grid gap-1.5">
                        <span className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--color-text-muted)]">Comment</span>
                        <textarea
                          value={draft.comment}
                          onChange={(event) => updateReviewDraft(rr.id, (current) => ({ ...current, comment: event.target.value, error: undefined, message: undefined }))}
                          rows={hasOptions ? 3 : 4}
                          placeholder={hasOptions ? 'Optional context for the selected option' : 'Explain the requested change or rejection reason'}
                          className="w-full rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-input)] px-3 py-2 text-sm leading-relaxed text-[var(--color-text)] outline-none focus:border-[var(--color-primary)]"
                        />
                      </label>

                      {(draft.error || draft.message) && (
                        <div className={`rounded-lg border px-3 py-2 text-xs ${draft.error ? 'border-red-900/40 bg-red-950/20 text-red-300' : 'border-green-900/40 bg-green-950/20 text-green-300'}`}>
                          {draft.error ?? draft.message}
                        </div>
                      )}

                      <div className="flex justify-end">
                        <button
                          onClick={() => handleReviewSubmit(rr.id, hasOptions)}
                          disabled={isSubmitting}
                          className="inline-flex items-center gap-2 rounded-lg bg-[var(--color-primary)] px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-[var(--color-primary-hover)] disabled:opacity-50"
                        >
                          <Send className="h-4 w-4" />
                          {isSubmitting ? 'Submitting...' : 'Submit response'}
                        </button>
                      </div>
                    </div>
                  )
                })()}
              </div>
            ))}
          </div>
        </Cell>
      )}

      {/* ── Evidence Cell ── */}
      {evidence && evidence.length > 0 && (
        <Cell title="Evidence" icon={<FileSearch className="w-4 h-4" />} accentColor="var(--cell-evidence)" sectionId="section-evidence" defaultCollapsed>
          <div className="space-y-4">
            <div className="grid gap-3 md:grid-cols-3">
              <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-bg)]/50 p-3">
                <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--color-text-muted)]">Runs</p>
                <p className="mt-2 text-2xl font-semibold text-[var(--color-text-bright)]">{evidence.length}</p>
                <p className="mt-1 text-xs text-[var(--color-text-muted)]">Persisted evidence manifests associated with this spec.</p>
              </div>
              <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-bg)]/50 p-3">
                <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--color-text-muted)]">Artifacts</p>
                <p className="mt-2 text-2xl font-semibold text-[var(--color-text-bright)]">{evidenceCount}</p>
                <p className="mt-1 text-xs text-[var(--color-text-muted)]">Individual files or structured outputs captured across runs.</p>
              </div>
              <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-bg)]/50 p-3">
                <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--color-text-muted)]">Latest Run</p>
                <p className="mt-2 text-sm font-semibold text-[var(--color-text-bright)]">{latestEvidence?.runId ?? '-'}</p>
                <p className="mt-1 text-xs text-[var(--color-text-muted)]">{latestEvidence ? `${relativeTime(latestEvidence.createdAt)} • ${formatTime(latestEvidence.createdAt)}` : 'No evidence timestamps available.'}</p>
              </div>
            </div>

            {evidence.map((m) => (
              <div key={m.runId} className="rounded-xl border border-[var(--color-border-subtle)] bg-[var(--color-bg)]/60 p-3">
                <div className="flex flex-wrap items-start gap-3 justify-between">
                  <div className="space-y-1">
                    <div className="flex items-center gap-2">
                      <span className="font-mono text-[11px] text-[var(--color-text-bright)]">{m.runId}</span>
                      <span className="rounded-full border border-[var(--color-border)] bg-[var(--color-bg-elevated)] px-2 py-0.5 text-[10px] text-[var(--color-text-muted)]">{m.refs.length} refs</span>
                    </div>
                    <p className="text-xs text-[var(--color-text-muted)]">Captured {relativeTime(m.createdAt)} • {formatTime(m.createdAt)}</p>
                  </div>
                  <div className="flex flex-wrap gap-1.5">
                    {[...new Set(m.refs.map((ref) => ref.kind))].map((kind) => (
                      <span key={`${m.runId}-${kind}`} className={`rounded-full border px-2 py-0.5 text-[10px] font-medium ${evidenceKindClass(kind)}`}>
                        {kind}
                      </span>
                    ))}
                  </div>
                </div>
                <div className="mt-3 grid gap-2">
                  {m.refs.map((r, i) => (
                    <div key={i} className="rounded-lg border border-[var(--color-border-subtle)] bg-[var(--color-bg)]/55 px-3 py-2">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className={`rounded-full border px-2 py-0.5 text-[10px] font-medium ${evidenceKindClass(r.kind)}`}>{r.kind}</span>
                        <span className="font-mono text-xs text-[var(--color-text-bright)] break-all">{r.relativePath}</span>
                      </div>
                      {r.summary && <p className="mt-1 text-xs leading-relaxed text-[var(--color-text-muted)]">{r.summary}</p>}
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </Cell>
      )}

      {/* ── Activity Cell ── */}
      {activity && activity.length > 0 && (
        <Cell title="Activity" icon={<Clock className="w-4 h-4" />} accentColor="var(--cell-activity)" sectionId="section-activity" defaultCollapsed>
          <div className="space-y-4">
            <div className="grid gap-3 md:grid-cols-3">
              <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-bg)]/50 p-3">
                <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--color-text-muted)]">Recent Events</p>
                <p className="mt-2 text-2xl font-semibold text-[var(--color-text-bright)]">{activity.length}</p>
                <p className="mt-1 text-xs text-[var(--color-text-muted)]">Latest transitions and side effects loaded for this spec.</p>
              </div>
              <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-bg)]/50 p-3">
                <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--color-text-muted)]">Latest Actor</p>
                <p className="mt-2 text-sm font-semibold text-[var(--color-text-bright)]">{activity[0]?.actor ?? '-'}</p>
                <p className="mt-1 text-xs text-[var(--color-text-muted)]">{activity[0]?.sourceType ? `Source: ${activity[0].sourceType}` : 'Source metadata unavailable.'}</p>
              </div>
              <div className="rounded-xl border border-[var(--color-border)] bg-[var(--color-bg)]/50 p-3">
                <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-[var(--color-text-muted)]">Latest Event</p>
                <p className="mt-2 text-sm font-semibold text-[var(--color-text-bright)]">{activity[0] ? formatActionLabel(activity[0].action) : '-'}</p>
                <p className="mt-1 text-xs text-[var(--color-text-muted)]">{activity[0] ? `${relativeTime(activity[0].timestamp)} • ${formatTime(activity[0].timestamp)}` : 'No activity available.'}</p>
              </div>
            </div>

            <div className="space-y-0">
            {activity.map((evt) => (
              <div key={evt.eventId} className="grid grid-cols-[20px_1fr] gap-3 border-b border-[var(--color-border-subtle)] py-3 last:border-0">
                <div className="flex flex-col items-center">
                  <span className={`mt-1 h-2.5 w-2.5 rounded-full ${activityTone(evt.action).dotClass}`} />
                  <span className="mt-1 h-full w-px bg-[var(--color-border-subtle)] last:hidden" />
                </div>
                <div className="space-y-2">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className={`rounded-full border px-2 py-0.5 text-[10px] font-semibold uppercase tracking-[0.12em] ${activityTone(evt.action).badgeClass}`}>
                      {formatActionLabel(evt.action)}
                    </span>
                    <span className="text-[10px] text-[var(--color-text-muted)]">{relativeTime(evt.timestamp)}</span>
                    <span className="text-[10px] text-[var(--color-text-muted)]">{formatTime(evt.timestamp)}</span>
                  </div>

                  <p className="text-sm leading-relaxed text-[var(--color-text)]">{evt.message}</p>

                  <div className="flex flex-wrap items-center gap-2 text-[10px] text-[var(--color-text-muted)]">
                    <span className="inline-flex items-center gap-1 rounded-full border border-[var(--color-border)] bg-[var(--color-bg-elevated)] px-2 py-0.5">
                      <Bot className="h-3 w-3" />
                      {evt.actor}
                    </span>
                    {evt.sourceType && (
                      <span className="inline-flex items-center gap-1 rounded-full border border-[var(--color-border)] bg-[var(--color-bg-elevated)] px-2 py-0.5">
                        <Layers3 className="h-3 w-3" />
                        {evt.sourceType}
                      </span>
                    )}
                    {typeof evt.baseVersion === 'number' && (
                      <span className="rounded-full border border-[var(--color-border)] bg-[var(--color-bg-elevated)] px-2 py-0.5">base v{evt.baseVersion}</span>
                    )}
                    {evt.assignmentId && (
                      <span className="rounded-full border border-[var(--color-border)] bg-[var(--color-bg-elevated)] px-2 py-0.5 font-mono">assignment {evt.assignmentId}</span>
                    )}
                    {evt.reviewRequestId && (
                      <span className="rounded-full border border-[var(--color-border)] bg-[var(--color-bg-elevated)] px-2 py-0.5 font-mono">review {evt.reviewRequestId}</span>
                    )}
                    {evt.correlationId && (
                      <span className="rounded-full border border-[var(--color-border)] bg-[var(--color-bg-elevated)] px-2 py-0.5 font-mono">run {evt.correlationId}</span>
                    )}
                  </div>

                  <div className="flex flex-wrap items-center gap-2">
                    <StateBadge state={evt.state as FlowState} />
                    <StatusBadge status={evt.processingStatus} />
                  </div>
                </div>
              </div>
            ))}
            </div>
          </div>
        </Cell>
      )}
    </div>
  )
}

function MetaChip({ icon, label, className = '' }: { icon: React.ReactNode; label: string; className?: string }) {
  return (
    <span className={`inline-flex items-center gap-1 px-2 py-1 rounded-md border border-[var(--color-border)] text-xs text-[var(--color-text-muted)] ${className}`}>
      {icon} {label}
    </span>
  )
}
