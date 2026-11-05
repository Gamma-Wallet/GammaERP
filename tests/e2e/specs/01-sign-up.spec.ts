import { expect, test } from '@playwright/test';
import { choose, field, stamp } from './support';

/** Sign-up from the sign-in page through every step of the wizard, then the new owner's first sign-in and entry page. */
test('a new business signs up, signs in with its name filled in and is asked to finish its company', async ({ page }) => {
  const owner = `e2e.bar.${stamp.toLowerCase()}`;
  const password = 'e2e-bar-password-1';
  await page.goto('/');
  await page.waitForURL('**/account/login**');
  await page.click('a:has-text("Create an account")');
  const wizard = page.locator('app-sign-up');
  await expect(wizard.locator('.card h1')).toHaveText('Account');
  const next = () => wizard.getByRole('button', { name: 'Next', exact: true }).click();

  await field(wizard, 'User name').locator('input').fill(owner);
  await expect(field(wizard, 'User name').locator('.hint')).toHaveText('Free');
  await field(wizard, 'Password').locator('input').fill(password);
  await field(wizard, 'Password again').locator('input').fill('something-else-1');
  await expect(wizard.locator('.problem')).toHaveCount(0);
  await expect(wizard.getByRole('button', { name: 'Next', exact: true })).toBeDisabled();
  await field(wizard, 'Password again').locator('input').fill(password);
  await next();

  await choose(page, field(wizard, 'Country'), 'Albania', 'Alb');
  await expect(wizard.locator('.card > p.muted').last()).toContainText('ALL');
  await next();
  await field(wizard, 'Company name').locator('input').fill(`E2E Bar ${stamp}`);
  await next();
  await expect(wizard.locator('app-activity-picker')).toBeVisible();
  await next();
  await expect(wizard.locator('app-groups-picker input[type=checkbox]').first()).toBeChecked();
  await next();
  await wizard.getByRole('button', { name: 'User' }).click();
  await wizard.locator('input[aria-label="User name 1"]').fill(`e2e.waiter.${stamp.toLowerCase()}`);
  await wizard.locator('input[aria-label="Password 1"]').fill('e2e-waiter-password-1');
  await next();

  await expect(wizard.locator('dl.review')).toContainText(owner);
  await expect(wizard.locator('dl.review')).toContainText(`E2E Bar ${stamp}`);
  await wizard.getByRole('button', { name: 'Create my company' }).click();
  await expect(wizard.locator('.done h1')).toHaveText('Your company is ready', { timeout: 120_000 });

  // Back to sign-in with the user name filled in.
  await wizard.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL('**/account/login**');
  await expect(page.locator('input[name=username]')).toHaveValue(owner);
  await page.fill('input[name=password]', password);
  await page.click('button[type=submit]');
  await expect(page.locator('app-shell nav.menu')).toBeVisible();
  await expect(page.locator('app-home .finish')).toBeVisible();

  // A single company has no group to add another company to.
  await page.goto('/new-company');
  await expect(page.locator('app-new-company .card')).toContainText('single company');

  // The user name is taken now.
  await page.context().clearCookies();
  await page.goto('/sign-up');
  await field(page.locator('app-sign-up'), 'User name').locator('input').fill(owner);
  await expect(field(page.locator('app-sign-up'), 'User name').locator('.error')).toHaveText('This user name is taken.');
});
