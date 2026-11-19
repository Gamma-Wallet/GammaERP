import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { BankingApi, CardProvider, CardProviderModel, cardCallbackUrl, cardProviderModelOf } from '../../core/banking';
import { Button, Field, Input } from '../../ui/controls';
import { errorsOf } from '../../core/entities';
import { t } from '../../core/i18n';
import { TranslatePipe } from '../../core/t.pipe';
import { Dialogs } from '../../ui/dialog';
import { Icon } from '../../ui/icon';
import { Toasts } from '../../ui/toast';

/**
 * Currencies and payments › Card payments (the founder, 2026-11-19: card payments are not open banking, so they have their own page): the
 * company's own card gateway (JCC). Administrators only. The password and the callback key are sent, never shown again.
 */
@Component({
  selector: 'app-card-payments',
  imports: [TranslatePipe, Button, Icon, Field, Input],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Card payments' | t }}</h1></div>
    <p class="muted intro">{{ 'The shop takes cards through its own JCC merchant account: a payment method with provider kind Jcc shows the payment link at the till, and its page offers Pay by card. The company has its own contract with JCC; Gamma holds none.' | t }}</p>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @if (loaded()) {
      @if (card(); as c) {
        <dl class="review">
          <dt>{{ 'Gateway' | t }}</dt><dd>JCC · {{ c.environment }}</dd>
          <dt>{{ 'API user' | t }}</dt><dd>{{ c.apiUser }}</dd>
          <dt>{{ 'Password' | t }}</dt><dd>@if (c.passwordReadable) { {{ 'Set' | t }} } @else { <strong>{{ 'Enter it again' | t }}</strong> }</dd>
          <dt>{{ 'Callbacks' | t }}</dt><dd>@if (c.callbackKeySet) { {{ 'Checked with the key, taken at' | t }} <code>{{ callbackUrl(c) }}</code> } @else { {{ 'No key: callbacks are refused; the till uses Check card.' | t }} }</dd>
        </dl>
      } @else {
        <p>{{ 'The company has no card gateway yet.' | t }}</p>
      }
      <section class="card">
        <h2>{{ card() ? ('Change it' | t) : ('Set it' | t) }}</h2>
        <ui-field [label]="'API user' | t" [required]="true" [hint]="'The merchant API user JCC gave (login-api).' | t">
          <input uiInput [value]="model().apiUser" (input)="set({ apiUser: $any($event.target).value })" />
        </ui-field>
        <ui-field [label]="'Password' | t" [required]="!card()?.passwordSet" [hint]="card()?.passwordSet ? ('Set. Type a new one only to replace it.' | t) : ''">
          <input uiInput type="password" autocomplete="off" [value]="model().password" (input)="set({ password: $any($event.target).value })" />
        </ui-field>
        <ui-field [label]="'Callback key' | t" [hint]="'The key JCC signs its callbacks with (checksum).' | t">
          <input uiInput type="password" autocomplete="off" [value]="model().callbackKey" (input)="set({ callbackKey: $any($event.target).value })" />
        </ui-field>
        <ui-field [label]="'Return address' | t" [required]="true" [hint]="'Where JCC sends the payer back: the ERP address and /pay/card-return.' | t">
          <input uiInput [value]="model().returnUrl" (input)="set({ returnUrl: $any($event.target).value })" />
        </ui-field>
        <ui-field [label]="'API address' | t" [hint]="'Empty: JCC test gateway. Another https address only for a copy of the gateway (e.g. a test simulator).' | t">
          <input uiInput [value]="model().apiBase" (input)="set({ apiBase: $any($event.target).value })" />
        </ui-field>
        <footer>
          @if (card()) { <button uiButton variant="quiet" [disabled]="busy()" (click)="switchOff()">{{ 'Switch off…' | t }}</button> }
          <span class="spacer"></span>
          <button uiButton variant="primary" [disabled]="busy() || !model().apiUser.trim()" (click)="save()">{{ 'Save' | t }}</button>
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
export class CardPayments {
  private readonly api = inject(BankingApi);
  private readonly toasts = inject(Toasts);
  private readonly dialogs = inject(Dialogs);

  protected readonly card = signal<CardProvider | null>(null);
  protected readonly model = signal<CardProviderModel>(cardProviderModelOf(null, window.location.origin));
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly message = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected callbackUrl(c: CardProvider): string {
    return cardCallbackUrl(window.location.origin, c.id);
  }

  protected set(change: Partial<CardProviderModel>): void {
    this.model.set({ ...this.model(), ...change });
  }

  private show(card: CardProvider | null): void {
    this.card.set(card);
    this.model.set(cardProviderModelOf(card, window.location.origin));
  }

  private async load(): Promise<void> {
    try {
      this.show(await this.api.cardProvider());
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.loaded.set(true);
    }
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      this.show(await this.api.saveCardProvider(this.model()));
      this.toasts.good(t('Card payments saved.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async switchOff(): Promise<void> {
    const yes = await this.dialogs.confirm({ title: t('Switch card payments off?'), message: t('The payment link can no longer take cards until a gateway is set again. Nothing is deleted.'),
      confirmLabel: t('Switch off'), danger: true });
    if (!yes) {
      return;
    }
    this.busy.set(true);
    this.message.set(null);
    try {
      await this.api.switchOffCardProvider();
      this.show(null);
      this.toasts.good(t('Card payments switched off.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
