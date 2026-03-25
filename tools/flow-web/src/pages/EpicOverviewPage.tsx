import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  AlertTriangle,
  FileText,
  ListChecks,
  Milestone,
  Link2,
  Circle,
  Pencil,
  Save,
  Loader2,
} from 'lucide-react'
import { useEpicView, useEpicDocument, useUpdateEpicDocument } from '@/hooks/useSpecs'
import { Cell } from '@/components/Cell'
import { ListEditor } from '@/components/ListEditor'
import type { EpicChildSpec, EpicView, FlowState } from '@/types/flow'

const stateColor: Record<string, string> = {
  active: 'text-green-400',
  completed: 'text-green-400',
  implementation: 'text-yellow-400',
  testGeneration: 'text-yellow-400',
  review: 'text-yellow-400',
  architectureReview: 'text-yellow-400',
  queued: 'text-blue-400',
  draft: 'text-[var(--color-text-muted)]',
  failed: 'text-red-400',
  archived: 'text-[var(--color-text-muted)]',
}

const stateLabel: Record<string, string> = {
  active: 'Active',
  completed: 'Completed',
  implementation: 'Implementation',
  testGeneration: 'Test Gen',
  review: 'Review',
  architectureReview: 'Arch Review',
  queued: 'Queued',
  draft: 'Draft',
  failed: 'Failed',
  archived: 'Archived',
}

const riskBadge: Record<string, { bg: string; text: string }> = {
  high: { bg: 'bg-red-900/40', text: 'text-red-400' },
  medium: { bg: 'bg-yellow-900/40', text: 'text-yellow-400' },
  low: { bg: 'bg-green-900/40', text: 'text-green-400' },
}

function ProgressHeader({ view }: { view: EpicView }) {
  const { progress } = view
  const pct = Math.round(progress.completionRatio * 100)

  return (
    <div className="mb-6">
      {/* Title row */}
      <div className="flex items-start gap-3 mb-2">
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 mb-1">
            <span className="text-xs font-mono text-[var(--color-text-muted)]">{view.epicId}</span>
            {view.priority && (
              <span className={`text-xs px-1.5 py-0.5 rounded ${
                view.priority === 'high' ? 'bg-red-900/40 text-red-400' :
                view.priority === 'medium' ? 'bg-yellow-900/40 text-yellow-400' :
                'bg-gray-700 text-gray-400'
              }`}>
                {view.priority}
              </span>
            )}
          </div>
          <h1 className="text-xl font-bold text-[var(--color-text-bright)]">{view.title}</h1>
          {view.summary && (
            <p className="text-sm text-[var(--color-text-muted)] mt-1">{view.summary}</p>
          )}
        </div>
      </div>

      {/* Meta row */}
      <div className="flex flex-wrap gap-4 text-xs text-[var(--color-text-muted)] mb-3">
        {view.owner && <span>Owner: {view.owner}</span>}
        {view.milestone && <span>Milestone: {view.milestone}</span>}
      </div>

      {/* Progress bar */}
      <div className="p-3 rounded-lg bg-[var(--color-bg-card)] border border-[var(--color-border)]">
        <div className="flex items-center justify-between text-xs text-[var(--color-text-muted)] mb-1.5">
          <span>{progress.completedSpecs}/{progress.totalSpecs} specs completed</span>
          <span>{pct}%</span>
        </div>
        <div className="h-2 rounded-full bg-[var(--color-bg-input)] overflow-hidden">
          <div
            className="h-full rounded-full bg-[var(--color-primary)] transition-all"
            style={{ width: `${pct}%` }}
          />
        </div>
        <div className="flex gap-4 mt-2 text-xs text-[var(--color-text-muted)]">
          {progress.activeSpecs > 0 && <span className="text-blue-400">{progress.activeSpecs} active</span>}
          {progress.blockedSpecs > 0 && <span className="text-red-400">{progress.blockedSpecs} blocked</span>}
        </div>
      </div>
    </div>
  )
}

