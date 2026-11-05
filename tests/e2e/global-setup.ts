import { FullConfig } from '@playwright/test';
import { OWNER } from './specs/support';

/** The company the specs work in: signed up through the public sign-up API (spec 01 goes through the wizard itself). */
export default async function globalSetup(config: FullConfig): Promise<void> {
  const base = config.projects[0].use.baseURL!;
  const made = await fetch(`${base}/api/v1/setup/sign-up`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      username: OWNER.username, password: OWNER.password, country: 'AL', companyName: OWNER.company, activity: 'GENERAL', fromCatalogue: false,
      cashDesks: ['Arka'],
    }),
  });
  // A stack kept from an earlier run (KEEP_STACK=1) already has it.
  if (!made.ok && made.status !== 409 && !(await made.text()).includes('taken')) {
    throw new Error(`The e2e company could not be signed up (${made.status}).`);
  }
}
