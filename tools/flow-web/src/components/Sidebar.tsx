import { useEffect, useState } from 'react'
import { Link, useParams, useLocation } from 'react-router-dom'
import { Search, FileText, Circle, BookOpenText, Layers, ArrowRight, Link2, Flame } from 'lucide-react'
import { useActivity, useAssignments, useEvidence, useReviewRequests, useSpec, useSpecs, useSpecEpic, useEpicView, useProjectView, useEpics } from '@/hooks/useSpecs'
import type { FlowState, EpicSummary } from '@/types/flow'

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

// ── Shared search bar ──

function SearchBar({ value, onChange, placeholder }: { value: string; onChange: (v: string) => void; placeholder: string }) {
  return (
    <div className="p-3 border-b border-[var(--color-border)]">
      <div className="relative">
        <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-[var(--color-text-muted)]" />
        <input
          type="text"
          placeholder={placeholder}
          value={value}
          onChange={e => onChange(e.target.value)}
          className="w-full pl-8 pr-3 py-1.5 rounded-md bg-[var(--color-bg-input)] border border-[var(--color-border)] text-[var(--color-text)] text-xs placeholder:text-[var(--color-text-muted)]"
        />
      </div>
    </div>
  )
}

// ── Project sidebar ──

function ProjectSidebar({ projectId }: { projectId: string }) {
  const [search, setSearch] = useState('')
  const { data: view } = useProjectView(projectId)
  const { data: epics } = useEpics(projectId)

  const hotspotCount = view ? view.hotspots.review.length + view.hotspots.failure.length + view.hotspots.onHold.length : 0

  const sections = [
    { id: 'epics', label: 'Epics', badge: view?.stats.epicCount },
    { id: 'hotspots', label: 'Hotspots', badge: hotspotCount > 0 ? hotspotCount : undefined },
    { id: 'document', label: 'Project Document' },
  ]

  const filteredEpics = epics?.filter(e =>
    !search || e.title.toLowerCase().includes(search.toLowerCase()) || e.epicId.toLowerCase().includes(search.toLowerCase())
  )

  return (
    <>
      <SearchBar value={search} onChange={setSearch} placeholder="Search epics..." />
      <nav className="flex-1 overflow-y-auto py-2">
        {/* Page section anchors */}
        <div className="mb-3">
          <div className="px-3 py-1 text-[10px] font-semibold uppercase tracking-wider text-[var(--color-text-muted)]">
            Sections
          </div>
          {sections.map((s) => (
            <a
              key={s.id}
              href={`#${s.id}`}
              className="flex items-center gap-2 px-3 py-1.5 text-xs text-[var(--color-text-muted)] hover:bg-[var(--color-bg-card)] hover:text-[var(--color-text)] transition-colors"
            >
              <span className="h-1.5 w-1.5 rounded-full bg-[var(--color-primary)]/70" />
              <span className="flex-1">{s.label}</span>
              {s.badge != null && (
                <span className="rounded-full px-1.5 py-0.5 text-[10px] bg-[var(--color-bg-elevated)] text-[var(--color-text-muted)]">
                  {s.badge}
                </span>
              )}
            </a>
          ))}
        </div>

        {/* Epic list */}
        <div className="mb-2">
          <div className="px-3 py-1 text-[10px] font-semibold uppercase tracking-wider text-[var(--color-text-muted)] flex items-center gap-1.5">
            <Layers className="w-3 h-3" />
            Epics
          </div>
          {filteredEpics?.map((epic) => (
            <EpicNavItem key={epic.epicId} epic={epic} projectId={projectId} />
          ))}
          {filteredEpics?.length === 0 && (
            <div className="px-3 py-4 text-center">
              <p className="text-xs text-[var(--color-text-muted)]">No epics</p>
            </div>
          )}
        </div>

        {/* Hotspots */}
        {view && (view.hotspots.review.length > 0 || view.hotspots.failure.length > 0 || view.hotspots.onHold.length > 0) && (
          <div className="mb-2">
            <div className="px-3 py-1 text-[10px] font-semibold uppercase tracking-wider text-[var(--color-text-muted)] flex items-center gap-1.5">
              <Flame className="w-3 h-3 text-red-400" />
              Hotspots
            </div>
            {view.hotspots.review.map((h) => (
              <Link
                key={`r-${h.specId}`}
                to={`/projects/${projectId}/specs/${h.specId}`}
                className="flex items-center gap-2 px-3 py-1.5 text-xs text-[var(--color-text-muted)] hover:bg-[var(--color-bg-card)] hover:text-[var(--color-text)] transition-colors"
              >
                <Circle className="w-2 h-2 fill-current text-yellow-400" />
                <span className="font-mono text-[10px] flex-shrink-0 opacity-60">{h.specId}</span>
                <span className="truncate flex-1">{h.title}</span>
              </Link>
            ))}
            {view.hotspots.failure.map((h) => (
              <Link
                key={`f-${h.specId}`}
                to={`/projects/${projectId}/specs/${h.specId}`}
                className="flex items-center gap-2 px-3 py-1.5 text-xs text-[var(--color-text-muted)] hover:bg-[var(--color-bg-card)] hover:text-[var(--color-text)] transition-colors"
              >
                <Circle className="w-2 h-2 fill-current text-red-400" />
                <span className="font-mono text-[10px] flex-shrink-0 opacity-60">{h.specId}</span>
                <span className="truncate flex-1">{h.title}</span>
              </Link>
            ))}
            {view.hotspots.onHold.map((h) => (
              <Link
                key={`h-${h.specId}`}
                to={`/projects/${projectId}/specs/${h.specId}`}
                className="flex items-center gap-2 px-3 py-1.5 text-xs text-[var(--color-text-muted)] hover:bg-[var(--color-bg-card)] hover:text-[var(--color-text)] transition-colors"
              >
                <Circle className="w-2 h-2 fill-current text-orange-400" />
                <span className="font-mono text-[10px] flex-shrink-0 opacity-60">{h.specId}</span>
                <span className="truncate flex-1">{h.title}</span>
              </Link>
            ))}
          </div>
        )}

        {/* Quick links */}
        <div className="mb-2">
          <div className="px-3 py-1 text-[10px] font-semibold uppercase tracking-wider text-[var(--color-text-muted)]">
            Quick Links
          </div>
          <Link
            to={`/projects/${projectId}/specs`}
            className="flex items-center gap-2 px-3 py-1.5 text-xs text-[var(--color-text-muted)] hover:bg-[var(--color-bg-card)] hover:text-[var(--color-text)] transition-colors"
          >
            <FileText className="w-3 h-3" />
            <span className="flex-1">All Specs</span>
            <ArrowRight className="w-3 h-3" />
          </Link>
        </div>
      </nav>
    </>
  )
}

