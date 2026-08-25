import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: '.',
  testMatch: /staging-demo\.spec\.ts/,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: 'list',
  timeout: 235_000,
  expect: { timeout: 90_000 },
  outputDir: process.env.RACEHUNTER_DEMO_ARTIFACT_DIR ?? 'test-results/staging-demo',
  use: {
    baseURL: process.env.RACEHUNTER_BASE_URL,
    trace: 'retain-on-failure',
    video: 'on'
  }
})
