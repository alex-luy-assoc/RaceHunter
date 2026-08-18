import { describe, expect, it } from 'vitest'
import type { FindingResponse } from '../api/contracts'
import { buildActorLanes, buildReplayComparison, findingHeadline } from './findingView'

const finding = (): FindingResponse => ({
  id: 'finding-1',
  runId: 'run-1',
  successMessage: 'Race condition verified — reproduced 3/3 and minimized to 2 actors.',
  invariantOutcome: 'Fail',
  invariantSummary: '2 successful orders exceeded capacity 1',
  traceReferences: ['trace:1', 'trace:2'],
  agentInterpretation: 'Concurrent reads exposed a lost update.',
  reproductions: [1, 2, 3].map(attempt => ({ attempt, outcome: 'Fail', traceReferences: [`trace:r${attempt}`] })),
  replayArtifact: {
    id: 'artifact-1', fingerprint: 'sha256:abc', strategy: 'checkpoint-interleaving', seed: 1729,
    actorCount: 2, stepCount: 2,
    steps: [
      { actorId: 1, stepId: 'place-order', operationId: 'place-order', offsetMilliseconds: 0 },
      { actorId: 2, stepId: 'place-order', operationId: 'place-order', offsetMilliseconds: 0 }
    ]
  },
  timeline: [
      { actorId: 2, events: [{ sequence: 2, attemptId: 'attempt-1', stepId: 'commit', kind: 'response', requestId: 'b', occurredAtUtc: '2026-08-18T12:00:02Z' }] },
      { actorId: 1, events: [{ sequence: 1, attemptId: 'attempt-1', stepId: 'read', kind: 'request', requestId: 'a', occurredAtUtc: '2026-08-18T12:00:01Z' }] }
  ],
  agentActivity: [{ iteration: 1, action: 'StartMinimization', rationaleSummary: 'Reduce actors', modelId: 'gemini-3.5-flash', schemaVersion: 'strategy-v1', modelInvocationId: 'model-1', occurredAtUtc: '2026-08-18T12:00:03Z' }],
  replayAttempts: [{ id: 'attempt-v', targetMode: 'Vulnerable', outcome: 'Fail', artifactFingerprint: 'sha256:abc', idempotencyKey: 'original', completedAtUtc: '2026-08-18T12:00:04Z' }]
})

describe('finding view projection', () => {
  it('uses the exact verified message only for measured 3/3 and two actors', () => {
    expect(findingHeadline(finding())).toBe('Race condition verified — reproduced 3/3 and minimized to 2 actors.')
    const incomplete = finding()
    incomplete.reproductions[2] = { ...incomplete.reproductions[2], outcome: 'Pass' }
    expect(findingHeadline(incomplete)).toBe('Finding evidence is not yet fully reproduced and minimized.')
  })

  it('orders causal actor lanes and their events deterministically', () => {
    const lanes = buildActorLanes(finding())
    expect(lanes.map(lane => lane.actorId)).toEqual([1, 2])
    expect(lanes.flatMap(lane => lane.events.map(event => event.sequence))).toEqual([1, 2])
  })

  it('shows vulnerable failure beside fixed pass only when fingerprints match', () => {
    const value = finding()
    value.replayAttempts.push({ id: 'attempt-f', targetMode: 'Fixed', outcome: 'Pass', artifactFingerprint: 'sha256:abc', idempotencyKey: 'verify', completedAtUtc: '2026-08-18T12:00:05Z' })
    expect(buildReplayComparison(value)).toEqual({ vulnerable: 'Fail', fixed: 'Pass', sameArtifact: true })
    value.replayAttempts[1].artifactFingerprint = 'sha256:mutated'
    expect(buildReplayComparison(value).sameArtifact).toBe(false)
  })
})
