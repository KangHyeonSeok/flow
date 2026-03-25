import { BrowserRouter, Routes, Route } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Layout } from '@/components/Layout'
import { ProjectsPage } from '@/pages/ProjectsPage'
import { ProjectOverviewPage } from '@/pages/ProjectOverviewPage'
import { SpecsPage } from '@/pages/SpecsPage'
import { SpecDetailPage } from '@/pages/SpecDetailPage'
import { EpicOverviewPage } from '@/pages/EpicOverviewPage'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 2000,
    },
  },
})

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          <Route element={<Layout />}>
            <Route path="/" element={<ProjectsPage />} />
            <Route path="/projects/:projectId" element={<ProjectOverviewPage />} />
            <Route path="/projects/:projectId/epics/:epicId" element={<EpicOverviewPage />} />
            <Route path="/projects/:projectId/specs" element={<SpecsPage />} />
            <Route path="/projects/:projectId/specs/:specId" element={<SpecDetailPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