function NarrativeSection({ view }: { view: EpicView }) {
  const { data: epicDoc } = useEpicDocument(view.projectId, view.epicId)
  const updateMutation = useUpdateEpicDocument(view.projectId, view.epicId)
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState({
    problem: '',
    goal: '',
    scope: [] as string[],
    nonGoals: [] as string[],
    successCriteria: [] as string[],
  })
  const [error, setError] = useState<string | null>(null)

  const { narrative } = view

  function startEditing() {
    setForm({
      problem: narrative.problem ?? '',
      goal: narrative.goal ?? '',
      scope: [...narrative.scope],
      nonGoals: [...narrative.nonGoals],
      successCriteria: [...narrative.successCriteria],
    })
    setError(null)
    setEditing(true)
  }

  async function save() {
    if (!epicDoc) return
    setError(null)
    try {
      await updateMutation.mutateAsync({
        version: epicDoc.version,
        problem: form.problem || undefined,
        goal: form.goal || undefined,
        scope: form.scope.filter(Boolean),
        nonGoals: form.nonGoals.filter(Boolean),
        successCriteria: form.successCriteria.filter(Boolean),
      })
      setEditing(false)
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Save failed'
      setError(msg.includes('409') || msg.includes('conflict') ? 'Version conflict — reload and try again.' : msg)
    }
  }

  if (editing) {
    return (
      <div className="space-y-4">
        <div>
          <h4 className="text-xs font-semibold text-[var(--color-text-muted)] uppercase tracking-wide mb-1">Problem</h4>
          <textarea
            value={form.problem}
            onChange={(e) => setForm({ ...form, problem: e.target.value })}
            rows={3}
            className="w-full text-sm px-2 py-1.5 rounded bg-[var(--color-bg-input)] border border-[var(--color-border)] text-[var(--color-text)] focus:outline-none focus:border-[var(--color-primary)] resize-y"
          />
        </div>
        <div>
          <h4 className="text-xs font-semibold text-[var(--color-text-muted)] uppercase tracking-wide mb-1">Goal</h4>
          <textarea
            value={form.goal}
            onChange={(e) => setForm({ ...form, goal: e.target.value })}
            rows={3}
            className="w-full text-sm px-2 py-1.5 rounded bg-[var(--color-bg-input)] border border-[var(--color-border)] text-[var(--color-text)] focus:outline-none focus:border-[var(--color-primary)] resize-y"
          />
        </div>
        <ListEditor label="Scope" items={form.scope} onChange={(scope) => setForm({ ...form, scope })} />
        <ListEditor label="Non-goals" items={form.nonGoals} onChange={(nonGoals) => setForm({ ...form, nonGoals })} />
        <ListEditor label="Success Criteria" items={form.successCriteria} onChange={(successCriteria) => setForm({ ...form, successCriteria })} />

        {error && <p className="text-xs text-red-400">{error}</p>}

        <div className="flex gap-2 pt-2">
          <button
            onClick={save}
            disabled={updateMutation.isPending}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded text-xs font-medium bg-[var(--color-primary)] text-white hover:opacity-90 disabled:opacity-50"
          >
            {updateMutation.isPending ? <Loader2 className="w-3 h-3 animate-spin" /> : <Save className="w-3 h-3" />}
            Save
          </button>
          <button
            onClick={() => setEditing(false)}
            disabled={updateMutation.isPending}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded text-xs font-medium bg-[var(--color-bg-elevated)] text-[var(--color-text)] hover:bg-[var(--color-bg-card-hover)] disabled:opacity-50"
          >
            Cancel
          </button>
        </div>
      </div>
    )
  }

  const sections: { label: string; items: string[] }[] = []
  if (narrative.scope.length > 0) sections.push({ label: 'Scope', items: narrative.scope })
  if (narrative.nonGoals.length > 0) sections.push({ label: 'Non-goals', items: narrative.nonGoals })
  if (narrative.successCriteria.length > 0) sections.push({ label: 'Success Criteria', items: narrative.successCriteria })

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <button
          onClick={startEditing}
          className="inline-flex items-center gap-1 px-2 py-1 rounded text-xs text-[var(--color-text-muted)] hover:text-[var(--color-text)] hover:bg-[var(--color-bg-elevated)] transition-colors"
        >
          <Pencil className="w-3 h-3" />
          Edit
        </button>
      </div>
      {narrative.problem && (
        <div>
          <h4 className="text-xs font-semibold text-[var(--color-text-muted)] uppercase tracking-wide mb-1">Problem</h4>
          <p className="text-sm text-[var(--color-text)]">{narrative.problem}</p>
        </div>
      )}
      {narrative.goal && (
        <div>
          <h4 className="text-xs font-semibold text-[var(--color-text-muted)] uppercase tracking-wide mb-1">Goal</h4>
          <p className="text-sm text-[var(--color-text)]">{narrative.goal}</p>
        </div>
      )}
      {sections.map((s) => (
        <div key={s.label}>
          <h4 className="text-xs font-semibold text-[var(--color-text-muted)] uppercase tracking-wide mb-1">{s.label}</h4>
          <ul className="list-disc list-inside space-y-0.5">
            {s.items.map((item, i) => (
              <li key={i} className="text-sm text-[var(--color-text)]">{item}</li>
            ))}
          </ul>
        </div>
      ))}
    </div>
  )
}

function ChildSpecRow({ spec, projectId }: { spec: EpicChildSpec; projectId: string }) {
  const risk = spec.riskLevel ? riskBadge[spec.riskLevel] : null

  return (
    <Link
      to={`/projects/${projectId}/specs/${spec.specId}`}
      className="flex items-center gap-3 px-3 py-2.5 rounded-lg hover:bg-[var(--color-bg-card-hover)] transition-colors"
    >
      <Circle className={`w-2.5 h-2.5 fill-current flex-shrink-0 ${stateColor[spec.state] ?? 'text-gray-500'}`} />
      <span className="text-xs font-mono text-[var(--color-text-muted)] flex-shrink-0 w-16">{spec.specId}</span>
      <span className="text-sm text-[var(--color-text)] flex-1 truncate">{spec.title}</span>
      <span className={`text-xs px-1.5 py-0.5 rounded bg-[var(--color-bg-elevated)] ${stateColor[spec.state] ?? 'text-gray-500'}`}>
        {stateLabel[spec.state] ?? spec.state}
      </span>
      {risk && (
        <span className={`text-xs px-1.5 py-0.5 rounded ${risk.bg} ${risk.text}`}>
          {spec.riskLevel}
        </span>
      )}
      {spec.lastActivityAt && (
        <span className="text-xs text-[var(--color-text-muted)] flex-shrink-0 w-24 text-right">
          {new Date(spec.lastActivityAt).toLocaleDateString()}
        </span>
      )}
    </Link>
  )
}

