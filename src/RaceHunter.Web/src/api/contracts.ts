export type HuntResponse = { id: string; objective: string; status: string; createdAtUtc: string }
export type PlanResponse = {
  planVersion: string
  schemaVersion: string
  promptVersion: string
  modelId: string
  actors: { name: string; operationId: string }[]
  invariant: { type: string; metric: string; maximum: number | null; leftMetric: string | null; rightMetric: string | null; relation: string | null }
  strategy: { kind: string; actorCount: number; seed: number }
}
export type ApprovalResponse = { runId: string; planVersion: string }
export type RunEvent = { cursor: number; kind: string; message: string; occurredAtUtc: string }
export type RunResponse = {
  id: string
  status: string
  maxActors: number
  maxConcurrentActors: number
  maxRequests: number
  maxModelCalls: number
  maxDurationSeconds: number
  createdAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  cancellationRequestedAtUtc: string | null
}
