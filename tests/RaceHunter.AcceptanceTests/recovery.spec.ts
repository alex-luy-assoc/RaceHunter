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
