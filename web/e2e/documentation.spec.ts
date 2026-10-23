import { test, expect } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import path from 'node:path';

// Opt-in only. The normal e2e job never modifies documentation artifacts.
// POS_E2E_CONNECTION must point at a freshly created disposable database.
test('capture real demo dashboard, checkout and downloaded invoice', async ({ page, request }) => {
  test.skip(process.env.POS_CAPTURE_DOCS !== '1', 'Set POS_CAPTURE_DOCS=1 to update documentation screenshots.');
  test.setTimeout(120_000);
  const output = path.resolve(process.cwd(), '../docs/screenshots');
  await mkdir(output, { recursive: true });
  const openapi = await request.get('http://127.0.0.1:5082/swagger/v1/swagger.json');
  expect(openapi.ok()).toBeTruthy();
  await writeFile(path.join(tmpdir(), 'pos-audit-openapi.json'), await openapi.body());

  await page.setViewportSize({ width: 1680, height: 1280 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/login');
  await page.getByLabel('Username', { exact: true }).fill('admin');
  await page.getByLabel('Password', { exact: true }).fill('Admin@12345');
  const dashboard = page.waitForResponse(r => r.url().includes('/api/dashboard') && r.request().method() === 'GET');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Dashboard', exact: true })).toBeVisible();
  expect((await dashboard).ok()).toBeTruthy();
  await expect(page.locator('app-dashboard-chart canvas')).toHaveCount(3);
  await page.evaluate(() => document.fonts.ready);
  await page.screenshot({ path: path.join(output, 'dashboard.png'), fullPage: true });

  await page.getByRole('link', { name: 'Shift', exact: true }).click();
  await page.getByLabel('Opening cash', { exact: true }).fill('100');
  await page.getByRole('button', { name: 'Open shift', exact: true }).click();
  await expect(page.getByRole('button', { name: 'End shift', exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Register', exact: true }).click();
  for (const sku of ['COF-250', 'TEA-100', 'CHP-050']) {
    const scanner = page.getByRole('textbox', { name: 'Search products or scan a barcode' });
    await scanner.fill(sku);
    await scanner.press('Enter');
    await expect(scanner).toHaveValue('');
  }
  await page.getByRole('button', { name: 'Increase quantity of Chocolate Bar', exact: true }).click();
  await page.getByRole('button', { name: 'Exact', exact: true }).click();
  const toastClose = page.locator('.toast button');
  if (await toastClose.isVisible()) await toastClose.click();
  await page.evaluate(() => {
    document.querySelector('.cart-lines')?.scrollTo(0, 0);
    window.scrollTo(0, 0);
  });
  await page.screenshot({ path: path.join(output, 'pos.png'), fullPage: true });

  const recorded = page.waitForResponse(r => r.url().endsWith('/api/sales') && r.request().method() === 'POST');
  await page.getByRole('button', { name: /^Charge / }).click();
  const response = await recorded;
  expect(response.ok()).toBeTruthy();
  const sale = await response.json();
  await expect(page.getByRole('heading', { name: 'Sale complete', exact: true })).toBeVisible();
  const downloadEvent = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download invoice PDF', exact: true }).click();
  const invoice = await downloadEvent;
  const invoiceFile = path.join(output, 'sample-invoice.pdf');
  await invoice.saveAs(invoiceFile);
  execFileSync('pdftoppm', ['-f', '1', '-singlefile', '-scale-to', '1600', '-png', invoiceFile, path.join(output, 'invoice')]);
  await page.locator('.modal').getByRole('button', { name: 'Close', exact: true }).click();
  await page.getByRole('link', { name: 'Shift', exact: true }).click();
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await page.getByLabel('Counted closing cash', { exact: true }).fill((100 + sale.totalAmount).toFixed(2));
  await page.getByRole('button', { name: 'Close drawer', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Open shift', exact: true })).toBeVisible();
  // Capture the dashboard again after the sample checkout adds the Snacks category.
  const updatedDashboard = page.waitForResponse(r => r.url().includes('/api/dashboard') && r.request().method() === 'GET');
  await page.getByRole('link', { name: 'Dashboard', exact: true }).click();
  expect((await updatedDashboard).ok()).toBeTruthy();
  await expect(page.locator('app-dashboard-chart canvas')).toHaveCount(3);
  if (await toastClose.isVisible()) await toastClose.click();
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: path.join(output, 'dashboard.png'), fullPage: true });
});
