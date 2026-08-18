import { expect, test } from '@playwright/test'

test('real browser journey persists vulnerable proof and fixed replay across refresh', async ({ page }) => {
  await page.goto('/hunts/new')
  await page.getByRole('button', { name: 'Generate Plan' }).click()
  const approve = page.getByRole('button', { name: 'Approve & Run' })
  await expect(approve).toBeVisible({ timeout: 30_000 })
  await approve.click()

  const findingLink = page.getByRole('link', { name: 'Open verified finding' })
  await expect(findingLink).toBeVisible({ timeout: 90_000 })
  await findingLink.click()

  await expect(page.getByRole('heading', { name: 'Race condition verified — reproduced 3/3 and minimized to 2 actors.' })).toBeVisible()
  const schedule = page.getByRole('region', { name: 'Minimized replay schedule' })
  await expect(schedule.getByRole('listitem')).toHaveCount(2)
  await expect(schedule).toContainText('Actor 1')
  await expect(schedule).toContainText('operation place-order')
  await expect(schedule).toContainText('offset 0 ms')

  await page.getByRole('button', { name: 'Verify Fix' }).click()
  const comparison = page.getByRole('region', { name: 'Replay comparison' })
  await expect(comparison).toContainText('Vulnerable failed invariant')
  await expect(comparison).toContainText('Fixed passed invariant')

  await page.reload()
  await expect(page.getByRole('heading', { name: 'Race condition verified — reproduced 3/3 and minimized to 2 actors.' })).toBeVisible()
  await expect(page.getByRole('region', { name: 'Minimized replay schedule' }).getByRole('listitem')).toHaveCount(2)
  await expect(page.getByRole('region', { name: 'Replay comparison' })).toContainText('Fixed passed invariant')
})
