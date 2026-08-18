import type { ApprovalResponse, HuntResponse, PlanResponse } from './contracts'

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
