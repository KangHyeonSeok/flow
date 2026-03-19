// Flow domain types — mirrors flow-core models

export type FlowState =
  | 'draft'
  | 'queued'
  | 'architectureReview'
  | 'testGeneration'
  | 'implementation'
  | 'review'
  | 'active'
  | 'failed'
  | 'completed'
  | 'archived'

export type ProcessingStatus =
  | 'pending'
  | 'inProgress'
  | 'inReview'
  | 'userReview'
  | 'done'
  | 'error'
  | 'onHold'

export type SpecType = 'feature' | 'task'
export type RiskLevel = 'low' | 'medium' | 'high' | 'critical'
export type AssignmentStatus = 'queued' | 'running' | 'completed' | 'failed' | 'cancelled'

export interface AcceptanceCriterion {
  id: string
  text: string
  testable: boolean
  notes?: string
}

export interface Spec {
  id: string
  projectId: string
  title: string
  type: SpecType
  problem?: string
  goal?: string
  state: FlowState
  processingStatus: ProcessingStatus
  riskLevel: RiskLevel
  acceptanceCriteria: AcceptanceCriterion[]
  version: number
  createdAt: string
  updatedAt: string
}

export interface Assignment {
  id: string
  specId: string
  agentRole: string
  type: string
  status: AssignmentStatus
  startedAt?: string
  finishedAt?: string
  resultSummary?: string
}

export interface ReviewRequest {
  id: string
  specId: string
  status: string
  createdAt?: string
  reason?: string
  summary?: string
  questions?: string[]
  options?: ReviewRequestOption[]
  response?: ReviewResponse
}

export interface ReviewRequestOption {
  id: string
  label: string
  description?: string
}

export interface ReviewResponse {
  respondedBy: string
  respondedAt: string
  type: string
  selectedOptionId?: string
  comment?: string
}

export interface ActivityEvent {
  eventId: string
  timestamp: string
  specId: string
  actor: string
  action: string
  message: string
  state: FlowState
  processingStatus: ProcessingStatus
}

export interface EvidenceManifest {
  specId: string
  runId: string
  createdAt: string
  refs: EvidenceRef[]
}

export interface EvidenceRef {
  kind: string
  relativePath: string
  summary?: string
}

// Request types
export interface CreateSpecRequest {
  title: string
  type?: string
  problem?: string
  goal?: string
  acceptanceCriteria?: { text: string; testable?: boolean; notes?: string }[]
  riskLevel?: string
}

export interface UpdateSpecRequest {
  version: number
  title?: string
  problem?: string
  goal?: string
  acceptanceCriteria?: { text: string; testable?: boolean; notes?: string }[]
  riskLevel?: string
}
