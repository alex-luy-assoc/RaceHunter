import { expect, test } from '@playwright/test'

test('local admin can configure and replay the controlled HTTP JSON target', async ({ page }) => {
  await page.goto('/hunts/new')
  const manual = page.getByLabel('Use authorized HTTP/JSON target')
  await expect(manual).toBeVisible()
  await manual.check()
  await page.getByLabel('Admin bearer token').fill('local-admin')
  await page.getByRole('button', { name: 'Generate Plan' }).click()

  const approve = page.getByRole('button', { name: 'Approve & Run' })
  await expect(approve).toBeVisible({ timeout: 30_000 })
  await approve.click()
  const findingLink = page.getByRole('link', { name: 'Open verified finding' })
  await expect(findingLink).toBeVisible({ timeout: 90_000 })
  await findingLink.click()

  await expect(page.getByRole('heading', { name: /authorized target and minimized to 2 actors/ })).toBeVisible()
  await expect(page.getByRole('region', { name: 'Minimized replay schedule' }).getByRole('listitem')).toHaveCount(2)
  await expect(page.getByRole('region', { name: 'Minimized replay schedule' })).toContainText('reserve-seat')
  await page.getByRole('button', { name: 'Replay Authorized Target' }).click()
  await expect(page.getByRole('region', { name: 'Replay comparison' })).toContainText('Authorized target replay')
  await expect(page.getByRole('region', { name: 'Replay comparison' })).toContainText('Both results use the same immutable artifact.')
})
