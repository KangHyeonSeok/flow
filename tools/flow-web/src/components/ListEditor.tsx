import { X } from 'lucide-react'

export function ListEditor({ label, items, onChange }: { label: string; items: string[]; onChange: (items: string[]) => void }) {
  return (
    <div>
      <h4 className="text-xs font-semibold text-[var(--color-text-muted)] uppercase tracking-wide mb-1">{label}</h4>
      <div className="space-y-1">
        {items.map((item, i) => (
          <div key={i} className="flex gap-1">
            <input
              type="text"
              value={item}
              onChange={(e) => { const next = [...items]; next[i] = e.target.value; onChange(next) }}
              className="flex-1 text-sm px-2 py-1 rounded bg-[var(--color-bg-input)] border border-[var(--color-border)] text-[var(--color-text)] focus:outline-none focus:border-[var(--color-primary)]"
            />
            <button
              onClick={() => onChange(items.filter((_, j) => j !== i))}
              className="px-1.5 text-[var(--color-text-muted)] hover:text-red-400"
              title="Remove"
            >
              <X className="w-3 h-3" />
            </button>
          </div>
        ))}
        <button
          onClick={() => onChange([...items, ''])}
          className="text-xs text-[var(--color-primary)] hover:underline"
        >
          + Add item
        </button>
      </div>
    </div>
  )
}
