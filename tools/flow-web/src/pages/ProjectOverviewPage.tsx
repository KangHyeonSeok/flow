import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  Layers,
  AlertTriangle,
  FileText,
  ArrowRight,
  BarChart3,
  Pencil,
  Save,
  Loader2,
  Flame,
  Circle,
} from 'lucide-react'
import { useProjectView, useProjectDocument, useUpdateProjectDocument } from '@/hooks/useSpecs'
import { Cell } from '@/components/Cell'
import { ListEditor } from '@/components/ListEditor'
import type { EpicSummary, HotspotEntry, ProjectView } from '@/types/flow'

function StatCard({ label, value, accent }: { label: string; value: number; accent?: string }) {
  return (
    <div className="flex flex-col items-center px-4 py-3 rounded-lg bg-[var(--color-bg-card)] border border-[var(--color-border)]">
      <span className={`text-2xl font-bold ${accent ?? 'text-[var(--color-text-bright)]'}`}>{value}</span>
      <span className="text-xs text-[var(--color-text-muted)] mt-0.5">{label}</span>
    </div>
  )
}

function EpicCard({ epic, projectId }: { epic: EpicSummary; projectId: string }) {
  const total = epic.specCounts.total
  const completed = epic.specCounts.completed
  const pct = total > 0 ? Math.round((completed / total) * 100) : 0

  return (
    <Link
      to={`/projects/${projectId}/epics/${epic.epicId}`}
      className="block p-4 rounded-lg bg-[var(--color-bg-card)] border border-[var(--color-border)] hover:bg-[var(--color-bg-card-hover)] transition-colors"
    >
      <div className="flex items-start justify-between gap-3 mb-2">
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2">
            <span className="text-xs font-mono text-[var(--color-text-muted)]">{epic.epicId}</span>
            {epic.priority && (
              <span className={`text-xs px-1.5 py-0.5 rounded ${
                epic.priority === 'high' ? 'bg-red-900/40 text-red-400' :
                epic.priority === 'medium' ? 'bg-yellow-900/40 text-yellow-400' :
                'bg-gray-700 text-gray-400'
              }`}>
                {epic.priority}
              </span>
            )}
          </div>
          <h3 className="text-sm font-semibold text-[var(--color-text-bright)] mt-1">{epic.title}</h3>
          {epic.summary && (
            <p className="text-xs text-[var(--color-text-muted)] mt-1 line-clamp-2">{epic.summary}</p>
          )}
        </div>
        <ArrowRight className="w-4 h-4 text-[var(--color-text-muted)] flex-shrink-0 mt-1" />
      </div>

      {/* Progress bar */}
      <div className="mt-3">
        <div className="flex items-center justify-between text-xs text-[var(--color-text-muted)] mb-1">
          <span>{completed}/{total} specs</span>
          <span>{pct}%</span>
        </div>
        <div className="h-1.5 rounded-full bg-[var(--color-bg-input)] overflow-hidden">
          <div
            className="h-full rounded-full bg-[var(--color-primary)] transition-all"
            style={{ width: `${pct}%` }}
          />
        </div>
      </div>

      {/* Counts row */}
      <div className="flex gap-3 mt-2 text-xs text-[var(--color-text-muted)]">
        {epic.specCounts.active > 0 && <span className="text-blue-400">{epic.specCounts.active} active</span>}
        {epic.specCounts.review > 0 && <span className="text-yellow-400">{epic.specCounts.review} review</span>}
        {epic.specCounts.blocked > 0 && <span className="text-red-400">{epic.specCounts.blocked} blocked</span>}
      </div>

      {/* Meta */}
      {(epic.owner || epic.milestone) && (
        <div className="flex gap-3 mt-2 text-xs text-[var(--color-text-muted)]">
          {epic.owner && <span>{epic.owner}</span>}
          {epic.milestone && <span>{epic.milestone}</span>}
        </div>
      )}
    </Link>
  )
}

