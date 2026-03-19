import { useParams } from 'react-router-dom'
import { Clock, CheckCircle, ListChecks, GitBranch, MessageSquare, FileSearch } from 'lucide-react'
import { useSpec, useAssignments, useReviewRequests, useActivity, useEvidence } from '@/hooks/useSpecs'
import { StateBadge, StatusBadge } from '@/components/StateBadge'
import type { FlowState } from '@/types/flow'

// State flow pipeline
const PIPELINE: FlowState[] = [
  'draft', 'queued', 'architectureReview', 'testGeneration',
  'implementation', 'review', 'active',
]

function PipelineView({ current }: { current: FlowState }) {
  const idx = PIPELINE.indexOf(current)
  return (
    <div className="flex items-center gap-1 overflow-x-auto py-2">
      {PIPELINE.map((s, i) => {
        const isCurrent = s === current
        const isPast = idx >= 0 && i < idx
        const bg = isCurrent
          ? 'bg-[var(--color-primary)] text-white'
          : isPast
            ? 'bg-green-900/50 text-green-400'
            : 'bg-[var(--color-bg-card)] text-[var(--color-text-muted)]'
        return (
          <div key={s} className="flex items-center gap-1">
            {i > 0 && <div className={`w-4 h-0.5 ${isPast || isCurrent ? 'bg-green-700' : 'bg-[var(--color-border)]'}`} />}
            <div className={`px-2 py-1 rounded text-xs font-medium whitespace-nowrap ${bg}`}>
              {s === 'architectureReview' ? 'Arch' : s === 'testGeneration' ? 'TestGen' : s.charAt(0).toUpperCase() + s.slice(1)}
            </div>
          </div>
        )
      })}
      {(current === 'failed' || current === 'completed' || current === 'archived') && (
        <>
          <div className="w-4 h-0.5 bg-[var(--color-border)]" />
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

export function SpecDetailPage() {
  const { projectId, specId } = useParams<{ projectId: string; specId: string }>()
  const { data: spec, isLoading } = useSpec(projectId!, specId!)
  const { data: assignments } = useAssignments(projectId!, specId!)
  const { data: reviewRequests } = useReviewRequests(projectId!, specId!)
  const { data: activity } = useActivity(projectId!, specId!)
  const { data: evidence } = useEvidence(projectId!, specId!)

  if (isLoading) return <p className="text-[var(--color-text-muted)]">Loading...</p>
  if (!spec) return <p className="text-[var(--color-danger)]">Spec not found</p>

  return (
    <div className="max-w-4xl mx-auto">
      {/* Header */}
      <div className="mb-6">
        <div className="flex items-center gap-3 mb-2">
          <span className="font-mono text-[var(--color-text-muted)]">{spec.id}</span>
          <StateBadge state={spec.state as FlowState} />
          <StatusBadge status={spec.processingStatus} />
        </div>
        <h1 className="text-2xl font-bold mb-3">{spec.title}</h1>
        <PipelineView current={spec.state as FlowState} />
      </div>

      {/* Info grid */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-6">
        <InfoCard label="Type" value={spec.type} />
        <InfoCard label="Risk" value={spec.riskLevel} />
        <InfoCard label="Version" value={String(spec.version)} />
        <InfoCard label="Created" value={formatTime(spec.createdAt)} />
      </div>

      {/* Problem & Goal */}
      {(spec.problem || spec.goal) && (
        <div className="grid md:grid-cols-2 gap-3 mb-6">
          {spec.problem && (
            <div className="p-4 rounded-lg bg-[var(--color-bg-card)] border border-[var(--color-border)]">
              <h3 className="text-xs font-semibold text-[var(--color-text-muted)] uppercase mb-1">Problem</h3>
              <p className="text-sm">{spec.problem}</p>
            </div>
          )}
          {spec.goal && (
            <div className="p-4 rounded-lg bg-[var(--color-bg-card)] border border-[var(--color-border)]">
              <h3 className="text-xs font-semibold text-[var(--color-text-muted)] uppercase mb-1">Goal</h3>
              <p className="text-sm">{spec.goal}</p>
            </div>
          )}
        </div>
      )}

      {/* Acceptance Criteria */}
      {spec.acceptanceCriteria?.length > 0 && (
        <Section icon={<ListChecks className="w-4 h-4" />} title="Acceptance Criteria">
          <div className="grid gap-1.5">
            {spec.acceptanceCriteria.map((ac) => (
              <div key={ac.id} className="flex items-start gap-2 text-sm">
                <CheckCircle className={`w-4 h-4 mt-0.5 flex-shrink-0 ${ac.testable ? 'text-green-500' : 'text-[var(--color-text-muted)]'}`} />
                <span>{ac.text}</span>
                {ac.notes && <span className="text-[var(--color-text-muted)]">({ac.notes})</span>}
              </div>
            ))}
          </div>
        </Section>
      )}

      {/* Assignments */}
      {assignments && assignments.length > 0 && (
        <Section icon={<GitBranch className="w-4 h-4" />} title="Assignments">
          <div className="grid gap-2">
            {assignments.map((a) => (
              <div key={a.id} className="flex items-center gap-3 text-sm p-2 rounded bg-[var(--color-bg)]/50">
                <span className="font-mono text-xs text-[var(--color-text-muted)]">{a.id}</span>
                <span className="text-[var(--color-text-muted)]">{a.agentRole}</span>
                <span>{a.type}</span>
                <span className={`ml-auto text-xs px-2 py-0.5 rounded ${
                  a.status === 'completed' ? 'bg-green-900/50 text-green-400' :
                  a.status === 'running' ? 'bg-blue-900/50 text-blue-400' :
                  a.status === 'failed' ? 'bg-red-900/50 text-red-400' :
                  'bg-gray-800 text-gray-400'
                }`}>
                  {a.status}
                </span>
              </div>
            ))}
          </div>
        </Section>
      )}

      {/* Review Requests */}
      {reviewRequests && reviewRequests.length > 0 && (
        <Section icon={<MessageSquare className="w-4 h-4" />} title="Review Requests">
          <div className="grid gap-2">
            {reviewRequests.map((rr) => (
              <div key={rr.id} className="p-3 rounded bg-[var(--color-bg)]/50 text-sm">
                <div className="flex items-center gap-2 mb-1">
                  <span className="font-mono text-xs text-[var(--color-text-muted)]">{rr.id}</span>
                  <span className={`text-xs px-2 py-0.5 rounded ${
                    rr.status === 'open' ? 'bg-yellow-900/50 text-yellow-400' : 'bg-gray-800 text-gray-400'
                  }`}>{rr.status}</span>
                </div>
                {rr.summary && <p className="text-[var(--color-text-muted)]">{rr.summary}</p>}
              </div>
            ))}
          </div>
        </Section>
      )}

      {/* Evidence */}
      {evidence && evidence.length > 0 && (
        <Section icon={<FileSearch className="w-4 h-4" />} title="Evidence">
          <div className="grid gap-2">
            {evidence.map((m) => (
              <div key={m.runId} className="p-3 rounded bg-[var(--color-bg)]/50 text-sm">
                <div className="flex items-center gap-2 mb-1">
                  <span className="font-mono text-xs text-[var(--color-text-muted)]">{m.runId}</span>
                  <span className="text-xs text-[var(--color-text-muted)]">{formatTime(m.createdAt)}</span>
                </div>
                {m.refs.map((r, i) => (
                  <div key={i} className="text-xs text-[var(--color-text-muted)] ml-4">
                    [{r.kind}] {r.relativePath} {r.summary && `— ${r.summary}`}
                  </div>
                ))}
              </div>
            ))}
          </div>
        </Section>
      )}

      {/* Activity */}
      {activity && activity.length > 0 && (
        <Section icon={<Clock className="w-4 h-4" />} title="Activity">
          <div className="grid gap-1">
            {activity.map((evt) => (
              <div key={evt.eventId} className="flex items-start gap-3 text-sm py-1.5 border-b border-[var(--color-border)]/50 last:border-0">
                <span className="text-xs text-[var(--color-text-muted)] whitespace-nowrap w-36">
                  {formatTime(evt.timestamp)}
                </span>
                <span className="text-xs font-mono text-[var(--color-info)] w-32 truncate">{evt.action}</span>
                <span className="text-[var(--color-text-muted)] flex-1">{evt.message}</span>
              </div>
            ))}
          </div>
        </Section>
      )}
    </div>
  )
}

function InfoCard({ label, value }: { label: string; value: string }) {
  return (
    <div className="p-3 rounded-lg bg-[var(--color-bg-card)] border border-[var(--color-border)]">
      <p className="text-xs text-[var(--color-text-muted)] uppercase mb-0.5">{label}</p>
      <p className="text-sm font-medium">{value}</p>
    </div>
  )
}

function Section({ icon, title, children }: { icon: React.ReactNode; title: string; children: React.ReactNode }) {
  return (
    <div className="mb-6">
      <h2 className="flex items-center gap-2 text-sm font-semibold text-[var(--color-text-muted)] uppercase mb-3">
        {icon} {title}
      </h2>
      <div className="p-4 rounded-lg bg-[var(--color-bg-card)] border border-[var(--color-border)]">
        {children}
      </div>
    </div>
  )
}
