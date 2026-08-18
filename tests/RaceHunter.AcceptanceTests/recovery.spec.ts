import { expect, test } from '@playwright/test'

test('failed verify-fix remains recoverable without replacing the original finding', async ({ page }) => {
  const findingId = '22222222-2222-2222-2222-222222222222'
  const finding = {
    id: findingId, runId: 'run-1', successMessage: 'Race condition verified — reproduced 3/3 and minimized to 2 actors.',
    invariantOutcome: 'Fail', invariantSummary: 'oversell', traceReferences: ['trace:1'], agentInterpretation: 'Advisory.',
    reproductions: [1, 2, 3].map(attempt => ({ attempt, outcome: 'Fail', traceReferences: [`trace:${attempt}`] })),
    replayArtifact: { id: 'artifact-1', fingerprint: 'sha256:abc', strategy: 'checkpoint-interleaving', seed: 1729, actorCount: 2, stepCount: 2,
      steps: [{ actorId: 1, stepId: 'order', operationId: 'order', offsetMilliseconds: 0 }, { actorId: 2, stepId: 'order', operationId: 'order', offsetMilliseconds: 0 }] },
    timeline: [], agentActivity: [], replayAttempts: []
  }
  await page.route(`**/api/findings/${findingId}`, route => route.fulfill({ json: finding }))
  await page.route(`**/api/findings/${findingId}/replays`, route => route.fulfill({ status: 503, json: { detail: 'Fixed-target replay is temporarily unavailable.' } }))

  await page.goto(`/findings/${findingId}`)
  await page.getByRole('button', { name: 'Verify Fix' }).click()

  await expect(page.getByRole('alert')).toContainText('Fixed-target replay is temporarily unavailable.')
  await expect(page.getByRole('heading', { name: finding.successMessage })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Verify Fix' })).toBeEnabled()
})

test('live campaign refresh reconstructs reproduction and minimization with ordered cursor history', async ({ page }) => {
  const runId = '33333333-3333-3333-3333-333333333333'
  let phase = 'Running'
  const response = () => ({
    id: runId, status: phase, maxActors: 10, maxConcurrentActors: 10, maxRequests: 40, maxModelCalls: 5,
    maxDurationSeconds: 90, createdAtUtc: '2026-08-18T12:00:00Z', startedAtUtc: '2026-08-18T12:00:01Z',
    completedAtUtc: null, cancellationRequestedAtUtc: null, findingId: null
  })
  await page.route(`**/api/runs/${runId}`, route => route.fulfill({ json: response() }))
  await page.route(`**/api/runs/${runId}/events?after=*`, route => {
    if (route.request().headers().accept?.includes('text/event-stream')) {
      const reproduction = { cursor: 2, kind: 'reproduction-started', message: 'Measuring reproduction 1 of 3.', occurredAtUtc: '2026-08-18T12:00:02Z' }
      return route.fulfill({ contentType: 'text/event-stream', body: `id: 2\nevent: reproduction-started\ndata: ${JSON.stringify(reproduction)}\n\n` })
    }
    return route.fulfill({ json: [
      { cursor: 1, kind: 'campaign-started', message: 'Campaign started.', occurredAtUtc: '2026-08-18T12:00:01Z' },
      { cursor: 2, kind: 'reproduction-started', message: 'Measuring reproduction 1 of 3.', occurredAtUtc: '2026-08-18T12:00:02Z' },
      ...(phase === 'Minimizing' ? [{ cursor: 3, kind: 'minimization-started', message: 'Reducing the verified schedule.', occurredAtUtc: '2026-08-18T12:00:03Z' }] : [])
    ] })
  })

  await page.goto(`/runs/${runId}`)
  await expect(page.getByText('Persisted run status:')).toContainText('Reproducing')
  await expect(page.locator('ol > li')).toHaveCount(2)

  phase = 'Minimizing'
  await page.reload()
  await expect(page.getByText('Persisted run status:')).toContainText('Minimizing')
  await expect(page.locator('ol > li')).toHaveCount(3)
  await expect(page.locator('ol > li').nth(1)).toContainText('reproduction-started')
  await expect(page.locator('ol > li').nth(2)).toContainText('minimization-started')
})