const hotspotColor: Record<string, string> = {
  review: 'text-yellow-400',
  failure: 'text-red-400',
  onHold: 'text-orange-400',
}

function HotspotGroup({ label, entries, projectId, color }: { label: string; entries: HotspotEntry[]; projectId: string; color: string }) {
  if (entries.length === 0) return null
  return (
    <div>
      <h4 className="text-xs font-semibold text-[var(--color-text-muted)] uppercase tracking-wide mb-1.5">{label} ({entries.length})</h4>
      <div className="space-y-0.5">
        {entries.map((e) => (
          <Link
            key={e.specId}
            to={`/projects/${projectId}/specs/${e.specId}`}
            className="flex items-center gap-3 px-3 py-2 rounded-lg hover:bg-[var(--color-bg-card-hover)] transition-colors"
          >
            <Circle className={`w-2.5 h-2.5 fill-current flex-shrink-0 ${color}`} />
            <span className="text-xs font-mono text-[var(--color-text-muted)] flex-shrink-0 w-16">{e.specId}</span>
            <span className="text-sm text-[var(--color-text)] flex-1 truncate">{e.title}</span>
            <span className="text-xs text-[var(--color-text-muted)]">{e.reason}</span>
          </Link>
        ))}
      </div>
    </div>
  )
}

function HotspotsSection({ view }: { view: ProjectView }) {
  const { hotspots } = view
  const total = hotspots.review.length + hotspots.failure.length + hotspots.onHold.length
  if (total === 0) {
    return <p className="text-sm text-[var(--color-text-muted)]">No hotspots — everything looks clear.</p>
  }
  return (
    <div className="space-y-4">
      <HotspotGroup label="Review" entries={hotspots.review} projectId={view.projectId} color={hotspotColor.review} />
      <HotspotGroup label="Failed" entries={hotspots.failure} projectId={view.projectId} color={hotspotColor.failure} />
      <HotspotGroup label="On Hold" entries={hotspots.onHold} projectId={view.projectId} color={hotspotColor.onHold} />
    </div>
  )
}

