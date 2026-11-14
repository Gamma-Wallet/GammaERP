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
