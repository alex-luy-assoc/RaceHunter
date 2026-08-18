import type { ApprovalResponse, CloudProofResponse, FindingResponse, HuntResponse, ManualTargetOperation, ManualTargetResponse, PlanResponse, ReplayComparisonResponse, RunEvent, RunResponse } from './contracts'

async function requireOk(response: Response) {
  if (!response.ok) throw new Error((await response.json() as { detail?: string }).detail ?? `Request failed (${response.status})`)
  return response
}

export async function createInventoryHunt(objective: string, targetId?: string, adminToken?: string): Promise<HuntResponse> {
  const response = await requireOk(await fetch('/api/hunts', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...(adminToken ? { Authorization: `Bearer ${adminToken}` } : {}) },
    body: JSON.stringify({ objective, targetId })
  }))
  return response.json() as Promise<HuntResponse>
}

export async function getCapabilities(): Promise<{ manualTargetsEnabled: boolean }> {
  const response = await requireOk(await fetch('/api/capabilities'))
  return response.json() as Promise<{ manualTargetsEnabled: boolean }>
}

export async function configureManualTarget(input: {
  baseUrl: string; credentialReference: string; operations: ManualTargetOperation[]; sensitiveJsonPaths: string[]
}, adminToken: string): Promise<ManualTargetResponse> {
  const url = new URL(input.baseUrl)
  const response = await requireOk(await fetch('/api/admin/targets', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${adminToken}` },
    body: JSON.stringify({ ...input, allowedHosts: [url.hostname], authorizationAcknowledged: true })
  }))
  return response.json() as Promise<ManualTargetResponse>
}

export async function requestPlan(huntId: string) {
  await requireOk(await fetch(`/api/hunts/${huntId}/plan`, { method: 'POST' }))
}

export async function getPlan(huntId: string): Promise<PlanResponse | undefined> {
  const response = await fetch(`/api/hunts/${huntId}/plan`)
  if (response.status === 202) return undefined
  await requireOk(response)
  return response.json() as Promise<PlanResponse>
}

export async function approvePlan(huntId: string, planVersion: string, idempotencyKey: string): Promise<ApprovalResponse> {
  const response = await requireOk(await fetch(`/api/hunts/${huntId}/runs`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ planVersion, idempotencyKey })
  }))
  return response.json() as Promise<ApprovalResponse>
}

export async function getRun(runId: string): Promise<RunResponse> {
  const response = await requireOk(await fetch(`/api/runs/${runId}`))
  return response.json() as Promise<RunResponse>
}

export async function getRunEvents(runId: string, after: number): Promise<RunEvent[]> {
  const response = await requireOk(await fetch(`/api/runs/${runId}/events?after=${after}`))
  return response.json() as Promise<RunEvent[]>
}

export async function getFinding(findingId: string): Promise<FindingResponse> {
  const response = await requireOk(await fetch(`/api/findings/${findingId}`))
  return response.json() as Promise<FindingResponse>
}

export async function verifyFix(findingId: string, idempotencyKey: string): Promise<ReplayComparisonResponse> {
  const response = await requireOk(await fetch(`/api/findings/${findingId}/replays`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ idempotencyKey })
  }))
  return response.json() as Promise<ReplayComparisonResponse>
}

export async function getCloudProof(runId: string): Promise<CloudProofResponse> {
  const response = await requireOk(await fetch(`/api/cloud-proof?runId=${encodeURIComponent(runId)}`))
  return response.json() as Promise<CloudProofResponse>
}