function EpicNavItem({ epic, projectId }: { epic: EpicSummary; projectId: string }) {
  const total = epic.specCounts.total
  const completed = epic.specCounts.completed
  const pct = total > 0 ? Math.round((completed / total) * 100) : 0

  return (
    <Link
      to={`/projects/${projectId}/epics/${epic.epicId}`}
      className="flex items-center gap-2 px-3 py-1.5 text-xs text-[var(--color-text-muted)] hover:bg-[var(--color-bg-card)] hover:text-[var(--color-text)] transition-colors"
    >
      <Circle className={`w-2 h-2 fill-current ${
        pct === 100 ? 'text-green-400' :
        epic.specCounts.active > 0 ? 'text-blue-400' :
        'text-[var(--color-text-muted)]'
      }`} />
      <span className="font-mono text-[10px] flex-shrink-0 opacity-60">{epic.epicId}</span>
      <span className="truncate flex-1">{epic.title}</span>
      <span className="text-[10px] opacity-60">{pct}%</span>
    </Link>
  )
}

// ── Epic sidebar ──

function EpicSidebar({ projectId, epicId }: { projectId: string; epicId: string }) {
  const [search, setSearch] = useState('')
  const { data: epicView } = useEpicView(projectId, epicId)

  const sections = [
    { id: 'epic-narrative', label: 'Narrative' },
    { id: 'epic-child-specs', label: 'Child Specs', badge: epicView?.childSpecs.length },
    { id: 'epic-dependencies', label: 'Dependencies & Docs' },
  ]

  const filteredSpecs = epicView?.childSpecs.filter(s =>
    !search || s.title.toLowerCase().includes(search.toLowerCase()) || s.specId.toLowerCase().includes(search.toLowerCase())
  )

  return (
    <>
      {/* Epic summary header */}
      {epicView && (
        <div className="p-3 border-b border-[var(--color-border)]">
          <div className="text-xs font-medium text-[var(--color-text-bright)] truncate">{epicView.title}</div>
          <div className="flex items-center gap-2 mt-1 text-[10px] text-[var(--color-text-muted)]">
            <span>{epicView.progress.completedSpecs}/{epicView.progress.totalSpecs} specs</span>
            <span>{Math.round(epicView.progress.completionRatio * 100)}%</span>
          </div>
          <div className="mt-1.5 h-1 rounded-full bg-[var(--color-bg-input)] overflow-hidden">
            <div
              className="h-full rounded-full bg-[var(--color-primary)] transition-all"
              style={{ width: `${Math.round(epicView.progress.completionRatio * 100)}%` }}
            />
          </div>
        </div>
      )}

      <SearchBar value={search} onChange={setSearch} placeholder="Search specs..." />

      <nav className="flex-1 overflow-y-auto py-2">
        {/* Page section anchors */}
        <div className="mb-3">
          <div className="px-3 py-1 text-[10px] font-semibold uppercase tracking-wider text-[var(--color-text-muted)]">
            Sections
          </div>
          {sections.map((s) => (
            <a
              key={s.id}
              href={`#${s.id}`}
              className="flex items-center gap-2 px-3 py-1.5 text-xs text-[var(--color-text-muted)] hover:bg-[var(--color-bg-card)] hover:text-[var(--color-text)] transition-colors"
            >
              <span className="h-1.5 w-1.5 rounded-full bg-[var(--color-primary)]/70" />
              <span className="flex-1">{s.label}</span>
              {s.badge != null && (
                <span className="rounded-full px-1.5 py-0.5 text-[10px] bg-[var(--color-bg-elevated)] text-[var(--color-text-muted)]">
                  {s.badge}
                </span>
              )}
            </a>
          ))}
        </div>

        {/* Child spec list */}
        <div className="mb-2">
          <div className="px-3 py-1 text-[10px] font-semibold uppercase tracking-wider text-[var(--color-text-muted)] flex items-center gap-1.5">
            <FileText className="w-3 h-3" />
            Child Specs
          </div>
          {filteredSpecs?.map((spec) => (
            <Link
              key={spec.specId}
              to={`/projects/${projectId}/specs/${spec.specId}`}
              className="flex items-center gap-2 px-3 py-1.5 text-xs text-[var(--color-text-muted)] hover:bg-[var(--color-bg-card)] hover:text-[var(--color-text)] transition-colors"
            >
              <Circle className={`w-2 h-2 fill-current ${stateIndicator[spec.state] ?? 'text-gray-500'}`} />
              <span className="font-mono text-[10px] flex-shrink-0 opacity-60">{spec.specId}</span>
              <span className="truncate">{spec.title}</span>
            </Link>
          ))}
          {filteredSpecs?.length === 0 && (
            <div className="px-3 py-4 text-center">
              <p className="text-xs text-[var(--color-text-muted)]">No specs</p>
            </div>
          )}
        </div>

        {/* Related docs */}
        {epicView && epicView.relatedDocs.length > 0 && (
          <div className="mb-2">
            <div className="px-3 py-1 text-[10px] font-semibold uppercase tracking-wider text-[var(--color-text-muted)] flex items-center gap-1.5">
              <Link2 className="w-3 h-3" />
              Related Docs
            </div>
            {epicView.relatedDocs.map((doc) => (
              <div
                key={doc}
                className="flex items-center gap-2 px-3 py-1.5 text-xs text-[var(--color-text-muted)]"
              >
                <span className="truncate">{doc}</span>
              </div>
            ))}
          </div>
        )}
      </nav>
    </>
  )
}

