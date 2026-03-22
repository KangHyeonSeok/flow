import { Outlet, Link, useParams, useLocation } from 'react-router-dom'
import { Activity, ChevronRight } from 'lucide-react'
import { Sidebar } from '@/components/Sidebar'

export function Layout() {
  const { projectId, specId } = useParams()
  const location = useLocation()

  const isSpecsList = projectId && !specId && location.pathname.endsWith('/specs')

  return (
    <div className="h-screen flex flex-col">
      <header className="border-b border-[var(--color-border)] px-4 py-2.5 flex items-center gap-2 bg-[var(--color-bg)] flex-shrink-0">
        <Link to="/" className="flex items-center gap-2 text-sm font-semibold text-[var(--color-text-bright)]">
          <Activity className="w-4 h-4 text-[var(--color-primary)]" />
          Flow
        </Link>
        {projectId && (
          <>
            <ChevronRight className="w-3 h-3 text-[var(--color-text-muted)]" />
            <Link to={`/projects/${projectId}`} className="text-xs text-[var(--color-text-muted)] hover:text-[var(--color-text)]">
              {projectId}
            </Link>
          </>
        )}
        {isSpecsList && (
          <>
            <ChevronRight className="w-3 h-3 text-[var(--color-text-muted)]" />
            <span className="text-xs text-[var(--color-text-muted)]">Specs</span>
          </>
        )}
        {specId && (
          <>
            <ChevronRight className="w-3 h-3 text-[var(--color-text-muted)]" />
            <Link to={`/projects/${projectId}/specs`} className="text-xs text-[var(--color-text-muted)] hover:text-[var(--color-text)]">
              Specs
            </Link>
            <ChevronRight className="w-3 h-3 text-[var(--color-text-muted)]" />
            <span className="text-xs font-mono text-[var(--color-text-muted)]">{specId}</span>
          </>
        )}
      </header>
      <div className="flex flex-1 overflow-hidden">
        {projectId && specId && <Sidebar />}
        <main data-spec-scroll-root className="flex-1 overflow-y-auto p-6 scroll-smooth">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
