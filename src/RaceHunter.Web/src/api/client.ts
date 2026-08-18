import type { ApprovalResponse, FindingResponse, HuntResponse, PlanResponse, ReplayComparisonResponse, RunEvent, RunResponse } from './contracts'

async function requireOk(response: Response) {
  if (!response.ok) throw new Error((await response.json() as { detail?: string }).detail ?? `Request failed (${response.status})`)
  return response
}

export async function createInventoryHunt(objective: string): Promise<HuntResponse> {
  const response = await requireOk(await fetch('/api/hunts', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ objective })
  }))
  return response.json() as Promise<HuntResponse>
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
