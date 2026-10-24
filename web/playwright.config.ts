import { defineConfig } from '@playwright/test';

if (!process.env.POS_E2E_CONNECTION) {
  throw new Error('Set POS_E2E_CONNECTION to a disposable PostgreSQL database before running browser tests.');
}

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: 'http://localhost:4202',
    browserName: 'chromium',
    actionTimeout: 15_000,
    launchOptions: process.env.CHROMIUM_EXECUTABLE_PATH
      ? { executablePath: process.env.CHROMIUM_EXECUTABLE_PATH }
      : {},
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
  webServer: [
    {
      command: 'dotnet run --no-build --configuration Release --no-launch-profile --project ../src/PosSystem.Api --urls http://127.0.0.1:5082',
      url: 'http://127.0.0.1:5082/health',
      timeout: 120_000,
      reuseExistingServer: false,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ConnectionStrings__Default: process.env.POS_E2E_CONNECTION,
        Cors__Origins__0: 'http://localhost:4202',
        Email__Enabled: 'false',
        Seed__AdminUsername: 'admin',
        Seed__AdminEmail: 'admin@example.test',
        Seed__AdminPassword: 'Admin@12345',
        // Optional documentation capture uses this suite's disposable database only.
        Seed__DemoData: process.env.POS_CAPTURE_DOCS === '1' ? 'true' : 'false',
      },
    },
    {
      command: 'npm start -- --host 127.0.0.1 --port 4202 --proxy-config e2e/proxy.conf.json',
      url: 'http://127.0.0.1:4202',
      timeout: 120_000,
      reuseExistingServer: false,
      env: { NG_CLI_ANALYTICS: 'false' },
    },
  ],
});
