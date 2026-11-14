import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import { BankAspsp, BankConnection, BankConnectionsApi, ibanShown } from '../../core/banking';
import { errorsOf } from '../../core/entities';
import { t } from '../../core/i18n';
import { TranslatePipe } from '../../core/t.pipe';
import { Button, Field, Input } from '../../ui/controls';
import { formatDate } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';
import { Dialogs } from '../../ui/dialog';
import { COUNTRY_NAMES } from '../setup/setup-parts';

/** What the person is doing with one account: typing its IBAN, or choosing the bank to connect it at. */
type Editing = { bankId: string; kind: 'account'; iban: string; bic: string; holder: string } | { bankId: string; kind: 'connect'; country: string; aspsp: string | null; psu: string };

/**
 * The company's bank accounts on Bank statements (the founder, 2026-11-14): each with its IBAN, BIC and holder (typed by hand, or given by the bank), and
 * its connection to the bank through the company's open banking provider. A person connects an account by signing in at the bank, which sends them back
 * to the ERP; nothing reads the bank by itself.
 */
@Component({
  selector: 'app-bank-accounts',
  imports: [TranslatePipe, Button, Field, Icon, Input, Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>{{ 'Bank accounts' | t }}</h2>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @if (accounts().length) {
      <table>
        <thead><tr><th>{{ 'Bank' | t }}</th><th>{{ 'IBAN' | t }}</th><th>{{ 'Holder' | t }}</th><th>{{ 'Connection' | t }}</th><th></th></tr></thead>
        <tbody>
          @for (a of accounts(); track a.bankId) {
            <tr>
              <td>{{ a.bank }}</td>
              <td>{{ iban(a.iban) }} @if (a.currency) { <span class="muted">{{ a.currency }}</span> }</td>
              <td>{{ a.holderName }}</td>
              <td>
                @if (a.connected) {
                  {{ 'Connected at {0}' | t: (a.aspspName ?? '') }} · {{ 'until {0}' | t: day(a.validUntil) }}
                  @if (a.renew) { <strong>{{ 'Connect it again soon' | t }}</strong> }
                  @if (a.lastFetchedTo) { <span class="muted"> · {{ 'fetched to {0}' | t: day(a.lastFetchedTo) }}</span> }
                } @else if (a.choices.length) {
                  {{ 'Choose the account:' | t }}
                } @else if (a.pending) {
                  <span class="muted">{{ 'Waiting for the bank sign-in' | t }}</span>
                } @else {
                  <span class="muted">{{ 'Not connected' | t }}</span>
                }
              </td>
              <td>
                <span class="row-actions">
                  @if (a.connected) { <button uiButton size="small" [disabled]="busy()" (click)="fetch.emit(a)">{{ 'Fetch from bank' | t }}</button> }
                  <button uiButton size="small" [disabled]="busy()" (click)="editAccount(a)">{{ 'IBAN' | t }}</button>
                  <button uiButton size="small" [disabled]="busy()" (click)="startConnect(a)">{{ a.connected ? ('Connect again' | t) : ('Connect to bank' | t) }}</button>
                  @if (a.connected || a.pending) { <button uiButton variant="danger" size="small" [disabled]="busy()" (click)="disconnect(a)">{{ 'Disconnect' | t }}</button> }
                </span>
              </td>
            </tr>
            @for (c of a.choices; track c.uid) {
              <tr><td colspan="5"><button uiButton size="small" [disabled]="busy()" (click)="pick(a, c.uid)">{{ 'This one' | t }}</button> {{ iban(c.iban) }} · {{ c.currency }} · {{ c.name }}</td></tr>
            }
            @if (editing(); as e) {
              @if (e.bankId === a.bankId) {
                <tr>
                  <td colspan="5">
                    @if (e.kind === 'account') {
                      <div class="form">
                        <ui-field [label]="'IBAN' | t"><input uiInput [value]="e.iban" (input)="edit({ iban: $any($event.target).value })" /></ui-field>
                        <ui-field [label]="'BIC' | t"><input uiInput [value]="e.bic" (input)="edit({ bic: $any($event.target).value })" /></ui-field>
                        <ui-field [label]="'Holder' | t" [hint]="'As the bank knows it; a payer sees it.' | t"><input uiInput [value]="e.holder" (input)="edit({ holder: $any($event.target).value })" /></ui-field>
                      </div>
                      <button uiButton variant="primary" size="small" [disabled]="busy()" (click)="saveAccount(e)">{{ 'Save' | t }}</button>
                    } @else {
                      <div class="form">
                        <ui-field [label]="'Country' | t"><ui-select [options]="countries" [searchable]="true" [value]="e.country" (valueChange)="pickCountry($event)" /></ui-field>
                        <ui-field [label]="'Bank' | t"><ui-select [options]="aspspOptions()" [searchable]="true" [value]="e.aspsp" (valueChange)="edit({ aspsp: $event })" /></ui-field>
                        <ui-field [label]="'Account kind' | t"><ui-select [options]="psuOptions" [value]="e.psu" (valueChange)="edit({ psu: $event ?? 'business' })" /></ui-field>
                      </div>
                      <p class="muted">{{ 'You sign in at the bank, which then sends you back here.' | t }}</p>
                      <button uiButton variant="primary" size="small" [disabled]="busy() || !e.aspsp" (click)="connect(e)">{{ 'Go to the bank' | t }}</button>
                    }
                    <button uiButton variant="quiet" size="small" (click)="editing.set(null)">{{ 'Cancel' | t }}</button>
                  </td>
                </tr>
              }
            }
          }
        </tbody>
      </table>
    }
  `,
  styleUrl: './operations.scss',
})
export class BankAccounts {
  private readonly api = inject(BankConnectionsApi);
  private readonly dialogs = inject(Dialogs);

  /** Fetch a connected account's statement from its bank (Bank statements does it). */
  readonly fetch = output<BankConnection>();

  protected readonly accounts = signal<BankConnection[]>([]);
  protected readonly editing = signal<Editing | null>(null);
  protected readonly aspsps = signal<BankAspsp[]>([]);
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly iban = ibanShown;
  protected readonly countries: SelectOption[] = Object.entries(COUNTRY_NAMES).map(([code, name]) => ({ value: code, label: name })).sort((a, b) => a.label.localeCompare(b.label));
  protected readonly psuOptions: SelectOption[] = [{ value: 'business', label: t('Business account') }, { value: 'personal', label: t('Personal account') }];
  protected readonly aspspOptions = computed<SelectOption[]>(() => this.aspsps().map(a => ({ value: a.name, label: a.name })));

  constructor() {
    void this.reload();
  }

  async reload(): Promise<void> {
    await this.run(async () => this.accounts.set(await this.api.list()));
  }

  protected day(value: string | null): string {
    return value ? formatDate(value) : '';
  }

  private async run(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      await work();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected edit(change: Partial<{ iban: string; bic: string; holder: string; country: string; aspsp: string | null; psu: string }>): void {
    this.editing.update(e => (e ? ({ ...e, ...change } as Editing) : e));
  }

  protected editAccount(a: BankConnection): void {
    this.editing.set({ bankId: a.bankId, kind: 'account', iban: a.iban ?? '', bic: a.bic ?? '', holder: a.holderName ?? '' });
  }

  protected saveAccount(e: Editing & { kind: 'account' }): Promise<void> {
    return this.run(async () => {
      await this.api.setAccount(e.bankId, e.iban, e.bic, e.holder);
      this.editing.set(null);
      this.accounts.set(await this.api.list());
    });
  }

  protected startConnect(a: BankConnection): Promise<void> {
    const country = a.aspspCountry ?? (a.iban && /^[A-Z]{2}/.test(a.iban) ? a.iban.slice(0, 2) : 'FI');
    this.editing.set({ bankId: a.bankId, kind: 'connect', country, aspsp: a.aspspName, psu: 'business' });
    return this.loadAspsps(country);
  }

  protected pickCountry(country: string | null): Promise<void> {
    if (!country) {
      return Promise.resolve();
    }
    this.edit({ country, aspsp: null });
    return this.loadAspsps(country);
  }

  private loadAspsps(country: string): Promise<void> {
    return this.run(async () => this.aspsps.set(await this.api.aspsps(country)));
  }

  /** Off to the bank: the ERP page comes back at the return address and finishes it. */
  protected connect(e: Editing & { kind: 'connect' }): Promise<void> {
    return this.run(async () => {
      const link = await this.api.connect(e.bankId, e.aspsp!, e.country, e.psu);
      window.location.assign(link.url);
    });
  }

  protected pick(a: BankConnection, uid: string): Promise<void> {
    return this.run(async () => {
      await this.api.pick(a.bankId, uid);
      this.accounts.set(await this.api.list());
    });
  }

  protected async disconnect(a: BankConnection): Promise<void> {
    const yes = await this.dialogs.confirm({ title: t('Disconnect this account?'), message: t('Its statements can no longer be fetched until it is connected again. The IBAN stays.'),
      confirmLabel: t('Disconnect'), danger: true });
    if (!yes) {
      return;
    }
    await this.run(async () => {
      await this.api.disconnect(a.bankId);
      this.accounts.set(await this.api.list());
    });
  }
}
