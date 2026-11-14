import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { BankProvider, BankProviderChoices, BankProviderModel, BankingApi, bankProviderModelOf, bankProviderProblem, emptyBankProvider } from '../../core/banking';
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
  imports: [TranslatePipe, BankProviderFields, Button, Icon],
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
        </dl>
      } @else {
        <p>{{ 'The company has no open banking provider yet.' | t }}</p>
      }
      <section class="card">
        <h2>{{ current() ? ('Change it' | t) : ('Set it' | t) }}</h2>
        <app-bank-provider-fields [value]="model()" [choices]="choices()" [keySet]="!!current()?.keyReadable" [fingerprint]="current()?.keyFingerprint ?? null"
          (valueChange)="model.set($event)" />
        @if (problem()) { <p class="muted problem">{{ problem() }}</p> }
        <footer>
          @if (current()) { <button uiButton variant="quiet" [disabled]="busy()" (click)="switchOff()">{{ 'Switch off…' | t }}</button> }
          <span class="spacer"></span>
          <button uiButton variant="primary" [disabled]="busy() || !!problem()" (click)="save()">{{ 'Save' | t }}</button>
        </footer>
      </section>
    }
  `,
  styles: `
    .head { display: flex; align-items: center; gap: var(--space-3, 12px); }
    .intro { max-width: 70ch; }
    .review { display: grid; grid-template-columns: max-content 1fr; gap: 6px 16px; }
    .card { margin-top: 16px; max-width: 640px; }
    footer { display: flex; gap: 8px; margin-top: 12px; }
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

  private async load(): Promise<void> {
    try {
      const [choices, current] = await Promise.all([this.api.choices(), this.api.provider()]);
      this.choices.set(choices);
      this.show(current);
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
