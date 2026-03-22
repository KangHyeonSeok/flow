import { useEffect, useState, type ReactNode } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'

export interface CellProps {
  title: string
  icon?: ReactNode
  accentColor: string
  sectionId?: string
  defaultCollapsed?: boolean
  badge?: ReactNode
  children: ReactNode
}

export function Cell({ title, icon, accentColor, sectionId, defaultCollapsed = false, badge, children }: CellProps) {
  const [collapsed, setCollapsed] = useState(defaultCollapsed)

  useEffect(() => {
    if (!sectionId) return

    const syncWithHash = () => {
      if (window.location.hash === `#${sectionId}`) {
        setCollapsed(false)
      }
    }

    syncWithHash()
    window.addEventListener('hashchange', syncWithHash)

    return () => window.removeEventListener('hashchange', syncWithHash)
  }, [sectionId])

  return (
    <section id={sectionId} className="scroll-mt-6 rounded-lg bg-[var(--color-bg-card)] border border-[var(--color-border)] overflow-hidden">
      <button
        onClick={() => setCollapsed(!collapsed)}
        className="w-full flex items-center gap-3 px-4 py-3 text-left hover:bg-[var(--color-bg-card-hover)] transition-colors"
      >
        <div className="w-1 h-6 rounded-full flex-shrink-0" style={{ background: accentColor }} />
        {icon && <span className="flex-shrink-0" style={{ color: accentColor }}>{icon}</span>}
        <span className="text-sm font-semibold text-[var(--color-text-bright)] flex-1">{title}</span>
        {badge}
        {collapsed
          ? <ChevronRight className="w-4 h-4 text-[var(--color-text-muted)]" />
          : <ChevronDown className="w-4 h-4 text-[var(--color-text-muted)]" />
        }
      </button>
      {!collapsed && (
        <div className="px-4 pb-4 border-t border-[var(--color-border-subtle)]">
          <div className="pt-3">
            {children}
          </div>
        </div>
      )}
    </section>
  )
}