function DocumentSection({ view, projectId }: { view: ProjectView; projectId: string }) {
  const doc = view.document
  const { data: fullDoc } = useProjectDocument(projectId)
  const updateMutation = useUpdateProjectDocument(projectId)
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState({
    problem: '',
    goals: [] as string[],
    nonGoals: [] as string[],
    contextAndConstraints: [] as string[],
    architectureOverview: [] as string[],
  })
  const [error, setError] = useState<string | null>(null)

  function startEditing() {
    setForm({
      problem: doc.problem ?? '',
      goals: [...doc.goals],
      nonGoals: [...doc.nonGoals],
      contextAndConstraints: [...doc.contextAndConstraints],
      architectureOverview: [...doc.architectureOverview],
    })
    setError(null)
    setEditing(true)
  }

  async function save() {
    if (!fullDoc) return
    setError(null)
    try {
      await updateMutation.mutateAsync({
        version: fullDoc.version,
        problem: form.problem || undefined,
        goals: form.goals.filter(Boolean),
        nonGoals: form.nonGoals.filter(Boolean),
        contextAndConstraints: form.contextAndConstraints.filter(Boolean),
        architectureOverview: form.architectureOverview.filter(Boolean),
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
        <ListEditor label="Goals" items={form.goals} onChange={(goals) => setForm({ ...form, goals })} />
        <ListEditor label="Non-goals" items={form.nonGoals} onChange={(nonGoals) => setForm({ ...form, nonGoals })} />
        <ListEditor label="Context & Constraints" items={form.contextAndConstraints} onChange={(contextAndConstraints) => setForm({ ...form, contextAndConstraints })} />
        <ListEditor label="Architecture" items={form.architectureOverview} onChange={(architectureOverview) => setForm({ ...form, architectureOverview })} />

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
  if (doc.goals.length > 0) sections.push({ label: 'Goals', items: doc.goals })
  if (doc.nonGoals.length > 0) sections.push({ label: 'Non-goals', items: doc.nonGoals })
  if (doc.contextAndConstraints.length > 0) sections.push({ label: 'Context & Constraints', items: doc.contextAndConstraints })
  if (doc.architectureOverview.length > 0) sections.push({ label: 'Architecture', items: doc.architectureOverview })

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
      {doc.problem && (
        <div>
          <h4 className="text-xs font-semibold text-[var(--color-text-muted)] uppercase tracking-wide mb-1">Problem</h4>
          <p className="text-sm text-[var(--color-text)]">{doc.problem}</p>
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

export function ProjectOverviewPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const { data: view, isLoading } = useProjectView(projectId!)

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
        <p className="text-[var(--color-text-muted)]">Project data not available</p>
      </div>
    )
  }

  return (
    <div className="max-w-4xl">
      {/* Header */}
      <div className="mb-6">
        <h1 className="text-xl font-bold text-[var(--color-text-bright)]">{view.title}</h1>
        {view.summary && (
          <p className="text-sm text-[var(--color-text-muted)] mt-1">{view.summary}</p>
        )}
        {view.lastActivityAt && (
          <p className="text-xs text-[var(--color-text-muted)] mt-1">
            Last activity: {new Date(view.lastActivityAt).toLocaleString()}
          </p>
        )}
      </div>

      {/* Stats */}
      <div className="grid grid-cols-3 sm:grid-cols-6 gap-2 mb-6">
        <StatCard label="Specs" value={view.stats.specCount} />
        <StatCard label="Epics" value={view.stats.epicCount} />
        <StatCard label="Active Epics" value={view.stats.activeEpicCount} />
        <StatCard label="Open Reviews" value={view.stats.openReviewCount} accent={view.stats.openReviewCount > 0 ? 'text-yellow-400' : undefined} />
        <StatCard label="Failed" value={view.stats.failedSpecCount} accent={view.stats.failedSpecCount > 0 ? 'text-red-400' : undefined} />
        <StatCard label="On Hold" value={view.stats.onHoldSpecCount} />
      </div>

      {/* Epic Index */}
      <Cell
        title="Epics"
        icon={<Layers className="w-4 h-4" />}
        accentColor="var(--color-primary)"
        sectionId="epics"
      >
        {view.epics.length === 0 ? (
          <p className="text-sm text-[var(--color-text-muted)]">No epics defined yet.</p>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2">
            {view.epics.map((epic) => (
              <EpicCard key={epic.epicId} epic={epic} projectId={view.projectId} />
            ))}
          </div>
        )}
      </Cell>

      {/* Hotspots */}
      <div className="mt-4">
        <Cell
          title="Hotspots"
          icon={<Flame className="w-4 h-4" />}
          accentColor="#ef4444"
          sectionId="hotspots"
          defaultCollapsed={view.hotspots.review.length + view.hotspots.failure.length + view.hotspots.onHold.length === 0}
        >
          <HotspotsSection view={view} />
        </Cell>
      </div>

      {/* Document Summary */}
      <div className="mt-4">
        <Cell
          title="Project Document"
          icon={<FileText className="w-4 h-4" />}
          accentColor="#6366f1"
          sectionId="document"
          defaultCollapsed
        >
          <DocumentSection view={view} projectId={projectId!} />
        </Cell>
      </div>

      {/* Quick links */}
      <div className="mt-6">
        <Link
          to={`/projects/${projectId}/specs`}
          className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-[var(--color-bg-card)] border border-[var(--color-border)] hover:bg-[var(--color-bg-card-hover)] text-sm text-[var(--color-text)] transition-colors"
        >
          <BarChart3 className="w-4 h-4 text-[var(--color-text-muted)]" />
          All Specs
        </Link>
      </div>
    </div>
  )
}
