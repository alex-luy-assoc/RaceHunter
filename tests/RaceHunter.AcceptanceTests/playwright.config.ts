import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: '.',
  testMatch: /.*\.spec\.ts/,
  testIgnore: /(?:real-backend|manual-target\.real)\.spec\.ts/,
  fullyParallel: false,
  retries: 0,
  reporter: 'list',
  use: { baseURL: 'http://127.0.0.1:4187', trace: 'retain-on-failure' },
  webServer: {
    command: 'npm run dev --prefix ../../src/RaceHunter.Web -- --port 4187',
    url: 'http://127.0.0.1:4187',
    reuseExistingServer: true,
    timeout: 120_000
  }
})
