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
export type TestStatus = 'notRun' | 'passed' | 'failed' | 'skipped'
export type TestType = 'unit' | 'e2E' | 'user'

export interface AcceptanceCriterion {
  id: string
  text: string
  testable: boolean
  notes?: string
  relatedTestIds?: string[]
}

export interface TestDefinition {
  id: string
  type: TestType
  title?: string
  acIds: string[]
  status: TestStatus
}

export interface Dependency {
  dependsOn: string[]
  blocks: string[]
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
  dependencies?: Dependency
  tests?: TestDefinition[]
  testIds?: string[]
  assignments?: string[]
  reviewRequestIds?: string[]
  retryCounters?: Record<string, number>
  derivedFrom?: string
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
  lastHeartbeatAt?: string
  timeoutSeconds?: number
  finishedAt?: string
  resultSummary?: string
  cancelReason?: string
  worktree?: { id: string; path: string; branch?: string }
}

export interface ReviewRequest {
  id: string
  specId: string
  createdBy?: string
  status: string
  createdAt?: string
  reason?: string
  summary?: string
  questions?: string[]
  options?: ReviewRequestOption[]
  response?: ReviewResponse
  resolution?: string
  deadlineAt?: string
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
  sourceType?: string
  baseVersion?: number
  message: string
  state: FlowState
  processingStatus: ProcessingStatus
  assignmentId?: string
  reviewRequestId?: string
  correlationId?: string
  payload?: unknown
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
