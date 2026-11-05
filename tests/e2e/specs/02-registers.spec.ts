import { expect, test } from '@playwright/test';
import { OWNER, choose, field, newRow, openRegister, saveRow as save, signIn, stamp } from './support';

/** The registers: a client, a product (changed, with its history) and a cost centre whose type has required properties. */
test.describe('registers', () => {
  test.beforeEach(async ({ page }) => signIn(page));

  test('a client is added, found by its name and changed, its history keeps both versions', async ({ page }) => {
    const name = `Klient E2E ${stamp}`;
    await openRegister(page, 'partners', 'Clients');
    await newRow(page, 'SME', name);
    await save(page);
    const list = page.locator('app-register-page ui-table tbody');
    await page.locator('app-register-page .search input').fill(name);
    await expect(list.locator('tr', { hasText: name })).toHaveCount(1);

    const panel = page.locator('app-element-panel');
    await field(panel, 'Name').locator('input').first().fill(`${name} sh.p.k.`);
    await save(page);
    await expect(panel.locator('.message')).toHaveCount(0);
    await panel.getByRole('tab', { name: 'History' }).click();
    await expect(panel).toContainText(`${name} sh.p.k.`);
    await expect(panel).toContainText(name);
  });

  test('a product of the goods type is added to the products', async ({ page }) => {
    const name = `Birrë E2E ${stamp}`;
    await openRegister(page, 'products', 'Products');
    await newRow(page, 'Goods', name);
    await save(page);
    await page.locator('app-register-page .search input').fill(name);
    await expect(page.locator('app-register-page ui-table tbody tr', { hasText: name })).toHaveCount(1);
  });

  test("a row is refused until its type's required properties are given", async ({ page }) => {
    await openRegister(page, 'cost-centers', 'Companies');
    await newRow(page, 'Company', `Kompania E2E ${stamp}`);
    await save(page);
    const panel = page.locator('app-element-panel');
    await expect(field(panel, 'Activity').locator('.error')).toBeVisible();
    await expect(field(panel, 'Country').locator('.error')).toBeVisible();
    await panel.locator('footer').getByRole('button', { name: 'Cancel' }).click();
    await page.locator('app-register-page .search input').fill(`Kompania E2E ${stamp}`);
    await expect(page.locator('app-register-page ui-table tbody tr', { hasText: `Kompania E2E ${stamp}` })).toHaveCount(0);
  });

  test('the cost centres show as a tree grid: each under its parent, a search keeping the parents of what it finds', async ({ page }) => {
    await openRegister(page, 'cost-centers', 'Companies');
    const tree = page.locator('app-register-page ui-tree-table');
    await expect(tree.locator('tbody tr').first()).toContainText(OWNER.company);
    await expect(tree.locator('tbody tr', { hasText: 'Arka' })).toBeVisible();
    await page.getByRole('button', { name: 'Collapse all' }).click();
    await expect(tree.locator('tbody tr', { hasText: 'Arka' })).toHaveCount(0);
    await page.locator('app-register-page .search input').fill('Arka');
    await expect(tree.locator('tbody tr')).toHaveCount(2);
    await expect(tree.locator('tbody tr').first()).toContainText(OWNER.company);
  });

  test('a point of sale is added under the company by choosing its kind, once its address, phone and e-mail are given', async ({ page }) => {
    const name = `Bar E2E ${stamp}`;
    await openRegister(page, 'cost-centers', 'Companies');
    // The kinds of point of sale are its types; the point of sale itself is only their folder.
    await page.locator('app-register-page').getByRole('button', { name: 'New' }).click();
    const panel0 = page.locator('app-element-panel');
    await field(panel0, 'Type').locator('button.trigger').click();
    await expect(page.locator('.select-panel li[role=option]', { hasText: 'Hotel' })).toBeVisible();
    await expect(page.locator('.select-panel li[role=option]', { hasText: /^\s*Point of sale/ })).toHaveCount(0);
    await page.keyboard.press('Escape');
    await panel0.locator('footer').getByRole('button', { name: 'Cancel' }).click();
    await newRow(page, 'Restaurant / café', name);
    await choose(page, field(page.locator('app-element-panel'), 'Under'), OWNER.company, OWNER.company);
    await save(page);
    const panel = page.locator('app-element-panel');
    await expect(field(panel, 'Address').locator('.error')).toBeVisible();
    await field(panel, 'Address').locator('input').fill('Rruga e Kavajës 1, Tiranë');
    await field(panel, 'Phone').locator('input').fill('+355 4 000 0000');
    await field(panel, 'E-mail').locator('input').fill('bar@example.com');
    await save(page);
    await page.locator('app-register-page .search input').fill(name);
    // The cost centres nest: the new place shows in the tree grid under its company.
    await expect(page.locator('app-register-page ui-tree-table tbody tr', { hasText: name })).toContainText('Restaurant / café');
  });
});
