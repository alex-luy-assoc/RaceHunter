export type HuntResponse = { id: string; objective: string; status: string; createdAtUtc: string }
export type ManualTargetOperation = { id: string; method: string; path: string; requestTemplateJson: string; observationPaths: Record<string, string>; observationTypes?: Record<string, 'number' | 'text'>; isSetup: boolean; idempotencyMode?: 'none' | 'receiver-keyed' }
export type ManualTargetResponse = { id: string; baseUrl: string; host: string; credentialReference: string; operations: ManualTargetOperation[]; sensitiveJsonPaths: string[]; createdAtUtc: string }
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
  findingId?: string | null
}

export type ReplayStep = { actorId: number; stepId: string; operationId: string; offsetMilliseconds: number }
export type TimelineEvent = { sequence: number; attemptId: string; stepId: string; kind: string; requestId: string; occurredAtUtc: string }
export type FindingResponse = {
  id: string
  runId: string
  successMessage: string
  invariantOutcome: string
  invariantSummary: string
  traceReferences: string[]
  agentInterpretation: string
  reproductions: { attempt: number; outcome: string; traceReferences: string[] }[]
  replayArtifact: {
    id: string
    fingerprint: string
    strategy: string
    seed: number
    actorCount: number
    stepCount: number
    steps: ReplayStep[]
  }
  timeline: { actorId: number; events: TimelineEvent[] }[]
  agentActivity: {
    iteration: number
    action: string
    rationaleSummary: string
    modelId: string
    schemaVersion: string
    modelInvocationId: string
    occurredAtUtc: string
  }[]
  replayAttempts: {
    id: string
    targetMode: string
    outcome: string
    artifactFingerprint: string
    idempotencyKey: string
    completedAtUtc: string
  }[]
}

export type ReplayComparisonResponse = {
  vulnerableOutcome: string
  fixedOutcome: string
  artifactFingerprint: string
  idempotencyKey: string
}

export type CloudProofResponse = {
  apiRevision: string
  workerService: string
  pubSubTopic: string
  cloudSqlInstance: string
  modelId: string
  schemaVersions: string
  workerAuthentication: string
  runId: string
  runStatus: string
  planVersion: string
  workerExecution: string
  modelInvocationId: string
  traceEventCount: number
  findingId: string | null
  evidenceCorrelationId: string
  requestTraceId: string
}
