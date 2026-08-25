import { expect, test } from '@playwright/test'
import fs from 'node:fs'
import path from 'node:path'
import { bufferJsonResponse } from './staging-demo-response.mjs'

type Progress = {
  schemaVersion: '1.0'
  status: 'Ready' | 'DemoStarted' | 'DemoComplete' | 'AmbiguousMutation'
  demoAttemptStarted: boolean
  runCreateStarted?: boolean
  replayCreateStarted?: boolean
  huntId?: string
  runId?: string
  findingId?: string
  replayComplete?: boolean
  completedAtUtc?: string
  elapsedSeconds?: number
}

const progressPath = process.env.RACEHUNTER_DEMO_PROGRESS_PATH ?? ''

function loadProgress(): Progress {
  if (!progressPath) throw new Error('RACEHUNTER_DEMO_PROGRESS_PATH is required.')
  if (!fs.existsSync(progressPath)) return { schemaVersion: '1.0', status: 'Ready', demoAttemptStarted: false }
  return JSON.parse(fs.readFileSync(progressPath, 'utf8')) as Progress
}

function saveProgress(progress: Progress) {
  fs.mkdirSync(path.dirname(progressPath), { recursive: true })
  const temporary = `${progressPath}.tmp`
  fs.writeFileSync(temporary, `${JSON.stringify(progress, null, 2)}\n`, 'utf8')
  fs.renameSync(temporary, progressPath)
}

test('one unedited fresh staging demo follows the documented journey', async ({ page }) => {
  const started = Date.now()
  const deadline = Date.parse(process.env.RACEHUNTER_DEMO_DEADLINE_UTC ?? '')
  if (!Number.isFinite(deadline) || deadline <= started) throw new Error('RACEHUNTER_DEMO_DEADLINE_UTC must be a future absolute deadline.')
  test.setTimeout(Math.min(235_000, deadline - started))
  const progress = loadProgress()
  if (progress.status === 'DemoComplete') return
  if (progress.demoAttemptStarted && !progress.huntId) {
    progress.status = 'AmbiguousMutation'
    saveProgress(progress)
    throw new Error('AmbiguousMutation: demo hunt creation may have occurred without a durable hunt ID; a second new demo is forbidden.')
  }

  if (!progress.huntId) {
    await page.goto('/hunts/new')
    progress.demoAttemptStarted = true
    progress.status = 'DemoStarted'
    saveProgress(progress)
    const created = page.waitForResponse(response => response.url().endsWith('/api/hunts') && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Generate Plan' }).click()
    progress.huntId = String((await (await created).json()).id)
    saveProgress(progress)
    await page.waitForURL(url => url.pathname === `/hunts/${progress.huntId}/plan`)
    await expect(page.getByRole('heading', { name: 'Approve once. Run unattended.' })).toBeVisible()
  }

  if (!progress.runId) {
    const planPath = `/hunts/${progress.huntId}/plan`
    if (new URL(page.url()).pathname !== planPath) await page.goto(planPath)
    await expect(page.getByRole('heading', { name: 'Approve once. Run unattended.' })).toBeVisible()
    const approve = page.getByRole('button', { name: 'Approve & Run' })
    await expect(approve).toBeVisible()
    await page.evaluate(({ key, value }) => localStorage.setItem(key, value), {
      key: `racehunter:approval:${progress.huntId}`,
      value: `cloud-demo-${progress.huntId}`
    })
    progress.runCreateStarted = true
    saveProgress(progress)
    const approved = bufferJsonResponse(page.waitForResponse(response => response.url().endsWith(`/api/hunts/${progress.huntId}/runs`) && response.request().method() === 'POST'))
    await approve.click()
    progress.runId = String((await approved).runId)
    progress.runCreateStarted = false
    saveProgress(progress)
  } else if (!progress.findingId) {
    await page.goto(`/runs/${progress.runId}`)
  }

  if (!progress.findingId) {
    const findingLink = page.getByRole('link', { name: 'Open verified finding' })
    await expect(findingLink).toBeVisible()
    const href = await findingLink.getAttribute('href')
    progress.findingId = href?.split('/').pop()
    if (!progress.findingId) throw new Error('Verified finding link did not expose a finding ID.')
    saveProgress(progress)
    await findingLink.click()
  } else {
    await page.goto(`/findings/${progress.findingId}`)
  }

  await expect(page.getByRole('heading', { name: 'Race condition verified — reproduced 3/3 and minimized to 2 actors.' })).toBeVisible()
  await expect(page.getByRole('region', { name: 'Minimized replay schedule' }).getByRole('listitem')).toHaveCount(2)
  await expect(page.getByRole('region', { name: 'Cloud proof' })).toContainText(progress.runId!)

  if (!progress.replayComplete) {
    await page.evaluate(({ key, value }) => localStorage.setItem(key, value), {
      key: `racehunter:verify-fix:${progress.findingId}`,
      value: `cloud-demo-fix-${progress.findingId}`
    })
    progress.replayCreateStarted = true
    saveProgress(progress)
    await page.getByRole('button', { name: 'Verify Fix' }).click()
    const comparison = page.getByRole('region', { name: 'Replay comparison' })
    await expect(comparison).toContainText('Vulnerable failed invariant')
    await expect(comparison).toContainText('Fixed passed invariant')
    progress.replayComplete = true
    progress.replayCreateStarted = false
    saveProgress(progress)
  }

  progress.status = 'DemoComplete'
  progress.completedAtUtc = new Date().toISOString()
  progress.elapsedSeconds = Math.round((Date.now() - started) / 100) / 10
  saveProgress(progress)
})
