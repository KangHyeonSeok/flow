import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Search, FileText, Circle, BookOpenText } from 'lucide-react'
import { useActivity, useAssignments, useEvidence, useReviewRequests, useSpec, useSpecs } from '@/hooks/useSpecs'
import type { FlowState } from '@/types/flow'

type DocumentSection = {
  id: string
  label: string
  badge?: string
}

const stateIndicator: Record<string, string> = {
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

export function Sidebar() {
  const { projectId, specId } = useParams<{ projectId: string; specId: string }>()
  const [search, setSearch] = useState('')
  const [activeSectionId, setActiveSectionId] = useState<string>('section-document-summary')
  const { data: specs } = useSpecs(projectId!, undefined)
  const { data: currentSpecDetail } = useSpec(projectId!, specId!)
  const { data: currentReviewRequests } = useReviewRequests(projectId!, specId!)
  const { data: currentAssignments } = useAssignments(projectId!, specId!)
  const { data: currentEvidence } = useEvidence(projectId!, specId!)
  const { data: currentActivity } = useActivity(projectId!, specId!)

  if (!projectId) return null

  const filtered = specs?.filter(s =>
    !search || s.title.toLowerCase().includes(search.toLowerCase()) || s.id.toLowerCase().includes(search.toLowerCase())
  )

  // Group specs by state category
  const active = filtered?.filter(s => ['implementation', 'testGeneration', 'review', 'architectureReview', 'queued'].includes(s.state)) ?? []
  const completed = filtered?.filter(s => s.state === 'completed' || s.state === 'active') ?? []
  const drafts = filtered?.filter(s => s.state === 'draft') ?? []
  const failed = filtered?.filter(s => s.state === 'failed' || s.state === 'archived') ?? []
  const openReviewCount = currentReviewRequests?.filter((request) => request.status === 'open').length ?? 0
  const documentSections: DocumentSection[] = currentSpecDetail ? [
    { id: 'section-document-summary', label: 'Document Summary' },
    ...(currentSpecDetail.problem
      ? [{ id: 'section-problem', label: 'Problem' }]
      : []),
    ...(currentSpecDetail.goal
      ? [{ id: 'section-goal', label: 'Goal' }]
      : []),
    ...(currentSpecDetail.context
      ? [{ id: 'section-context', label: 'Context' }]
      : []),
    ...(currentSpecDetail.nonGoals
      ? [{ id: 'section-non-goals', label: 'Non-Goals' }]
      : []),
    ...(currentSpecDetail.implementationNotes
      ? [{ id: 'section-implementation-notes', label: 'Implementation Notes' }]
      : []),
    ...(currentSpecDetail.testPlan
      ? [{ id: 'section-test-plan', label: 'Test Plan' }]
      : []),
    ...(currentSpecDetail.dependencies && (currentSpecDetail.dependencies.dependsOn.length > 0 || currentSpecDetail.dependencies.blocks.length > 0)
      ? [{ id: 'section-dependencies', label: 'Dependencies', badge: `${currentSpecDetail.dependencies.dependsOn.length + currentSpecDetail.dependencies.blocks.length}` }]
      : []),
    ...((currentSpecDetail.acceptanceCriteria?.length ?? 0) > 0
      ? [{ id: 'section-acceptance-criteria', label: 'Acceptance Criteria', badge: `${currentSpecDetail.acceptanceCriteria.length}` }]
      : []),
    ...((currentSpecDetail.tests?.length ?? 0) > 0
      ? [{ id: 'section-bdd-scenarios', label: 'BDD Scenarios', badge: `${currentSpecDetail.tests?.length ?? 0}` }]
      : []),
    ...(currentAssignments && currentAssignments.length > 0
      ? [{ id: 'section-implementation-status', label: 'Implementation Status', badge: `${currentAssignments.length}` }]
      : []),
    ...(currentReviewRequests && currentReviewRequests.length > 0
      ? [{ id: 'section-review-requests', label: 'Review Requests', badge: openReviewCount > 0 ? `${openReviewCount} open` : `${currentReviewRequests.length}` }]
      : []),
    ...(currentEvidence && currentEvidence.length > 0
      ? [{ id: 'section-evidence', label: 'Evidence', badge: `${currentEvidence.length}` }]
      : []),
    ...(currentActivity && currentActivity.length > 0
      ? [{ id: 'section-activity', label: 'Activity', badge: `${currentActivity.length}` }]
      : []),
  ] : []
  const sectionSignature = documentSections.map((section) => section.id).join('|')

  useEffect(() => {
    if (!specId || documentSections.length === 0) {
      setActiveSectionId('section-document-summary')
      return
    }

    const hash = window.location.hash.replace('#', '')
    if (hash && documentSections.some((section) => section.id === hash)) {
      setActiveSectionId(hash)
      return
    }

    setActiveSectionId(documentSections[0].id)
  }, [specId, sectionSignature])

  useEffect(() => {
    if (!specId || documentSections.length === 0) return

    const scrollRoot = document.querySelector<HTMLElement>('[data-spec-scroll-root]')
    if (!scrollRoot) return

    const sectionElements = documentSections
      .map((section) => document.getElementById(section.id))
      .filter((element): element is HTMLElement => element !== null)

    if (sectionElements.length === 0) return

    const updateFromHash = () => {
      const hash = window.location.hash.replace('#', '')
      if (hash && documentSections.some((section) => section.id === hash)) {
        setActiveSectionId(hash)
      }
    }

    updateFromHash()

    const observer = new IntersectionObserver(
      (entries) => {
        const visibleEntries = entries
          .filter((entry) => entry.isIntersecting)
          .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)

        if (visibleEntries.length === 0) return

        const nextActiveId = visibleEntries[0].target.id
        setActiveSectionId((current) => current === nextActiveId ? current : nextActiveId)
      },
      {
        root: scrollRoot,
        rootMargin: '-10% 0px -70% 0px',
        threshold: [0, 0.2, 0.5, 1],
      },
    )

    sectionElements.forEach((element) => observer.observe(element))
    window.addEventListener('hashchange', updateFromHash)

    return () => {
      observer.disconnect()
      window.removeEventListener('hashchange', updateFromHash)
    }
  }, [specId, sectionSignature])

  return (
    <aside className="w-60 flex-shrink-0 border-r border-[var(--color-border)] bg-[var(--color-bg)] flex flex-col h-full overflow-hidden">
      <div className="p-3 border-b border-[var(--color-border)]">
        <div className="relative">
          <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-[var(--color-text-muted)]" />
          <input
            type="text"
            placeholder="Search specs..."
            value={search}
            onChange={e => setSearch(e.target.value)}
            className="w-full pl-8 pr-3 py-1.5 rounded-md bg-[var(--color-bg-input)] border border-[var(--color-border)] text-[var(--color-text)] text-xs placeholder:text-[var(--color-text-muted)]"
          />
        </div>
      </div>

      <nav className="flex-1 overflow-y-auto py-2">
        {active.length > 0 && (
          <SpecGroup
            label="In Progress"
            specs={active}
            projectId={projectId}
            currentSpecId={specId}
            documentSections={documentSections}
            activeSectionId={activeSectionId}
            onActivateSection={setActiveSectionId}
          />
        )}
        {completed.length > 0 && (
          <SpecGroup
            label="Completed"
            specs={completed}
            projectId={projectId}
            currentSpecId={specId}
            documentSections={documentSections}
            activeSectionId={activeSectionId}
            onActivateSection={setActiveSectionId}
          />
        )}
        {drafts.length > 0 && (
          <SpecGroup
            label="Drafts"
            specs={drafts}
            projectId={projectId}
            currentSpecId={specId}
            documentSections={documentSections}
            activeSectionId={activeSectionId}
            onActivateSection={setActiveSectionId}
          />
        )}
        {failed.length > 0 && (
          <SpecGroup
            label="Failed / Archived"
            specs={failed}
            projectId={projectId}
            currentSpecId={specId}
            documentSections={documentSections}
            activeSectionId={activeSectionId}
            onActivateSection={setActiveSectionId}
          />
        )}
        {!filtered?.length && (
          <div className="px-3 py-6 text-center">
            <FileText className="w-6 h-6 mx-auto mb-2 text-[var(--color-text-muted)]" />
            <p className="text-xs text-[var(--color-text-muted)]">No specs</p>
          </div>
        )}
      </nav>
    </aside>
  )
}

