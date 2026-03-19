import { Outlet, Link, useParams } from 'react-router-dom'
import { Activity } from 'lucide-react'

export function Layout() {
  const { projectId } = useParams()

  return (
    <div className="min-h-screen flex flex-col">
      <header className="border-b border-[var(--color-border)] px-6 py-3 flex items-center gap-4">
        <Link to="/" className="flex items-center gap-2 text-lg font-semibold text-[var(--color-text)]">
          <Activity className="w-5 h-5 text-[var(--color-primary)]" />
          Flow
        </Link>
        {projectId && (
          <>
            <span className="text-[var(--color-text-muted)]">/</span>
            <Link to={`/projects/${projectId}`} className="text-sm text-[var(--color-text-muted)] hover:text-[var(--color-text)]">
              {projectId}
            </Link>
          </>
        )}
      </header>
      <main className="flex-1 p-6">
        <Outlet />
      </main>
    </div>
  )
}
