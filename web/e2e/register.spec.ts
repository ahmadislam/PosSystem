import { test, expect, Page, APIRequestContext } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { readFile } from 'node:fs/promises';

const apiOrigin = 'http://127.0.0.1:5082';
const browserOrigin = 'http://localhost:4202';

async function login(page: Page) {
  await page.goto('/login');
  await page.getByLabel('Username', { exact: true }).fill('admin');
  await page.getByLabel('Password', { exact: true }).fill('Admin@12345');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Sign out', exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Register', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Register', exact: true })).toBeVisible();
}

async function api(request: APIRequestContext) {
  const response = await request.post(`${apiOrigin}/api/auth/login`, {
    headers: { Origin: browserOrigin },
    data: { username: 'admin', password: 'Admin@12345' },
  });
  expect(response.ok()).toBeTruthy();
  const { accessToken } = await response.json();
  const headers = { Origin: browserOrigin, Authorization: `Bearer ${accessToken}` };
  return {
    async post(path: string, data: unknown) {
      const r = await request.post(apiOrigin + path, { headers, data });
      expect(r.ok(), `${path}: ${r.status()}`).toBeTruthy();
      return r.status() === 204 ? null : r.json();
    },
    async put(path: string, data: unknown) {
      const r = await request.put(apiOrigin + path, { headers, data });
      expect(r.ok(), `${path}: ${r.status()}`).toBeTruthy();
      return r.status() === 204 ? null : r.json();
    },
    async get(path: string) {
      const r = await request.get(apiOrigin + path, { headers });
      expect(r.ok(), `${path}: ${r.status()}`).toBeTruthy();
      return r.status() === 204 ? null : r.json();
    },
  };
}

async function productFixture(request: APIRequestContext, price = 10) {
  const client = await api(request);
  const suffix = randomUUID().slice(0, 8).toUpperCase();
  const category = await client.post('/api/categories', { name: `Browser ${suffix}` });
  const body = {
    sku: `SCAN-${suffix}`, name: `Browser item ${suffix}`, categoryId: category.id,
    costPrice: 5, salePrice: price, taxRate: 0, stockQuantity: 10,
    minStockThreshold: 2, isActive: true,
  };
  const product = await client.post('/api/products', body);
  await client.post('/api/shifts/open', { openingCash: 100 });
  return { client, product, body };
}

async function scan(page: Page, sku: string) {
  const scanner = page.getByRole('textbox', { name: 'Scan barcode or SKU' });
  await scanner.fill(sku.toLowerCase());
  await scanner.press('Enter');
  await expect(page.locator('.ticket li').filter({ hasText: sku.replace('SCAN-', '') })).toHaveCount(1);
}

test.beforeEach(async ({ request }) => {
  // All fixtures use this suite's disposable database. Recover a prior failed
  // test's shift so one failed assertion cannot contaminate later cases.
  const client = await api(request);
  const current = await client.get('/api/shifts/current');
  if (!current) return;
  const pending = current.status === 'Open'
    ? await client.post('/api/shifts/end', {})
    : current;
  await client.post('/api/shifts/close', { closingCash: pending.expectedCash });
});

