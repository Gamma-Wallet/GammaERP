import { Browser, Page, expect, test } from '@playwright/test';
import { OWNER, addRow, choose, field, newRow, openMenu, openRegister, saveRow, signIn, stamp, toast } from './support';

const seller = { username: `e2e.seller.${stamp.toLowerCase()}`, password: 'e2e-seller-password-1', name: `Shitësi E2E ${stamp}` };

async function asSeller(browser: Browser): Promise<Page> {
  const page = await (await browser.newContext()).newPage();
  await signIn(page, seller.username, seller.password);
  return page;
}

/**
 * Configuration and rights: a configuration row is added; a new user signs in with what the owner set; the user sees a register's category
 * only once the owner gives a right on it.
 */
test('the owner adds a VAT group, makes a user and gives the user the right to read the clients', async ({ page, browser }) => {
  const client = `Klient Leje ${stamp}`;
  await signIn(page);

  // A configuration row.
  await openMenu(page, 'Accounting setup', 'VAT groups');
  const table = page.locator('app-config-table').first();
  await table.getByRole('button', { name: 'Add' }).click();
  const dialog = page.locator('app-config-row-dialog');
  await field(dialog, 'Name').locator('input').first().fill(`TVSH E2E ${stamp}`);
  await field(dialog, 'Rate').locator('input').fill('6');
  await dialog.getByRole('button', { name: 'Save' }).click();
  await expect(dialog).toHaveCount(0);
  await expect(table.locator('tbody tr', { hasText: `TVSH E2E ${stamp}` })).toHaveCount(1);

  // A client, and a user who signs in with the name and password the owner sets.
  await addRow(page, 'partners', 'Clients', 'SME', client);
  await openRegister(page, 'users', 'Seller');
  await newRow(page, 'Staff', seller.name);
  await saveRow(page);
  const panel = page.locator('app-element-panel');
  await panel.getByRole('tab', { name: 'Sign-in' }).click();
  await field(panel, 'User name').locator('input').fill(seller.username);
  await field(panel, 'User name').getByRole('button', { name: 'Set' }).click();
  await toast(page, seller.username);
  await field(panel, 'New password').locator('input').fill(seller.password);
  await field(panel, 'New password').getByRole('button', { name: 'Set' }).click();
  await toast(page, /password/i);
  // Where the user works.
  await panel.getByRole('tab', { name: 'Cost centres' }).click();
  await choose(page, field(panel, 'Cost centres'), OWNER.company);
  await page.keyboard.press('Escape');
  await panel.getByRole('button', { name: 'Save' }).first().click();
  await toast(page, /saved|cost centre/i);

  // Without a right, the user's clients register shows nothing.
  let user = await asSeller(browser);
  await user.goto('/entities/partners');
  await expect(user.locator('app-register-page')).toContainText('No category is open to you.');
  await user.context().close();

  // The owner gives the user "read all" on Clients.
  await openMenu(page, 'Users and rights', 'Rights');
  const rights = page.locator('app-rights');
  await rights.locator('ui-chips button', { hasText: 'Users' }).click();
  await choose(page, field(rights, 'User'), seller.name, seller.name);
  await rights.locator('ui-checkbox[aria-label="Clients: readAll"], ui-checkbox:has(input[aria-label="Clients: readAll"])').first().click();
  await rights.getByRole('button', { name: 'Save' }).click();
  await toast(page, /saved/i);

  user = await asSeller(browser);
  await user.goto('/entities/partners');
  await user.locator('app-register-page nav.categories button.category', { hasText: 'Clients' }).first().click();
  await user.locator('app-register-page .search input').fill(client);
  await expect(user.locator('app-register-page ui-table tbody tr', { hasText: client })).toHaveCount(1);
  await expect(user.locator('app-register-page').getByRole('button', { name: 'New' })).toHaveCount(0);
});
