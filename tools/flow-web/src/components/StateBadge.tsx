import type { FlowState, ProcessingStatus } from '@/types/flow'

const stateColors: Record<string, string> = {
  draft: 'bg-gray-600',
  queued: 'bg-blue-800',
  architectureReview: 'bg-purple-800',
  testGeneration: 'bg-indigo-800',
  implementation: 'bg-blue-700',
  review: 'bg-yellow-800',
  active: 'bg-green-700',
  failed: 'bg-red-800',
  completed: 'bg-green-800',
  archived: 'bg-gray-700',
}

const statusColors: Record<string, string> = {
  pending: 'bg-gray-500',
  inProgress: 'bg-blue-600',
  inReview: 'bg-yellow-600',
  userReview: 'bg-orange-600',
  done: 'bg-green-600',
  error: 'bg-red-600',
  onHold: 'bg-gray-600',
}

const stateLabels: Record<string, string> = {
  draft: 'Draft',
  queued: 'Queued',
  architectureReview: 'Arch Review',
  testGeneration: 'Test Gen',
  implementation: 'Implementation',
  review: 'Review',
  active: 'Active',
  failed: 'Failed',
  completed: 'Completed',
  archived: 'Archived',
}

const statusLabels: Record<string, string> = {
  pending: 'Pending',
  inProgress: 'In Progress',
  inReview: 'In Review',
  userReview: 'User Review',
  done: 'Done',
  error: 'Error',
  onHold: 'On Hold',
}

export function StateBadge({ state }: { state: FlowState }) {
  return (
    <span className={`inline-block rounded-full px-2.5 py-0.5 text-xs font-medium text-white ${stateColors[state] ?? 'bg-gray-600'}`}>
      {stateLabels[state] ?? state}
    </span>
  )
}

export function StatusBadge({ status }: { status: ProcessingStatus }) {
  return (
    <span className={`inline-block rounded-full px-2.5 py-0.5 text-xs font-medium text-white ${statusColors[status] ?? 'bg-gray-500'}`}>
      {statusLabels[status] ?? status}
    </span>
  )
}