function SpecGroup({ label, specs, projectId, currentSpecId, documentSections, activeSectionId, onActivateSection }: {
  label: string
  specs: { id: string; title: string; state: FlowState }[]
  projectId: string
  currentSpecId?: string
  documentSections: DocumentSection[]
  activeSectionId: string
  onActivateSection: (sectionId: string) => void
}) {
  return (
    <div className="mb-2">
      <div className="px-3 py-1 text-[10px] font-semibold uppercase tracking-wider text-[var(--color-text-muted)]">
        {label}
      </div>
      {specs.map((spec) => {
        const isCurrent = spec.id === currentSpecId

        return (
          <div key={spec.id}>
            <Link
              to={`/projects/${projectId}/specs/${spec.id}`}
              className={`flex items-center gap-2 px-3 py-1.5 text-xs transition-colors ${
                isCurrent
                  ? 'bg-[var(--color-primary)]/10 text-[var(--color-primary)] border-r-2 border-[var(--color-primary)]'
                  : 'text-[var(--color-text-muted)] hover:bg-[var(--color-bg-card)] hover:text-[var(--color-text)]'
              }`}
            >
              <Circle className={`w-2 h-2 fill-current ${stateIndicator[spec.state] ?? 'text-gray-500'}`} />
              <span className="font-mono text-[10px] flex-shrink-0 opacity-60">{spec.id}</span>
              <span className="truncate">{spec.title}</span>
              {isCurrent && documentSections.length > 0 && (
                <BookOpenText className="ml-auto h-3.5 w-3.5 opacity-80" />
              )}
            </Link>

            {isCurrent && documentSections.length > 0 && (
              <div className="mb-2 mt-1 space-y-0.5 pl-6">
                {documentSections.map((section) => (
                  <a
                    key={section.id}
                    href={`#${section.id}`}
                    onClick={() => onActivateSection(section.id)}
                    aria-current={activeSectionId === section.id ? 'location' : undefined}
                    className={`flex items-center gap-2 rounded-l-md px-3 py-1.5 text-xs transition-colors ${
                      activeSectionId === section.id
                        ? 'bg-[var(--color-primary)]/10 text-[var(--color-primary)] border-r-2 border-[var(--color-primary)]'
                        : 'text-[var(--color-text-muted)] hover:bg-[var(--color-bg-card)] hover:text-[var(--color-text)]'
                    }`}
                  >
                    <span className={`h-1.5 w-1.5 rounded-full ${activeSectionId === section.id ? 'bg-[var(--color-primary)]' : 'bg-[var(--color-primary)]/70'}`} />
                    <span className="flex-1">{section.label}</span>
                    {section.badge && (
                      <span className={`rounded-full px-1.5 py-0.5 text-[10px] ${activeSectionId === section.id ? 'bg-[var(--color-primary)] text-white' : 'bg-[var(--color-bg-elevated)] text-[var(--color-text-muted)]'}`}>
                        {section.badge}
                      </span>
                    )}
                  </a>
                ))}
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}