// ── Spec sidebar (existing behavior) ──

function EpicContextBlock({ projectId, specId }: { projectId: string; specId: string }) {
  const epicInfo = useSpecEpic(projectId, specId)
  const { data: epicView } = useEpicView(projectId, epicInfo?.epicId ?? '')

  if (!epicInfo) return null

  const progress = epicView?.progress
  const pct = progress ? Math.round(progress.completionRatio * 100) : null

  return (
    <div className="p-3 border-b border-[var(--color-border)]">
      <Link
        to={`/projects/${projectId}/epics/${epicInfo.epicId}`}
        className="block rounded-lg p-2.5 bg-[var(--color-bg-card)] border border-[var(--color-border)] hover:border-[var(--color-primary)]/50 transition-colors"
      >
        <div className="flex items-center gap-2 mb-1">
          <Layers className="w-3.5 h-3.5 text-[var(--color-primary)]" />
          <span className="text-[10px] font-mono text-[var(--color-text-muted)]">{epicInfo.epicId}</span>
        </div>
        <div className="text-xs font-medium text-[var(--color-text-bright)] truncate">{epicInfo.title}</div>
        {progress && pct !== null && (
          <div className="mt-2">
            <div className="flex items-center justify-between text-[10px] text-[var(--color-text-muted)] mb-1">
              <span>{progress.completedSpecs}/{progress.totalSpecs} specs</span>
              <span>{pct}%</span>
            </div>
            <div className="h-1 rounded-full bg-[var(--color-bg-input)] overflow-hidden">
              <div
                className="h-full rounded-full bg-[var(--color-primary)] transition-all"
                style={{ width: `${pct}%` }}
              />
            </div>
          </div>
        )}
      </Link>
    </div>
  )
}

