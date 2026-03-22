import { Link } from 'react-router-dom'
import { FolderOpen } from 'lucide-react'
import { useProjects } from '@/hooks/useSpecs'

export function ProjectsPage() {
  const { data: projects, isLoading, error } = useProjects()

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="w-6 h-6 border-2 border-[var(--color-primary)] border-t-transparent rounded-full animate-spin" />
      </div>
    )
  }
  if (error) return <p className="text-[var(--color-danger)]">Failed to load projects</p>

  if (!projects?.length) {
    return (
      <div className="text-center py-20">
        <FolderOpen className="w-12 h-12 mx-auto mb-4 text-[var(--color-text-muted)]" />
        <p className="text-[var(--color-text-muted)]">No projects found</p>
        <p className="text-sm text-[var(--color-text-muted)] mt-1">
          Create a project via CLI: <code className="bg-[var(--color-bg-card)] px-1.5 py-0.5 rounded text-xs">flow spec create --project my-project</code>
        </p>
      </div>
    )
  }

  return (
    <div>
      <h1 className="text-xl font-bold mb-6 text-[var(--color-text-bright)]">Projects</h1>
      <div className="grid gap-3 max-w-xl">
        {projects.map((p) => (
          <Link
            key={p}
            to={`/projects/${p}`}
            className="flex items-center gap-3 p-4 rounded-lg bg-[var(--color-bg-card)] hover:bg-[var(--color-bg-card-hover)] border border-[var(--color-border)] transition-colors"
          >
            <FolderOpen className="w-5 h-5 text-[var(--color-primary)]" />
            <span className="font-medium">{p}</span>
          </Link>
        ))}
      </div>
    </div>
  )
}