function ChildSpecsSection({ view }: { view: EpicView }) {
  const { childSpecs } = view
  const projectId = view.projectId

  // Group by state category
  const inProgress = childSpecs.filter(s =>
    ['implementation', 'testGeneration', 'review', 'architectureReview', 'queued'].includes(s.state)
  )
  const completed = childSpecs.filter(s => s.state === 'completed' || s.state === 'active')
  const drafts = childSpecs.filter(s => s.state === 'draft')
  const other = childSpecs.filter(s => s.state === 'failed' || s.state === 'archived')

  const groups = [
    { label: 'In Progress', specs: inProgress },
    { label: 'Completed', specs: completed },
    { label: 'Drafts', specs: drafts },
    { label: 'Failed / Archived', specs: other },
  ].filter(g => g.specs.length > 0)

  if (childSpecs.length === 0) {
    return <p className="text-sm text-[var(--color-text-muted)]">No child specs yet.</p>
  }

  return (
    <div className="space-y-4">
      {groups.map((group) => (
        <div key={group.label}>
          <h4 className="text-xs font-semibold text-[var(--color-text-muted)] uppercase tracking-wide mb-1.5">
            {group.label} ({group.specs.length})
          </h4>
          <div className="space-y-0.5">
            {group.specs.map((spec) => (
              <ChildSpecRow key={spec.specId} spec={spec} projectId={projectId} />
            ))}
          </div>
        </div>
      ))}
    </div>
  )
}

function DependenciesSection({ view }: { view: EpicView }) {
  const hasDeps = view.epicDependsOn.length > 0
  const hasRelated = view.relatedDocs.length > 0

  if (!hasDeps && !hasRelated) {
    return <p className="text-sm text-[var(--color-text-muted)]">No dependencies or related docs.</p>
  }

  return (
    <div className="space-y-4">
      {hasDeps && (
        <div>
          <h4 className="text-xs font-semibold text-[var(--color-text-muted)] uppercase tracking-wide mb-1">Depends On</h4>
          <ul className="space-y-1">
            {view.epicDependsOn.map((dep) => (
              <li key={dep} className="text-sm text-[var(--color-text)] font-mono">{dep}</li>
            ))}
          </ul>
        </div>
      )}
      {hasRelated && (
        <div>
          <h4 className="text-xs font-semibold text-[var(--color-text-muted)] uppercase tracking-wide mb-1">Related Docs</h4>
          <ul className="space-y-1">
            {view.relatedDocs.map((doc) => (
              <li key={doc} className="text-sm text-[var(--color-text)]">{doc}</li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}

export function EpicOverviewPage() {
  const { projectId, epicId } = useParams<{ projectId: string; epicId: string }>()
  const { data: view, isLoading } = useEpicView(projectId!, epicId!)

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-32">
        <div className="w-6 h-6 border-2 border-[var(--color-primary)] border-t-transparent rounded-full animate-spin" />
      </div>
    )
  }

  if (!view) {
    return (
      <div className="text-center py-12">
        <AlertTriangle className="w-10 h-10 mx-auto mb-3 text-[var(--color-text-muted)]" />
        <p className="text-[var(--color-text-muted)]">Epic data not available</p>
      </div>
    )
  }

  return (
    <div className="max-w-4xl">
      <ProgressHeader view={view} />

      {/* Narrative */}
      <Cell
        title="Epic Narrative"
        icon={<FileText className="w-4 h-4" />}
        accentColor="#6366f1"
        sectionId="epic-narrative"
      >
        <NarrativeSection view={view} />
      </Cell>

      {/* Child Specs */}
      <div className="mt-4">
        <Cell
          title="Child Specs"
          icon={<ListChecks className="w-4 h-4" />}
          accentColor="var(--color-primary)"
          sectionId="epic-child-specs"
          badge={
            <span className="text-xs px-1.5 py-0.5 rounded bg-[var(--color-bg-elevated)] text-[var(--color-text-muted)]">
              {view.childSpecs.length}
            </span>
          }
        >
          <ChildSpecsSection view={view} />
        </Cell>
      </div>

      {/* Dependencies & Related Docs */}
      <div className="mt-4">
        <Cell
          title="Dependencies & Related Docs"
          icon={<Link2 className="w-4 h-4" />}
          accentColor="#f59e0b"
          sectionId="epic-dependencies"
          defaultCollapsed
        >
          <DependenciesSection view={view} />
        </Cell>
      </div>
    </div>
  )
}