function SiblingSpecs({ projectId, specId }: { projectId: string; specId: string }) {
  const epicInfo = useSpecEpic(projectId, specId)
  const { data: epicView } = useEpicView(projectId, epicInfo?.epicId ?? '')

  if (!epicInfo || !epicView || epicView.childSpecs.length <= 1) return null

  const siblings = epicView.childSpecs.filter(s => s.specId !== specId)

  return (
    <div className="mb-2">
      <div className="px-3 py-1 text-[10px] font-semibold uppercase tracking-wider text-[var(--color-text-muted)] flex items-center gap-1.5">
        <Layers className="w-3 h-3" />
        Same Epic
      </div>
      {siblings.map((spec) => (
        <Link
          key={spec.specId}
          to={`/projects/${projectId}/specs/${spec.specId}`}
          className="flex items-center gap-2 px-3 py-1.5 text-xs transition-colors text-[var(--color-text-muted)] hover:bg-[var(--color-bg-card)] hover:text-[var(--color-text)]"
        >
          <Circle className={`w-2 h-2 fill-current ${stateIndicator[spec.state] ?? 'text-gray-500'}`} />
          <span className="font-mono text-[10px] flex-shrink-0 opacity-60">{spec.specId}</span>
          <span className="truncate">{spec.title}</span>
        </Link>
      ))}
    </div>
  )
}

function SpecSidebar({ projectId, specId }: { projectId: string; specId: string }) {
  const [search, setSearch] = useState('')
  const [activeSectionId, setActiveSectionId] = useState<string>('section-document-summary')
  const { data: specs } = useSpecs(projectId, undefined)
  const { data: currentSpecDetail } = useSpec(projectId, specId)
  const { data: currentReviewRequests } = useReviewRequests(projectId, specId)
  const { data: currentAssignments } = useAssignments(projectId, specId)
  const { data: currentEvidence } = useEvidence(projectId, specId)
  const { data: currentActivity } = useActivity(projectId, specId)

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
    <>
      <EpicContextBlock projectId={projectId} specId={specId} />

      <SearchBar value={search} onChange={setSearch} placeholder="Search specs..." />

      <nav className="flex-1 overflow-y-auto py-2">
        <SiblingSpecs projectId={projectId} specId={specId} />
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
    </>
  )
}

// ── Main Sidebar (route-aware dispatcher) ──

export function Sidebar() {
  const { projectId, specId, epicId } = useParams<{ projectId: string; specId: string; epicId: string }>()
  const location = useLocation()

  if (!projectId) return null

  // Determine sidebar mode based on route
  const isSpecView = !!specId
  const isEpicView = !!epicId && !specId
  const isSpecsList = !specId && !epicId && location.pathname.endsWith('/specs')
  // Project view: /projects/:projectId (not specs list, not epic, not spec)
  const isProjectView = !specId && !epicId && !isSpecsList

  return (
    <aside className="w-60 flex-shrink-0 border-r border-[var(--color-border)] bg-[var(--color-bg)] flex flex-col h-full overflow-hidden">
      {isSpecView && <SpecSidebar projectId={projectId} specId={specId} />}
      {isEpicView && <EpicSidebar projectId={projectId} epicId={epicId} />}
      {(isProjectView || isSpecsList) && <ProjectSidebar projectId={projectId} />}
    </aside>
  )
}

// ── Spec group (used by SpecSidebar) ──

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