test('inventory → scanner → cash checkout → PDF → return → close shift', async ({ page }) => {
  await login(page);
  const suffix = randomUUID().slice(0, 8).toUpperCase();
  const sku = `UI-${suffix}`;
  const name = `UI item ${suffix}`;
  await page.getByRole('link', { name: 'Inventory', exact: true }).click();
  await page.getByPlaceholder('New category').fill(`UI category ${suffix}`);
  const categorySaved = page.waitForResponse(r => r.url().endsWith('/api/categories') && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Add category', exact: true }).click();
  expect((await categorySaved).ok()).toBeTruthy();
  await page.getByRole('button', { name: 'New product', exact: true }).click();
  const form = page.locator('.modal form');
  await form.getByLabel('SKU', { exact: true }).fill(sku);
  await form.getByLabel('Name', { exact: true }).fill(name);
  await form.getByRole('combobox', { name: 'Category', exact: true }).selectOption({ label: `UI category ${suffix}` });
  await form.getByLabel('Cost', { exact: true }).fill('5');
  await form.getByLabel('Sale', { exact: true }).fill('10');
  await form.getByLabel('Tax %', { exact: true }).fill('0');
  await form.getByLabel('Initial stock', { exact: true }).fill('5');
  await form.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(form).not.toBeVisible();
  await page.getByPlaceholder('Search', { exact: true }).fill(sku);
  await page.getByPlaceholder('Search', { exact: true }).press('Enter');
  await expect(page.getByRole('row').filter({ hasText: sku })).toBeVisible();
  await page.getByRole('link', { name: 'Shift', exact: true }).click();
  await page.getByLabel('Opening cash', { exact: true }).fill('100');
  await page.getByRole('button', { name: 'Open shift', exact: true }).click();
  await expect(page.getByRole('button', { name: 'End shift', exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Register', exact: true }).click();
  await page.getByRole('textbox', { name: 'Scan barcode or SKU' }).fill(sku.toLowerCase());
  await page.getByRole('textbox', { name: 'Scan barcode or SKU' }).press('Enter');
  await expect(page.locator('.ticket li').filter({ hasText: name })).toBeVisible();
  await page.getByLabel('Cash tendered', { exact: true }).fill('20');
  const checkoutResponse = page.waitForResponse(r => r.url().endsWith('/api/sales') && r.request().method() === 'POST');
  await page.getByRole('button', { name: /Record sale/ }).click();
  const recordedSale = await (await checkoutResponse).json();
  await expect(page.getByRole('heading', { name: 'Sale complete' })).toBeVisible();
  await expect(page.locator('.modal')).toContainText('Change due: 10.00');
  const downloadEvent = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download invoice PDF', exact: true }).click();
  const download = await downloadEvent;
  expect(download.suggestedFilename()).toMatch(/\.pdf$/);
  const pdf = await readFile((await download.path())!);
  expect(pdf.subarray(0, 4).toString()).toBe('%PDF');
  await page.locator('.modal').getByRole('button', { name: 'Close', exact: true }).click();
  await page.getByRole('link', { name: 'Sales', exact: true }).click();
  const invoiceRow = page.getByRole('row').filter({ hasText: recordedSale.invoiceNumber });
  await invoiceRow.getByRole('button', { name: 'Return', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Return reason (optional)', { exact: true }).fill('Browser regression return');
  await dialog.getByLabel('Return qty', { exact: true }).fill('1');
  await dialog.getByRole('button', { name: 'Record return', exact: true }).click();
  await expect(dialog).not.toBeVisible();
  await expect(invoiceRow).toContainText('Refunded');
  await page.getByRole('link', { name: 'Shift', exact: true }).click();
  await page.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(page.getByText('Expected cash 100.00', { exact: true })).toBeVisible();
  await page.getByLabel('Counted closing cash', { exact: true }).fill('100');
  await page.getByRole('button', { name: 'Close drawer', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Open shift', exact: true })).toBeVisible();
});

test('server rejects insufficient tender after price change and returns authoritative change', async ({ page, request }) => {
  const { client, product, body } = await productFixture(request);
  try {
    await login(page);
    await scan(page, product.sku);
    await client.put(`/api/products/${product.id}`, { ...body, salePrice: 12 });
    await page.getByLabel('Cash tendered', { exact: true }).fill('10');
    await page.getByRole('button', { name: /Record sale/ }).click();
    await expect(page.locator('.toast.err')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Sale complete' })).not.toBeVisible();
    expect((await client.get(`/api/products/${product.id}`)).stockQuantity).toBe(10);
    await page.getByLabel('Cash tendered', { exact: true }).fill('20');
    await page.getByRole('button', { name: /Record sale/ }).click();
    await expect(page.getByRole('heading', { name: 'Sale complete' })).toBeVisible();
    await expect(page.locator('.modal')).toContainText('12.00');
    await expect(page.locator('.modal')).toContainText('Change due: 8.00');
  } finally {
    await client.post('/api/shifts/end', {});
    await client.post('/api/shifts/close', { closingCash: 112 });
  }
});

test('lost checkout response can be retried without a duplicate sale', async ({ page, request }) => {
  const { client, product } = await productFixture(request);
  try {
    await login(page);
    await scan(page, product.sku);
    await page.getByLabel('Cash tendered', { exact: true }).fill('20');
    await page.route('**/api/sales', async route => {
      if (route.request().method() !== 'POST') return route.continue();
      const committed = await route.fetch();
      expect(committed.ok()).toBeTruthy();
      await route.abort('failed');
    }, { times: 1 });
    await page.getByRole('button', { name: /Record sale/ }).click();
    await expect(page.locator('.toast.err')).toBeVisible();
    await page.getByRole('button', { name: /Record sale/ }).click();
    await expect(page.getByRole('heading', { name: 'Sale complete' })).toBeVisible();
    expect((await client.get(`/api/products/${product.id}`)).stockQuantity).toBe(9);
    const sales = await client.get('/api/sales?pageSize=100');
    expect(sales.items.filter((s: { items: { productId: number }[] }) => s.items.some(i => i.productId === product.id))).toHaveLength(1);
  } finally {
    await client.post('/api/shifts/end', {});
    await client.post('/api/shifts/close', { closingCash: 110 });
  }
});

test('cookie restores a browser session and sign-out clears the cart', async ({ page, request }) => {
  const { client, product } = await productFixture(request);
  try {
    await login(page);
    await page.reload();
    await expect(page.getByRole('heading', { name: 'Register', exact: true })).toBeVisible();
    await scan(page, product.sku);
    await page.getByRole('button', { name: 'Sign out', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Sign in to the register' })).toBeVisible();
    await login(page);
    await expect(page.getByText('Tap a product to add it.', { exact: true })).toBeVisible();
  } finally {
    await client.post('/api/shifts/end', {});
    await client.post('/api/shifts/close', { closingCash: 100 });
  }
});
