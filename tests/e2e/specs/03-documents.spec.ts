import { expect, test } from '@playwright/test';
import { OWNER, addLine, addRow, api, choose, newDocument, openRegister, signIn, stamp, toast } from './support';

/** A sale on an account line: saved, posted, printed and reversed, the reversal pointing back at it. */
test('a sale is posted, printed and reversed', async ({ page }) => {
  const client = `Klient Shitje ${stamp}`;
  await signIn(page);
  await addRow(page, 'partners', 'Clients', 'SME', client);

  await newDocument(page, 'Documents', 'Sales', 'FSHM');
  const editor = page.locator('app-document-editor');
  await choose(page, editor.locator('ui-field.side').nth(0), '');
  await choose(page, editor.locator('ui-field.side').nth(1), client, client);
  await addLine(page, 'Account', '705', '100');
  await editor.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(editor.locator('h1')).toContainText('FSHM');
  await expect(editor.locator('.totals')).toContainText('120.00');

  await editor.getByRole('button', { name: 'Post', exact: true }).click();
  await page.locator('ui-confirm').getByRole('button', { name: 'Post' }).click();
  await toast(page, 'posted');
  await expect(editor.getByRole('button', { name: /Reverse/ })).toBeVisible();

  const id = page.url().split('/').pop()!;
  expect(await api(page, `/api/v1/documents/${id}/pdf`)).toEqual({ status: 200, head: '%PDF' });

  await editor.getByRole('button', { name: /Reverse/ }).click();
  await page.locator('app-reverse-dialog').getByRole('button', { name: 'Reverse' }).click();
  await toast(page, 'reversed by');
  await expect(editor.locator('h1')).not.toContainText(/FSHM\d/);
  await expect(editor.locator('.head, header').first()).toContainText(/Reversal of FSHM\d/);
  await expect(editor.locator('.totals')).toContainText('120.00');
});

/** A new product gets its unit in the Units tab, is bought into the company and sold from it; the stock report shows what is left. */
test('goods bought into the company are sold from it, its stock going in and out', async ({ page }) => {
  const [product, supplier, client] = [`Kafe E2E ${stamp}`, `Furnitori ${stamp}`, `Klient Mall ${stamp}`];
  await signIn(page);
  await addRow(page, 'partners', 'Suppliers', 'SME', supplier);
  await addRow(page, 'partners', 'Clients', 'SME', client);
  await addRow(page, 'products', 'Products', 'Goods', product);

  // Its unit: none of its own yet.
  const panel = page.locator('app-element-panel');
  await panel.getByRole('tab', { name: 'Units' }).click();
  const units = panel.locator('app-product-units');
  await choose(page, units.locator('.add'), 'Piece', 'Piece');
  await expect(units.locator('.units li', { hasText: 'Piece' })).toContainText('Default');
  await units.getByRole('button', { name: 'Save the units' }).click();
  await toast(page, 'Units saved.');

  const editor = page.locator('app-document-editor');
  async function goods(group: string, list: string, code: string, from: string, to: string, quantity: string, price: string): Promise<void> {
    await newDocument(page, group, list, code);
    await choose(page, editor.locator('ui-field.side').nth(0), from, from);
    await choose(page, editor.locator('ui-field.side').nth(1), to, to);
    await addLine(page, 'Item', product, price);
    const row = editor.locator('app-line-grid tbody tr').last();
    await expect(row.locator('ui-select[aria-label="Unit"], td').nth(3)).toContainText('Piece');
    await row.locator('input[aria-label=Quantity]').fill(quantity);
    await editor.getByRole('button', { name: 'Post', exact: true }).click();
    await page.locator('ui-confirm').getByRole('button', { name: 'Post' }).click();
    await toast(page, 'posted');
  }

  await goods('Documents', 'Purchases', 'FBM', supplier, OWNER.company, '10', '50');
  await goods('Documents', 'Sales', 'FSHM', OWNER.company, client, '4', '80');

  // The stock left: 10 in, 4 out.
  await page.goto('/reports/stock');
  await page.locator('app-report-page .run button').click();
  await expect(page.locator('app-report-page ui-table tbody tr', { hasText: product })).toContainText('6');

  // Bought in pieces: the unit is in use now and cannot be removed.
  await openRegister(page, 'products', 'Products');
  await page.locator('app-register-page .search input').fill(product);
  await page.locator('app-register-page ui-table tbody tr', { hasText: product }).first().click();
  await panel.getByRole('tab', { name: 'Units' }).click();
  await expect(units.locator('.units li', { hasText: 'Piece' })).toContainText('In use');
  await expect(units.getByRole('button', { name: 'Remove Piece' })).toBeDisabled();
});
