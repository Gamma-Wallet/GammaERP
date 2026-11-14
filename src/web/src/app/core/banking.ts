import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { t } from './i18n';

/** An adapter the ERP has for an open banking provider. */
export interface BankProviderKind {
  kind: string;
  name: string;
  apiBase: string;
}

export interface BankProviderChoices {
  kinds: BankProviderKind[];
  environments: string[];
  maxAccessDays: number;
}

/** A company's provider as the server shows it: never the key, only whether one is set and still readable. */
export interface BankProvider {
  id: string;
  kind: string;
  name: string | null;
  environment: string;
  apiBase: string;
  applicationId: string;
  keySet: boolean;
  keyFingerprint: string | null;
  keyReadable: boolean;
  redirectUrl: string;
  accessDays: number;
}

/** What the open banking fields collect; an empty key keeps the key already set (on a change). */
export interface BankProviderModel {
  kind: string;
  environment: string;
  applicationId: string;
  privateKey: string;
  keyFileName: string;
  redirectUrl: string;
  accessDays: number;
}

/** The ERP's own page the bank sends the person back to. */
export function defaultRedirectUrl(origin: string): string {
  return `${origin.replace(/\/$/, '')}/bank-connected`;
}

export function emptyBankProvider(origin: string): BankProviderModel {
  return { kind: 'EnableBanking', environment: 'Sandbox', applicationId: '', privateKey: '', keyFileName: '', redirectUrl: defaultRedirectUrl(origin), accessDays: 180 };
}

export function bankProviderModelOf(p: BankProvider): BankProviderModel {
  return { kind: p.kind, environment: p.environment, applicationId: p.applicationId, privateKey: '', keyFileName: '', redirectUrl: p.redirectUrl, accessDays: p.accessDays };
}

/** Why the fields can't be saved yet; null when they can. A new provider needs its key; a change may keep the one set. */
export function bankProviderProblem(m: BankProviderModel, keySet: boolean): string | null {
  if (!m.applicationId.trim()) {
    return t('Give the application id.');
  }
  if (!m.privateKey.trim() && !keySet) {
    return t("Choose the application's private key (.pem file).");
  }
  if (!/^https?:\/\/\S+$/i.test(m.redirectUrl.trim())) {
    return t('Give the full address the bank sends people back to.');
  }
  if (!(m.accessDays >= 1 && m.accessDays <= 180)) {
    return t('A bank connection asks for 1 to 180 days.');
  }
  return null;
}

export function bankProviderInputOf(m: BankProviderModel): object {
  return {
    kind: m.kind, applicationId: m.applicationId.trim(), privateKey: m.privateKey.trim() || null, redirectUrl: m.redirectUrl.trim(), environment: m.environment,
    accessDays: m.accessDays,
  };
}

/** Whether a file's text looks like a PEM private key (the server checks it properly). */
export function looksLikePrivateKey(text: string): boolean {
  return /-----BEGIN (RSA )?PRIVATE KEY-----/.test(text);
}

@Injectable({ providedIn: 'root' })
export class BankingApi {
  private readonly http = inject(HttpClient);

  choices(): Promise<BankProviderChoices> {
    return firstValueFrom(this.http.get<BankProviderChoices>('/api/v1/setup/bank-providers'));
  }

  /** The company's provider; null when it has none. */
  async provider(): Promise<BankProvider | null> {
    return (await firstValueFrom(this.http.get<BankProvider | null>('/api/v1/open-banking/provider'))) ?? null;
  }

  save(m: BankProviderModel): Promise<BankProvider> {
    return firstValueFrom(this.http.put<BankProvider>('/api/v1/open-banking/provider', bankProviderInputOf(m)));
  }

  switchOff(): Promise<unknown> {
    return firstValueFrom(this.http.delete('/api/v1/open-banking/provider', { headers: new HttpHeaders() }));
  }
}

/** An account the bank gave when the person signed in. */
export interface BankAccountChoice {
  uid: string;
  iban: string | null;
  bic: string | null;
  currency: string | null;
  name: string | null;
}

/** A bank account cost centre with its identity and its connection to the bank. */
export interface BankConnection {
  bankId: string;
  bank: string | null;
  iban: string | null;
  bic: string | null;
  holderName: string | null;
  currency: string | null;
  connected: boolean;
  aspspName: string | null;
  aspspCountry: string | null;
  validUntil: string | null;
  renew: boolean;
  lastFetchedTo: string | null;
  pending: boolean;
  choices: BankAccountChoice[];
}

/** What a fetch took, and the bank's balance next to the ledger's. */
export interface BankFetch {
  id: string | null;
  from: string;
  to: string;
  added: number;
  skipped: number;
  pending: number;
  bankBalance: number | null;
  bankBalanceDate: string | null;
  ledgerBalance: number;
}

/** A bank the company's provider reaches. */
export interface BankAspsp {
  name: string;
  country: string;
  psuTypes: string[];
}

/** An IBAN shown in groups of four. */
export function ibanShown(iban: string | null): string {
  return (iban ?? '').replace(/(.{4})/g, '$1 ').trim();
}

@Injectable({ providedIn: 'root' })
export class BankConnectionsApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/bank-connections';

  list(): Promise<BankConnection[]> {
    return firstValueFrom(this.http.get<BankConnection[]>(this.base));
  }

  setAccount(bankId: string, iban: string, bic: string, holderName: string): Promise<BankConnection> {
    return firstValueFrom(this.http.put<BankConnection>(`${this.base}/${bankId}/account`, { iban: iban || null, bic: bic || null, holderName: holderName || null }));
  }

  aspsps(country: string): Promise<BankAspsp[]> {
    return firstValueFrom(this.http.get<BankAspsp[]>(`${this.base}/aspsps`, { params: { country } }));
  }

  /** Where the person signs in at the bank. */
  connect(bankId: string, aspspName: string, country: string, psuType: string): Promise<{ url: string }> {
    return firstValueFrom(this.http.post<{ url: string }>(`${this.base}/${bankId}/connect`, { aspspName, country, psuType }));
  }

  finish(state: string, code: string): Promise<BankConnection> {
    return firstValueFrom(this.http.post<BankConnection>(`${this.base}/finish`, { state, code }));
  }

  pick(bankId: string, uid: string): Promise<BankConnection> {
    return firstValueFrom(this.http.post<BankConnection>(`${this.base}/${bankId}/pick/${encodeURIComponent(uid)}`, null));
  }

  /** Fetches the account's booked lines from its bank into a new statement (none when nothing is new). */
  fetch(bankId: string): Promise<BankFetch> {
    return firstValueFrom(this.http.post<BankFetch>(`${this.base}/${bankId}/fetch`, null));
  }

  disconnect(bankId: string): Promise<BankConnection> {
    return firstValueFrom(this.http.post<BankConnection>(`${this.base}/${bankId}/disconnect`, null));
  }
}
