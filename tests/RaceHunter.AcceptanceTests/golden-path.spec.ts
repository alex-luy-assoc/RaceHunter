import { expect, test, type Page } from '@playwright/test'

const findingId = '22222222-2222-2222-2222-222222222222'

const persistedFinding = () => ({
  id: findingId,
  runId: '33333333-3333-3333-3333-333333333333',
  successMessage: 'Race condition verified — reproduced 3/3 and minimized to 2 actors.',
  invariantOutcome: 'Fail',
  invariantSummary: '2 successful orders exceeded capacity 1',
  traceReferences: ['trace:1', 'trace:2'],
  agentInterpretation: 'Gemini interpretation is advisory; deterministic evidence established the finding.',
  reproductions: [1, 2, 3].map(attempt => ({ attempt, outcome: 'Fail', traceReferences: [`trace:r${attempt}`] })),
  replayArtifact: {
    id: '11111111-1111-1111-1111-111111111111', fingerprint: 'sha256:abc', strategy: 'checkpoint-interleaving', seed: 1729,
    actorCount: 2, stepCount: 2,
    steps: [
      { actorId: 1, stepId: 'place-order', operationId: 'place-order', offsetMilliseconds: 0 },
      { actorId: 2, stepId: 'place-order', operationId: 'place-order', offsetMilliseconds: 0 }
    ]
  },
  timeline: [
    { actorId: 1, events: [{ sequence: 1, stepId: 'place-order', kind: 'response-success', requestId: 'request-a', occurredAtUtc: '2026-08-18T12:00:01Z' }] },
    { actorId: 2, events: [{ sequence: 2, stepId: 'place-order', kind: 'response-success', requestId: 'request-b', occurredAtUtc: '2026-08-18T12:00:01Z' }] }
  ],
  agentActivity: [{ iteration: 1, action: 'StartMinimization', rationaleSummary: 'Reduce actors', modelId: 'gemini-3.5-flash', schemaVersion: 'strategy-v1', modelInvocationId: 'model-1', occurredAtUtc: '2026-08-18T12:00:02Z' }],
  replayAttempts: [{ id: 'attempt-v', targetMode: 'Vulnerable', outcome: 'Fail', artifactFingerprint: 'sha256:abc', idempotencyKey: 'original', completedAtUtc: '2026-08-18T12:00:03Z' }]
})

async function mockFinding(page: Page) {
  const state = persistedFinding()
  await page.route(`**/api/findings/${findingId}`, async route => route.fulfill({ json: state }))
  await page.route(`**/api/findings/${findingId}/replays`, async route => {
    state.replayAttempts.push({ id: 'attempt-f', targetMode: 'Fixed', outcome: 'Pass', artifactFingerprint: 'sha256:abc', idempotencyKey: 'verify-fix-ui', completedAtUtc: '2026-08-18T12:00:04Z' })
    await route.fulfill({ status: 202, json: { vulnerableOutcome: 'Fail', fixedOutcome: 'Pass', artifactFingerprint: 'sha256:abc', idempotencyKey: 'verify-fix-ui' } })
  })
  return state
}

test('golden path presents measured proof and verifies the fixed target with the same artifact', async ({ page }) => {
  await mockFinding(page)
  const huntId = '44444444-4444-4444-4444-444444444444'
  const runId = '33333333-3333-3333-3333-333333333333'
  let runReads = 0
  await page.route('**/api/hunts', route => route.fulfill({ status: 201, json: { id: huntId, objective: 'Successful orders must not exceed available inventory.', status: 'Draft', createdAtUtc: '2026-08-18T12:00:00Z' } }))
  await page.route(`**/api/hunts/${huntId}/plan`, async route => {
    if (route.request().method() === 'POST') return route.fulfill({ status: 202 })
    return route.fulfill({ json: {
      planVersion: 'plan-v1', schemaVersion: 'plan-v1', promptVersion: 'plan-v1', modelId: 'gemini-3.5-flash',
      actors: [{ name: 'buyer-1', operationId: 'place-order' }, { name: 'buyer-2', operationId: 'place-order' }],
      invariant: { type: 'numeric-boundary', metric: 'successful-orders', maximum: 1, leftMetric: null, rightMetric: null, relation: null },
      strategy: { kind: 'checkpoint-interleaving', actorCount: 2, seed: 1729 }
    } })
  })
  await page.route(`**/api/hunts/${huntId}/runs`, route => route.fulfill({ status: 202, json: { runId, planVersion: 'plan-v1' } }))
  await page.route(`**/api/runs/${runId}`, route => route.fulfill({ json: {
    id: runId, status: runReads++ === 0 ? 'Running' : 'Completed', maxActors: 10, maxConcurrentActors: 10, maxRequests: 40, maxModelCalls: 5,
    maxDurationSeconds: 90, createdAtUtc: '2026-08-18T12:00:00Z', startedAtUtc: '2026-08-18T12:00:01Z',
    completedAtUtc: '2026-08-18T12:00:05Z', cancellationRequestedAtUtc: null, findingId: runReads === 1 ? null : findingId
  } }))
  await page.route(`**/api/runs/${runId}/events?after=*`, route => route.fulfill({ json: [
    { cursor: 1, kind: 'campaign-started', message: 'Campaign started.', occurredAtUtc: '2026-08-18T12:00:01Z' },
    { cursor: 2, kind: 'finding-ready', message: `Verified finding ${findingId} is ready.`, occurredAtUtc: '2026-08-18T12:00:05Z' }
  ] }))

  await page.goto('/hunts/new')
  await page.getByRole('button', { name: 'Generate Plan' }).click()
  await expect(page.getByRole('button', { name: 'Approve & Run' })).toBeVisible()
  await page.getByRole('button', { name: 'Approve & Run' }).click()
  await page.getByRole('link', { name: 'Open verified finding' }).click()

  await expect(page.getByRole('heading', { name: 'Race condition verified — reproduced 3/3 and minimized to 2 actors.' })).toBeVisible()
  const schedule = page.getByRole('region', { name: 'Minimized replay schedule' })
  await expect(schedule).toContainText('Actor 1')
  await expect(schedule).toContainText('place-order')
  await expect(schedule).toContainText('0 ms')
  await expect(page.getByRole('region', { name: 'Causal actor-lane timeline' })).toContainText('Actor 1')
  await expect(page.getByRole('region', { name: 'Agent Activity' })).toContainText('gemini-3.5-flash')
  await page.getByRole('button', { name: 'Verify Fix' }).click()
  await expect(page.getByRole('region', { name: 'Replay comparison' })).toContainText('Vulnerable failed invariant')
  await expect(page.getByRole('region', { name: 'Replay comparison' })).toContainText('Fixed passed invariant')
})

test('refresh rehydrates the immutable finding and judge evidence from the API', async ({ page }) => {
  let reads = 0
  const state = persistedFinding()
  await page.route(`**/api/findings/${findingId}`, async route => { reads++; await route.fulfill({ json: state }) })

  await page.goto(`/findings/${findingId}`)
  await expect(page.getByText('sha256:abc')).toBeVisible()
  await page.reload()

  await expect(page.getByText('sha256:abc')).toBeVisible()
  expect(reads).toBe(2)
})
