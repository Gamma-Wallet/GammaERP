import { Page, expect, test } from '@playwright/test';
import { addRow, choose, download, field, postSale, signIn, stamp } from './support';

/** Opens a report, sets its choice filters, runs it. */
async function run(page: Page, report: string, filters: [label: string, option: string, multiple?: boolean][]): Promise<void> {
  await page.goto(`/reports/${report}`);
  const screen = page.locator('app-report-page');
  for (const [label, option, multiple] of filters) {
    await choose(page, field(screen.locator('.filters'), label), option, option);
    if (multiple) {
      await page.keyboard.press('Escape');
    }
  }
  await screen.locator('.run button').click();
}

async function exports(page: Page): Promise<void> {
  const buttons = page.locator('app-report-page');
  await download(page, () => buttons.getByRole('button', { name: /Excel/ }).click(), 'xlsx');
  await download(page, () => buttons.getByRole('button', { name: /PDF/ }).click(), 'pdf');
}

/** The reports read what was posted: a client's ledger, the VAT return and the trial balance, each with its Excel and PDF. */
test("a posted sale shows in the client's ledger, the VAT return and the trial balance, each exported", async ({ page }) => {
  const client = `Klient Raport ${stamp}`;
  await signIn(page);
  await addRow(page, 'partners', 'Clients', 'SME', client);
  await postSale(page, client, '250');
  const table = page.locator('app-report-page ui-table tbody');

  await run(page, 'partner-ledger', [['Partner', client]]);
  await expect(page.locator('app-report-page ui-kpi', { hasText: 'Closing balance' })).toContainText('300.00');
  await expect(table.locator('tr', { hasText: 'FSHM' })).toContainText('300.00');
  await exports(page);

  await run(page, 'vat-return', [['Sales (output VAT)', 'Sales', true]]);
  await expect(page.locator('app-report-page')).toContainText('50.00');
  await exports(page);

  await run(page, 'trial-balance', []);
  await expect(table.locator('tr', { hasText: '705' }).first()).toBeVisible();
  await exports(page);
});
