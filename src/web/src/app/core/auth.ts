import { DOCUMENT, Injectable, inject } from '@angular/core';
import { language } from './i18n';
import { challengeOf, randomString } from './pkce';

/** The API's client for the screens: public, authorization code with PKCE (registered by the API with the redirect below). */
export const CLIENT_ID = 'gamma-erp-web';
const TOKENS = 'gamma-erp.tokens';
const FLOW = 'gamma-erp.sign-in';

interface Tokens {
  access: string;
  refresh?: string;
  /** When the access token expires, in ms since the epoch. */
  expiresAt: number;
}

interface Flow {
  verifier: string;
  state: string;
  returnTo: string;
}

/**
 * Sign-in through the API's own OpenID Connect server: the browser goes to its sign-in page and comes back to /signed-in with a code,
 * exchanged here for the tokens. The tokens live in this tab's session storage only; the access token is refreshed shortly before it expires.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly window = inject(DOCUMENT).defaultView!;
  private refreshing: Promise<string | null> | null = null;

  get redirectUri(): string {
    return `${this.window.location.origin}/signed-in`;
  }

  isSignedIn(): boolean {
    return this.read() !== null;
  }

  /** Leaves for the API's sign-in page; after it, the browser comes back to returnTo. */
  /** Starts the sign-in; a login hint fills in the user name on the sign-in page (after a sign-up). */
  async signIn(returnTo = '/', loginHint?: string): Promise<void> {
    const flow: Flow = { verifier: randomString(), state: randomString(16), returnTo: returnTo.startsWith('/') ? returnTo : '/' };
    sessionStorage.setItem(FLOW, JSON.stringify(flow));
    const query = new URLSearchParams({
      client_id: CLIENT_ID,
      response_type: 'code',
      scope: 'openid offline_access',
      redirect_uri: this.redirectUri,
      code_challenge: await challengeOf(flow.verifier),
      code_challenge_method: 'S256',
      state: flow.state,
      ...(loginHint ? { login_hint: loginHint } : {}),
      ...(language() === 'en' ? {} : { ui_locales: language() }),
    });
    this.window.location.assign(`/connect/authorize?${query}`);
  }

  /** Back from the sign-in page: checks the state, exchanges the code; gives where to go next. */
  async completeSignIn(params: URLSearchParams): Promise<string> {
    const flow = JSON.parse(sessionStorage.getItem(FLOW) ?? 'null') as Flow | null;
    sessionStorage.removeItem(FLOW);
    const code = params.get('code');
    if (!flow || !code || params.get('state') !== flow.state) {
      throw new Error('The sign-in answer does not match the sign-in that was started.');
    }
    await this.token({ grant_type: 'authorization_code', code, redirect_uri: this.redirectUri, code_verifier: flow.verifier });
    return flow.returnTo;
  }

  /** A valid access token, refreshed when it is about to expire; null when signed out. */
  async accessToken(): Promise<string | null> {
    const tokens = this.read();
    if (!tokens) {
      return null;
    }
    if (tokens.expiresAt - Date.now() > 30_000) {
      return tokens.access;
    }
    if (!tokens.refresh) {
      this.clear();
      return null;
    }
    this.refreshing ??= this.token({ grant_type: 'refresh_token', refresh_token: tokens.refresh })
      .then(t => t.access)
      .catch(() => {
        this.clear();
        return null;
      })
      .finally(() => (this.refreshing = null));
    return this.refreshing;
  }

  /** Forgets the tokens and ends the API's sign-in session, coming back to /signed-in. */
  signOut(): void {
    this.clear();
    const query = new URLSearchParams({ post_logout_redirect_uri: this.redirectUri, client_id: CLIENT_ID });
    this.window.location.assign(`/connect/logout?${query}`);
  }

  clear(): void {
    sessionStorage.removeItem(TOKENS);
  }

  private async token(body: Record<string, string>): Promise<Tokens> {
    const response = await fetch('/connect/token', {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({ client_id: CLIENT_ID, ...body }),
    });
    if (!response.ok) {
      throw new Error(`The token endpoint answered ${response.status}.`);
    }
    const json = (await response.json()) as { access_token: string; refresh_token?: string; expires_in: number };
    const tokens: Tokens = { access: json.access_token, refresh: json.refresh_token, expiresAt: Date.now() + json.expires_in * 1000 };
    sessionStorage.setItem(TOKENS, JSON.stringify(tokens));
    return tokens;
  }

  private read(): Tokens | null {
    try {
      return JSON.parse(sessionStorage.getItem(TOKENS) ?? 'null') as Tokens | null;
    } catch {
      return null;
    }
  }
}
