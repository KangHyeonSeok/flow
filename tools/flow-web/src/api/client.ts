import type {
  Spec,
  Assignment,
  ReviewRequest,
  ActivityEvent,
  EvidenceManifest,
  CreateSpecRequest,
  UpdateSpecRequest,
} from '@/types/flow'

const BASE = '/api'

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${url}`, {
    headers: { 'Content-Type': 'application/json' },
    ...init,
  })
  if (!res.ok) {
    const body = await res.json().catch(() => ({}))
    throw new ApiError(res.status, body.error ?? res.statusText, body)
  }
  if (res.status === 204) return undefined as T
  return res.json()
}

export class ApiError extends Error {
  status: number
  body?: Record<string, unknown>

  constructor(status: number, message: string, body?: Record<string, unknown>) {
    super(message)
    this.status = status
    this.body = body
  }
}

// -- Projects --
export const listProjects = () => request<string[]>('/projects')

// -- Specs --
export const listSpecs = (projectId: string, params?: { state?: string; status?: string }) => {
  const query = new URLSearchParams()
  if (params?.state) query.set('state', params.state)
  if (params?.status) query.set('status', params.status)
  const qs = query.toString()
  return request<Spec[]>(`/projects/${projectId}/specs${qs ? `?${qs}` : ''}`)
}

export const getSpec = (projectId: string, specId: string) =>
  request<Spec>(`/projects/${projectId}/specs/${specId}`)

export const createSpec = (projectId: string, data: CreateSpecRequest) =>
  request<Spec>(`/projects/${projectId}/specs`, {
    method: 'POST',
    body: JSON.stringify(data),
  })

export const updateSpec = (projectId: string, specId: string, data: UpdateSpecRequest) =>
  request<Spec>(`/projects/${projectId}/specs/${specId}`, {
    method: 'PATCH',
    body: JSON.stringify(data),
  })

export const deleteSpec = (projectId: string, specId: string) =>
  request<void>(`/projects/${projectId}/specs/${specId}`, { method: 'DELETE' })

// -- Assignments --
export const listAssignments = (projectId: string, specId: string) =>
  request<Assignment[]>(`/projects/${projectId}/specs/${specId}/assignments`)

// -- Review Requests --
export const listReviewRequests = (projectId: string, specId: string) =>
  request<ReviewRequest[]>(`/projects/${projectId}/specs/${specId}/review-requests`)

export const respondToReview = (
  projectId: string,
  specId: string,
  rrId: string,
  data: { type: string; selectedOptionId?: string; comment?: string },
) =>
  request(`/projects/${projectId}/specs/${specId}/review-requests/${rrId}/respond`, {
    method: 'POST',
    body: JSON.stringify(data),
  })

// -- Activity --
export const listActivity = (projectId: string, specId: string, count = 50) =>
  request<ActivityEvent[]>(`/projects/${projectId}/specs/${specId}/activity?count=${count}`)

// -- Evidence --
export const listEvidence = (projectId: string, specId: string) =>
  request<EvidenceManifest[]>(`/projects/${projectId}/specs/${specId}/evidence`)

// -- Events --
export const submitEvent = (
  projectId: string,
  specId: string,
  event: string,
  version: number,
) =>
  request(`/projects/${projectId}/specs/${specId}/events`, {
    method: 'POST',
    body: JSON.stringify({ event, version }),
  })

export const validateSpec = (
  projectId: string,
  specId: string,
  data: { version: number; outcome?: string },
) =>
  request<{ status: string; eventName: string; currentVersion?: number }>(`/projects/${projectId}/specs/${specId}/validate`, {
    method: 'POST',
    body: JSON.stringify(data),
  })
