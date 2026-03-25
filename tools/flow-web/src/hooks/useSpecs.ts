import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import * as api from '@/api/client'
import type { CreateSpecRequest, UpdateSpecRequest, UpdateProjectDocumentRequest, UpdateEpicDocumentRequest } from '@/types/flow'

export function useProjects() {
  return useQuery({ queryKey: ['projects'], queryFn: api.listProjects })
}

export function useProjectView(projectId: string) {
  return useQuery({
    queryKey: ['projectView', projectId],
    queryFn: () => api.getProjectView(projectId),
    enabled: !!projectId,
    refetchInterval: 10000,
  })
}

export function useEpics(projectId: string) {
  return useQuery({
    queryKey: ['epics', projectId],
    queryFn: () => api.listEpics(projectId),
    enabled: !!projectId,
  })
}

export function useEpicView(projectId: string, epicId: string) {
  return useQuery({
    queryKey: ['epicView', projectId, epicId],
    queryFn: () => api.getEpicView(projectId, epicId),
    enabled: !!projectId && !!epicId,
    refetchInterval: 10000,
  })
}

export function useEpicSpecs(projectId: string, epicId: string) {
  return useQuery({
    queryKey: ['epicSpecs', projectId, epicId],
    queryFn: () => api.listEpicSpecs(projectId, epicId),
    enabled: !!projectId && !!epicId,
    refetchInterval: 5000,
  })
}

export function useProjectDocument(projectId: string) {
  return useQuery({
    queryKey: ['projectDocument', projectId],
    queryFn: () => api.getProjectDocument(projectId),
    enabled: !!projectId,
  })
}

export function useUpdateProjectDocument(projectId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: UpdateProjectDocumentRequest) => api.updateProjectDocument(projectId, data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['projectDocument', projectId] })
      qc.invalidateQueries({ queryKey: ['projectView', projectId] })
    },
  })
}

export function useEpicDocument(projectId: string, epicId: string) {
  return useQuery({
    queryKey: ['epicDocument', projectId, epicId],
    queryFn: () => api.getEpicDocument(projectId, epicId),
    enabled: !!projectId && !!epicId,
  })
}

export function useUpdateEpicDocument(projectId: string, epicId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: UpdateEpicDocumentRequest) => api.updateEpicDocument(projectId, epicId, data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['epicDocument', projectId, epicId] })
      qc.invalidateQueries({ queryKey: ['epicView', projectId, epicId] })
      qc.invalidateQueries({ queryKey: ['epics', projectId] })
      qc.invalidateQueries({ queryKey: ['projectView', projectId] })
    },
  })
}

export function useSpecEpic(projectId: string, specId: string) {
  const { data: spec } = useSpec(projectId, specId)
  const { data: epics } = useEpics(projectId)

  if (!spec?.epicId) return null

  const epic = epics?.find(e => e.epicId === spec.epicId)
  return epic ? { epicId: epic.epicId, title: epic.title } : { epicId: spec.epicId, title: spec.epicId }
}

export function useBackfillEpicIds(projectId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => api.backfillEpicIds(projectId),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['specs', projectId] })
      qc.invalidateQueries({ queryKey: ['projectView', projectId] })
    },
  })
}

export function useSpecs(projectId: string, filters?: { state?: string; status?: string }) {
  return useQuery({
    queryKey: ['specs', projectId, filters],
    queryFn: () => api.listSpecs(projectId, filters),
    enabled: !!projectId,
    refetchInterval: 5000,
  })
}

export function useSpec(projectId: string, specId: string) {
  return useQuery({
    queryKey: ['spec', projectId, specId],
    queryFn: () => api.getSpec(projectId, specId),
    enabled: !!projectId && !!specId,
    refetchInterval: 3000,
  })
}

export function useCreateSpec(projectId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: CreateSpecRequest) => api.createSpec(projectId, data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['specs', projectId] }),
  })
}

export function useUpdateSpec(projectId: string, specId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: UpdateSpecRequest) => api.updateSpec(projectId, specId, data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['specs', projectId] })
      qc.invalidateQueries({ queryKey: ['spec', projectId, specId] })
    },
  })
}

export function useDeleteSpec(projectId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (specId: string) => api.deleteSpec(projectId, specId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['specs', projectId] }),
  })
}

export function useAssignments(projectId: string, specId: string) {
  return useQuery({
    queryKey: ['assignments', projectId, specId],
    queryFn: () => api.listAssignments(projectId, specId),
    enabled: !!projectId && !!specId,
  })
}

export function useReviewRequests(projectId: string, specId: string) {
  return useQuery({
    queryKey: ['reviewRequests', projectId, specId],
    queryFn: () => api.listReviewRequests(projectId, specId),
    enabled: !!projectId && !!specId,
  })
}

export function useSubmitReviewResponse(projectId: string, specId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: { rrId: string; type: string; selectedOptionId?: string; comment?: string }) =>
      api.respondToReview(projectId, specId, data.rrId, {
        type: data.type,
        selectedOptionId: data.selectedOptionId,
        comment: data.comment,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['reviewRequests', projectId, specId] })
      qc.invalidateQueries({ queryKey: ['spec', projectId, specId] })
      qc.invalidateQueries({ queryKey: ['activity', projectId, specId] })
    },
  })
}

export function useValidateSpec(projectId: string, specId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: { version: number; outcome?: string }) => api.validateSpec(projectId, specId, data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['specs', projectId] })
      qc.invalidateQueries({ queryKey: ['spec', projectId, specId] })
      qc.invalidateQueries({ queryKey: ['assignments', projectId, specId] })
      qc.invalidateQueries({ queryKey: ['reviewRequests', projectId, specId] })
      qc.invalidateQueries({ queryKey: ['activity', projectId, specId] })
      qc.invalidateQueries({ queryKey: ['evidence', projectId, specId] })
    },
  })
}

export function useActivity(projectId: string, specId: string) {
  return useQuery({
    queryKey: ['activity', projectId, specId],
    queryFn: () => api.listActivity(projectId, specId),
    enabled: !!projectId && !!specId,
  })
}

export function useEvidence(projectId: string, specId: string) {
  return useQuery({
    queryKey: ['evidence', projectId, specId],
    queryFn: () => api.listEvidence(projectId, specId),
    enabled: !!projectId && !!specId,
  })
}
