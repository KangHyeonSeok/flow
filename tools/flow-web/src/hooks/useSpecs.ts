import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import * as api from '@/api/client'
import type { CreateSpecRequest, UpdateSpecRequest } from '@/types/flow'

export function useProjects() {
  return useQuery({ queryKey: ['projects'], queryFn: api.listProjects })
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
