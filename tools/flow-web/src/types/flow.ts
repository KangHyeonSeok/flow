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
  epicId?: string
  title: string
  type: SpecType
  problem?: string
  goal?: string
  context?: string
  nonGoals?: string
  implementationNotes?: string
  testPlan?: string
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

// ── Project / Epic view models ──

export interface ProjectMilestone {
  id: string
  title: string
  status: string
}

export interface ProjectDocument {
  projectId: string
  version: number
  title: string
  summary?: string
  problem?: string
  goals: string[]
  nonGoals: string[]
  contextAndConstraints: string[]
  architectureOverview: string[]
  milestones: ProjectMilestone[]
  relatedDocs: string[]
  createdAt: string
  updatedAt: string
}

export interface ProjectStats {
  specCount: number
  epicCount: number
  activeEpicCount: number
  openReviewCount: number
  failedSpecCount: number
  onHoldSpecCount: number
}

export interface ProjectDocumentSection {
  problem?: string
  goals: string[]
  nonGoals: string[]
  contextAndConstraints: string[]
  architectureOverview: string[]
}

export interface EpicSpecCounts {
  total: number
  completed: number
  active: number
  blocked: number
  review: number
}

export interface EpicSummary {
  epicId: string
  title: string
  summary?: string
  priority?: string
  milestone?: string
  owner?: string
  specCounts: EpicSpecCounts
}

export interface HotspotEntry {
  specId: string
  title: string
  epicId?: string
  reason: string
}

export interface ProjectHotspots {
  review: HotspotEntry[]
  failure: HotspotEntry[]
  onHold: HotspotEntry[]
}

export interface ProjectView {
  projectId: string
  title: string
  summary?: string
  documentVersion: number
  lastActivityAt?: string
  stats: ProjectStats
  document: ProjectDocumentSection
  epics: EpicSummary[]
  hotspots: ProjectHotspots
}

export interface EpicMilestone {
  id: string
  title: string
  status: string
}

export interface EpicDocument {
  projectId: string
  epicId: string
  version: number
  title: string
  summary?: string
  problem?: string
  goal?: string
  scope: string[]
  nonGoals: string[]
  successCriteria: string[]
  childSpecIds: string[]
  dependencies: string[]
  milestones: EpicMilestone[]
  relatedDocs: string[]
  owner?: string
  priority?: string
  createdAt: string
  updatedAt: string
}

export interface EpicProgress {
  totalSpecs: number
  completedSpecs: number
  activeSpecs: number
  blockedSpecs: number
  completionRatio: number
}

export interface EpicNarrative {
  problem?: string
  goal?: string
  scope: string[]
  nonGoals: string[]
  successCriteria: string[]
}

export interface EpicChildSpec {
  specId: string
  title: string
  state: FlowState
  processingStatus: ProcessingStatus
  riskLevel: RiskLevel
  lastActivityAt?: string
}

export interface EpicView {
  projectId: string
  epicId: string
  title: string
  summary?: string
  documentVersion: number
  priority?: string
  owner?: string
  milestone?: string
  progress: EpicProgress
  narrative: EpicNarrative
  childSpecs: EpicChildSpec[]
  epicDependsOn: string[]
  relatedDocs: string[]
}

// Request types
export interface CreateSpecRequest {
  title: string
  type?: string
  epicId?: string
  problem?: string
  goal?: string
  context?: string
  nonGoals?: string
  implementationNotes?: string
  testPlan?: string
  acceptanceCriteria?: { text: string; testable?: boolean; notes?: string }[]
  riskLevel?: string
}

export interface UpdateSpecRequest {
  version: number
  epicId?: string
  title?: string
  problem?: string
  goal?: string
  context?: string
  nonGoals?: string
  implementationNotes?: string
  testPlan?: string
  acceptanceCriteria?: { text: string; testable?: boolean; notes?: string }[]
  riskLevel?: string
}

export interface UpdateProjectDocumentRequest {
  version: number
  title?: string
  summary?: string
  problem?: string
  goals?: string[]
  nonGoals?: string[]
  contextAndConstraints?: string[]
  architectureOverview?: string[]
}

export interface UpdateEpicDocumentRequest {
  version: number
  title?: string
  summary?: string
  problem?: string
  goal?: string
  scope?: string[]
  nonGoals?: string[]
  successCriteria?: string[]
  childSpecIds?: string[]
  dependencies?: string[]
  relatedDocs?: string[]
  owner?: string
  priority?: string
}
