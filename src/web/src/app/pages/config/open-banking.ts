import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BankProvider, BankProviderChoices, BankProviderModel, BankingApi, CardProvider, CardProviderModel, WalletProvider, bankProviderModelOf, bankProviderProblem, bankResponseUrl,
  cardCallbackUrl, cardProviderModelOf, emptyBankProvider } from '../../core/banking';
import { Field, Input } from '../../ui/controls';
import { errorsOf } from '../../core/entities';
import { t } from '../../core/i18n';
import { TranslatePipe } from '../../core/t.pipe';
import { Button } from '../../ui/controls';
import { Dialogs } from '../../ui/dialog';
import { Icon } from '../../ui/icon';
import { Toasts } from '../../ui/toast';
import { BankProviderFields } from '../setup/bank-provider-fields';

/**
 * Currencies and payments › Open banking (the founder, 2026-11-14): the company's own open banking provider, set when the company was made or here.
 * Administrators only. The private key is sent once and never shown again: the page shows only that a key is set, its fingerprint, and whether the
 * server can still open it.
 */
@Component({
  selector: 'app-open-banking',
  imports: [TranslatePipe, BankProviderFields, Button, Icon, Field, Input],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Open banking' | t }}</h1></div>
    <p class="muted intro">{{ 'The company\\'s own provider reads its bank accounts, so bank statements can be fetched from the bank and payments by bank checked at the till. The company has its own contract with the provider; Gamma holds none.' | t }}</p>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @if (loaded()) {
      @if (current(); as p) {
        <dl class="review">
          <dt>{{ 'Provider' | t }}</dt><dd>{{ p.name ?? p.kind }} · {{ p.environment }}</dd>
          <dt>{{ 'Application id' | t }}</dt><dd>{{ p.applicationId }}</dd>
          <dt>{{ 'Private key' | t }}</dt>
          <dd>@if (p.keyReadable) { {{ 'Set' | t }} ({{ p.keyFingerprint }}) } @else { <strong>{{ 'The server can no longer open the key: choose the .pem file again.' | t }}</strong> }</dd>
          <dt>{{ 'Return address' | t }}</dt><dd>{{ p.redirectUrl }}</dd>
          <dt>{{ 'Days a bank connection lasts' | t }}</dt><dd>{{ p.accessDays }}</dd>
          <dt>{{ 'API address' | t }}</dt><dd>{{ p.apiBase }}</dd>
          <dt>{{ 'Bank responses' | t }}</dt>
          <dd>@if (p.responseSecretSet) { {{ 'Signed messages taken at' | t }} <code>{{ responseUrl(p) }}</code> } @else { {{ 'Not set: the till uses Check bank.' | t }} }</dd>
        </dl>
      } @else {
        <p>{{ 'The company has no open banking provider yet.' | t }}</p>
      }
      <section class="card">
        <h2>{{ current() ? ('Change it' | t) : ('Set it' | t) }}</h2>
        <app-bank-provider-fields [value]="model()" [choices]="choices()" [keySet]="!!current()?.keyReadable" [fingerprint]="current()?.keyFingerprint ?? null"
          [responseSecretSet]="!!current()?.responseSecretSet"
          (valueChange)="model.set($event)" />
        @if (problem()) { <p class="muted problem">{{ problem() }}</p> }
        <footer>
          @if (current()) { <button uiButton variant="quiet" [disabled]="busy()" (click)="switchOff()">{{ 'Switch off…' | t }}</button> }
          <span class="spacer"></span>
          <button uiButton variant="primary" [disabled]="busy() || !!problem()" (click)="save()">{{ 'Save' | t }}</button>
        </footer>
      </section>
      <section class="card">
        <h2>{{ 'Gamma Wallet' | t }}</h2>
        <p class="muted">{{ 'Paid till bills are sent to the Gamma business, so the customer gets the reward: by scanning its QR, or at once when they paid through the payment link in Gamma Wallet. The token is made in Gamma Business, Integrations.' | t }}</p>
        @if (wallet(); as w) {
          <dl class="review">
            <dt>{{ 'Business' | t }}</dt><dd>{{ w.businessName }} · {{ w.currencyCode }} · @if (w.canClaim) { {{ 'rewards on' | t }} } @else { <strong>{{ 'no active reward service' | t }}</strong> }</dd>
            <dt>{{ 'Token' | t }}</dt><dd>{{ w.tokenFingerprint }}</dd>
            <dt>{{ 'API address' | t }}</dt><dd>{{ w.apiBase }}</dd>
          </dl>
        }
        <ui-field [label]="'Integration token' | t" [required]="!wallet()?.tokenSet" [hint]="wallet()?.tokenSet ? ('Set. Type a new one only to replace it.' | t) : ('GWINT_ and the rest, from Gamma Business, Integrations.' | t)">
          <input uiInput type="password" autocomplete="off" [value]="walletToken()" (input)="walletToken.set($any($event.target).value)" />
        </ui-field>
        <ui-field [label]="'API address' | t" [hint]="'Empty: Gamma Wallet itself.' | t">
          <input uiInput [value]="walletApi()" (input)="walletApi.set($any($event.target).value)" />
        </ui-field>
        <footer>
          <span class="spacer"></span>
          <button uiButton variant="primary" [disabled]="busy() || (!walletToken().trim() && !wallet()?.tokenSet)" (click)="saveWallet()">{{ 'Save' | t }}</button>
        </footer>
      </section>
      <section class="card">
        <h2>{{ 'Card payments (JCC)' | t }}</h2>
        <p class="muted">{{ 'The shop takes cards through its own JCC merchant account: a payment method with provider kind Jcc shows the payment link at the till, and its page offers Pay by card.' | t }}</p>
        @if (card(); as c) {
          <dl class="review">
            <dt>{{ 'API user' | t }}</dt><dd>{{ c.apiUser }} · {{ c.environment }}</dd>
            <dt>{{ 'Password' | t }}</dt><dd>@if (c.passwordReadable) { {{ 'Set' | t }} } @else { <strong>{{ 'Enter it again' | t }}</strong> }</dd>
            <dt>{{ 'Callbacks' | t }}</dt><dd>@if (c.callbackKeySet) { {{ 'Checked with the key, taken at' | t }} <code>{{ callbackUrl(c) }}</code> } @else { {{ 'No key: callbacks are refused; the till uses Check card.' | t }} }</dd>
          </dl>
        }
        <ui-field [label]="'API user' | t" [required]="true" [hint]="'The merchant API user JCC gave (login-api).' | t">
          <input uiInput [value]="cardModel().apiUser" (input)="setCard({ apiUser: $any($event.target).value })" />
        </ui-field>
        <ui-field [label]="'Password' | t" [required]="!card()?.passwordSet" [hint]="card()?.passwordSet ? ('Set. Type a new one only to replace it.' | t) : ''">
          <input uiInput type="password" autocomplete="off" [value]="cardModel().password" (input)="setCard({ password: $any($event.target).value })" />
        </ui-field>
        <ui-field [label]="'Callback key' | t" [hint]="'The key JCC signs its callbacks with (checksum).' | t">
          <input uiInput type="password" autocomplete="off" [value]="cardModel().callbackKey" (input)="setCard({ callbackKey: $any($event.target).value })" />
        </ui-field>
        <ui-field [label]="'Return address' | t" [required]="true" [hint]="'Where JCC sends the payer back: the ERP address and /pay/card-return.' | t">
          <input uiInput [value]="cardModel().returnUrl" (input)="setCard({ returnUrl: $any($event.target).value })" />
        </ui-field>
        <ui-field [label]="'API address' | t" [hint]="'Empty: JCC test gateway. Another https address only for a copy of the gateway (e.g. a test simulator).' | t">
          <input uiInput [value]="cardModel().apiBase" (input)="setCard({ apiBase: $any($event.target).value })" />
        </ui-field>
        <footer>
          @if (card()) { <button uiButton variant="quiet" [disabled]="busy()" (click)="switchOffCard()">{{ 'Switch off…' | t }}</button> }
          <span class="spacer"></span>
          <button uiButton variant="primary" [disabled]="busy() || !cardModel().apiUser.trim()" (click)="saveCard()">{{ 'Save' | t }}</button>
        </footer>
      </section>
    }
  `,
  styles: `
    .head { display: flex; align-items: center; gap: var(--space-3); }
    .intro { max-width: 70ch; }
    .review { display: grid; grid-template-columns: max-content 1fr; gap: var(--space-cell) var(--space-4); }
    .card { margin-top: var(--space-4); max-width: 640px; }
    footer { display: flex; gap: var(--space-2); margin-top: var(--space-3); }
    .spacer { flex: 1; }
  `,
})
export class OpenBanking {
  private readonly api = inject(BankingApi);
  private readonly toasts = inject(Toasts);
  private readonly dialogs = inject(Dialogs);

  protected readonly current = signal<BankProvider | null>(null);
  protected readonly choices = signal<BankProviderChoices | null>(null);
  protected readonly model = signal<BankProviderModel>(emptyBankProvider(window.location.origin));
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly problem = computed(() => bankProviderProblem(this.model(), !!this.current()?.keyReadable));

  constructor() {
    void this.load();
  }

  protected readonly wallet = signal<WalletProvider | null>(null);
  protected readonly walletToken = signal('');
  protected readonly walletApi = signal('');

  protected async saveWallet(): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      this.wallet.set(await this.api.saveWalletProvider(this.walletToken(), this.walletApi()));
      this.walletToken.set('');
      this.toasts.good(t('Gamma Wallet connected.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected readonly card = signal<CardProvider | null>(null);
  protected readonly cardModel = signal<CardProviderModel>(cardProviderModelOf(null, window.location.origin));

  protected callbackUrl(c: CardProvider): string {
    return cardCallbackUrl(window.location.origin, c.id);
  }

  protected setCard(change: Partial<CardProviderModel>): void {
    this.cardModel.set({ ...this.cardModel(), ...change });
  }

  private showCard(card: CardProvider | null): void {
    this.card.set(card);
    this.cardModel.set(cardProviderModelOf(card, window.location.origin));
  }

  protected async saveCard(): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      this.showCard(await this.api.saveCardProvider(this.cardModel()));
      this.toasts.good(t('Card payments saved.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async switchOffCard(): Promise<void> {
    const yes = await this.dialogs.confirm({ title: t('Switch card payments off?'), message: t('The payment link can no longer take cards until a gateway is set again. Nothing is deleted.'),
      confirmLabel: t('Switch off'), danger: true });
    if (!yes) {
      return;
    }
    this.busy.set(true);
    try {
      await this.api.switchOffCardProvider();
      this.showCard(null);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected responseUrl(p: BankProvider): string {
    return bankResponseUrl(window.location.origin, p.id);
  }

  private async load(): Promise<void> {
    try {
      const [choices, current, card, wallet] = await Promise.all([this.api.choices(), this.api.provider(), this.api.cardProvider(), this.api.walletProvider()]);
      this.wallet.set(wallet);
      this.walletApi.set(wallet?.apiBase ?? '');
      this.choices.set(choices);
      this.show(current);
      this.showCard(card);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.loaded.set(true);
    }
  }

  private show(current: BankProvider | null): void {
    this.current.set(current);
    this.model.set(current ? bankProviderModelOf(current) : emptyBankProvider(window.location.origin));
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      this.show(await this.api.save(this.model()));
      this.toasts.good(t('Open banking saved.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async switchOff(): Promise<void> {
    const yes = await this.dialogs.confirm({ title: t('Switch open banking off?'), message: t('Bank statements can no longer be fetched until a provider is set again. Nothing is deleted: its history stays.'),
      confirmLabel: t('Switch off'), danger: true });
    if (!yes) {
      return;
    }
    this.busy.set(true);
    this.message.set(null);
    try {
      await this.api.switchOff();
      this.show(null);
      this.toasts.good(t('Open banking switched off.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
