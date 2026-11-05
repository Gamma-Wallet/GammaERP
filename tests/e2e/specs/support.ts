import { Download, Locator, Page, expect } from '@playwright/test';

/** The company the global setup signs up, and its owner (test values only: the e2e database is thrown away after every run). */
export const OWNER = { username: 'e2e.owner', password: 'e2e-owner-password-1', company: 'E2E Tregtia' };

/** A name unique to this run, so a kept stack can run the specs again. */
export const stamp = Date.now().toString(36).slice(-5).toUpperCase();

/** Signs in through the API's sign-in page and waits for the screens (choosing the place when the user has several). */
export async function signIn(page: Page, username = OWNER.username, password = OWNER.password, place?: string): Promise<void> {
  const errors: string[] = [];
  page.on('pageerror', e => errors.push(e.message));
  await page.goto('/');
  await page.waitForURL('**/account/login**');
  await page.fill('input[name=username]', username);
  await page.fill('input[name=password]', password);
  await page.click('button[type=submit]');
  await page.waitForSelector('app-shell nav.menu, app-choose-node button');
  if (await page.locator('app-choose-node').count()) {
    await page.locator('app-choose-node button', { hasText: place ?? OWNER.company }).first().click();
    await page.waitForSelector('app-shell nav.menu');
  }
  expect(errors, 'no script errors while signing in').toEqual([]);
}

/** Opens a menu item, opening its group first when it is closed. */
export async function openMenu(page: Page, group: string, item: string): Promise<void> {
  const link = page.locator('app-shell nav.menu a.item', { hasText: item }).first();
  if (!(await link.isVisible())) {
    await page.locator('app-shell nav.menu .group-head', { hasText: group }).first().click();
  }
  await link.click();
}

/** Chooses an option of a ui-select (searching first when it can search). */
export async function choose(page: Page, select: Locator, option: string, search?: string): Promise<void> {
  await select.locator('button.trigger').first().click();
  const panel = page.locator('.select-panel');
  await expect(panel).toBeVisible();
  if (search !== undefined && (await panel.locator('input.search').count())) {
    await panel.locator('input.search').fill(search);
  }
  await panel.locator('li[role=option]', { hasText: option }).first().click();
}

/** A labelled field (ui-field) by its label. */
export function field(scope: Locator | Page, label: string): Locator {
  const page = 'goto' in scope ? scope : scope.page();
  return scope.locator('ui-field').filter({ has: page.locator('.label', { hasText: new RegExp(`^\\s*${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*\\*?\\s*$`) }) }).first();
}

/** Waits for a toast saying something. */
export async function toast(page: Page, text: string | RegExp): Promise<void> {
  await expect(page.locator('ui-toasts .toast', { hasText: text }).last()).toBeVisible();
}

/** Waits for a download started by an action, and checks it is a real file of the kind named by its first bytes. */
export async function download(page: Page, start: () => Promise<unknown>, kind: 'pdf' | 'xlsx'): Promise<Download> {
  const [file] = await Promise.all([page.waitForEvent('download'), start()]);
  const path = await file.path();
  const head = (await import('node:fs')).readFileSync(path).subarray(0, 4).toString('latin1');
  expect(head, `${file.suggestedFilename()} is a ${kind}`).toBe(kind === 'pdf' ? '%PDF' : 'PK\u0003\u0004');
  return file;
}

/** Opens a register at one of its categories. */
export async function openRegister(page: Page, entity: string, category: string): Promise<void> {
  await page.goto(`/entities/${entity}`);
  await page.locator('app-register-page nav.categories button.category', { hasText: category }).first().click();
}

/** Starts a new row of a type in the open register category, with its name. */
export async function newRow(page: Page, type: string, name: string): Promise<void> {
  await page.locator('app-register-page').getByRole('button', { name: 'New' }).click();
  const panel = page.locator('app-element-panel');
  await choose(page, field(panel, 'Type'), type);
  await field(panel, 'Name').locator('input').first().fill(name);
}

export async function saveRow(page: Page): Promise<void> {
  await page.locator('app-element-panel footer').getByRole('button', { name: 'Save' }).click();
}

/** Adds a row to a register through its screen and waits until the list shows it. */
export async function addRow(page: Page, entity: string, category: string, type: string, name: string): Promise<void> {
  await openRegister(page, entity, category);
  await newRow(page, type, name);
  await saveRow(page);
  await page.locator('app-register-page .search input').fill(name);
  await expect(page.locator('app-register-page ui-table tbody tr', { hasText: name })).toHaveCount(1);
}

/** Calls the API as the signed-in screens do (their token and cost centre), for what a spec checks behind a screen. */
export async function api(page: Page, path: string): Promise<{ status: number; head: string }> {
  return page.evaluate(async url => {
    const tokens = JSON.parse(sessionStorage.getItem('gamma-erp.tokens') ?? '{}');
    const response = await fetch(url, { headers: { Authorization: `Bearer ${tokens.access}` } });
    const bytes = new Uint8Array(await response.arrayBuffer()).subarray(0, 4);
    return { status: response.status, head: String.fromCharCode(...bytes) };
  }, path);
}

/** Starts a new document of a type (its code) from a document list. */
export async function newDocument(page: Page, group: string, list: string, code: string): Promise<void> {
  await openMenu(page, group, list);
  const lists = page.locator('app-document-list');
  await lists.getByRole('button', { name: /^New/ }).first().click();
  const item = page.locator('.menu-panel .menu-item', { hasText: code });
  if (await item.count()) {
    await item.first().click();
  }
  await expect(page.locator('app-document-editor')).toBeVisible();
}

/** Adds a line of a kind on an element found by a search, at a price. */
export async function addLine(page: Page, kind: string, element: string, price: string): Promise<void> {
  const grid = page.locator('app-document-editor app-line-grid');
  await grid.locator('.add button').first().click();
  const menu = page.locator('.menu-panel .menu-item', { hasText: kind });
  if (await menu.count()) {
    await menu.first().click();
  }
  const row = grid.locator('tbody tr').last();
  await choose(page, row.locator('td').nth(2).locator('ui-select'), element, element);
  await row.locator('input[aria-label=Quantity]').fill('1');
  await row.locator('input[aria-label=Price]').fill(price);
}

/** A posted sale to a client on an account line (sales of goods, 705), through the editor. */
export async function postSale(page: Page, client: string, price: string): Promise<void> {
  await newDocument(page, 'Documents', 'Sales', 'FSHM');
  const editor = page.locator('app-document-editor');
  await choose(page, editor.locator('ui-field.side').nth(0), '');
  await choose(page, editor.locator('ui-field.side').nth(1), client, client);
  await addLine(page, 'Account', '705', price);
  await editor.getByRole('button', { name: 'Post', exact: true }).click();
  await page.locator('ui-confirm').getByRole('button', { name: 'Post' }).click();
  await toast(page, 'posted');
}
