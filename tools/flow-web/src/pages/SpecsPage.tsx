import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Plus, FileText } from 'lucide-react'
import { useSpecs, useCreateSpec } from '@/hooks/useSpecs'
import { StateBadge, StatusBadge } from '@/components/StateBadge'
import type { FlowState } from '@/types/flow'

const stateFilters: { label: string; value: string }[] = [
  { label: 'All', value: '' },
  { label: 'Draft', value: 'draft' },
  { label: 'Queued', value: 'queued' },
  { label: 'Implementation', value: 'implementation' },
  { label: 'Review', value: 'review' },
  { label: 'Active', value: 'active' },
  { label: 'Failed', value: 'failed' },
  { label: 'Completed', value: 'completed' },
]

export function SpecsPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const [stateFilter, setStateFilter] = useState('')
  const [showCreate, setShowCreate] = useState(false)

  const { data: specs, isLoading } = useSpecs(projectId!, stateFilter ? { state: stateFilter } : undefined)
  const createMutation = useCreateSpec(projectId!)

  const handleCreate = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    await createMutation.mutateAsync({
      title: form.get('title') as string,
      problem: (form.get('problem') as string) || undefined,
      goal: (form.get('goal') as string) || undefined,
    })
    setShowCreate(false)
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-xl font-bold text-[var(--color-text-bright)]">Specs</h1>
        <button
          onClick={() => setShowCreate(!showCreate)}
          className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-[var(--color-primary)] hover:bg-[var(--color-primary-hover)] text-white text-sm font-medium transition-colors"
        >
          <Plus className="w-4 h-4" /> New Spec
        </button>
      </div>

      {showCreate && (
        <form onSubmit={handleCreate} className="mb-6 p-4 rounded-lg bg-[var(--color-bg-card)] border border-[var(--color-border)]">
          <div className="grid gap-3">
            <input name="title" placeholder="Title" required
              className="w-full px-3 py-2 rounded-md bg-[var(--color-bg-input)] border border-[var(--color-border)] text-[var(--color-text)] text-sm placeholder:text-[var(--color-text-muted)]" />
            <input name="problem" placeholder="Problem (optional)"
              className="w-full px-3 py-2 rounded-md bg-[var(--color-bg-input)] border border-[var(--color-border)] text-[var(--color-text)] text-sm placeholder:text-[var(--color-text-muted)]" />
            <input name="goal" placeholder="Goal (optional)"
              className="w-full px-3 py-2 rounded-md bg-[var(--color-bg-input)] border border-[var(--color-border)] text-[var(--color-text)] text-sm placeholder:text-[var(--color-text-muted)]" />
            <div className="flex gap-2">
              <button type="submit" disabled={createMutation.isPending}
                className="px-4 py-1.5 rounded-md bg-[var(--color-primary)] hover:bg-[var(--color-primary-hover)] text-white text-sm font-medium disabled:opacity-50">
                {createMutation.isPending ? 'Creating...' : 'Create'}
              </button>
              <button type="button" onClick={() => setShowCreate(false)}
                className="px-4 py-1.5 rounded-md bg-[var(--color-bg-card-hover)] text-[var(--color-text-muted)] text-sm">
                Cancel
              </button>
            </div>
          </div>
        </form>
      )}

      <div className="flex gap-1.5 mb-4 flex-wrap">
        {stateFilters.map((f) => (
          <button
            key={f.value}
            onClick={() => setStateFilter(f.value)}
            className={`px-2.5 py-1 rounded-md text-xs font-medium transition-colors ${
              stateFilter === f.value
                ? 'bg-[var(--color-primary)] text-white'
                : 'bg-[var(--color-bg-card)] text-[var(--color-text-muted)] hover:bg-[var(--color-bg-card-hover)]'
            }`}
          >
            {f.label}
          </button>
        ))}
      </div>

      {isLoading ? (
        <div className="flex items-center justify-center h-32">
          <div className="w-6 h-6 border-2 border-[var(--color-primary)] border-t-transparent rounded-full animate-spin" />
        </div>
      ) : !specs?.length ? (
        <div className="text-center py-12">
          <FileText className="w-10 h-10 mx-auto mb-3 text-[var(--color-text-muted)]" />
          <p className="text-[var(--color-text-muted)]">No specs found</p>
        </div>
      ) : (
        <div className="grid gap-2">
          {specs.map((spec) => (
            <Link
              key={spec.id}
              to={`/projects/${projectId}/specs/${spec.id}`}
              className="flex items-center gap-4 p-3.5 rounded-lg bg-[var(--color-bg-card)] hover:bg-[var(--color-bg-card-hover)] border border-[var(--color-border)] transition-colors"
            >
              <span className="text-xs font-mono text-[var(--color-text-muted)] w-14">{spec.id}</span>
              <span className="flex-1 font-medium text-sm truncate">{spec.title}</span>
              <StateBadge state={spec.state as FlowState} />
              <StatusBadge status={spec.processingStatus} />
            </Link>
          ))}
        </div>
      )}
    </div>
  )
}
