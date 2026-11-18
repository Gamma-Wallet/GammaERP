import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { BankProviderChoices, BankProviderModel, looksLikePrivateKey } from '../../core/banking';
import { t } from '../../core/i18n';
import { TranslatePipe } from '../../core/t.pipe';
import { Field, Input } from '../../ui/controls';
import { Select, SelectOption } from '../../ui/select';

/**
 * The open banking fields (the founder, 2026-11-14): the provider, test banks or real accounts, the company's application id and its private key
 * (.pem, read in the browser and sent once; never shown again), where the bank sends people back, and how long a bank connection asks for.
 */
@Component({
  selector: 'app-bank-provider-fields',
  imports: [TranslatePipe, Field, Input, Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-field [label]="'Provider' | t" [required]="true">
      <ui-select [options]="kindOptions()" [value]="value().kind" (valueChange)="set({ kind: $event ?? 'EnableBanking' })" />
    </ui-field>
    <ui-field [label]="'Banks' | t" [hint]="'Sandbox: the provider\\'s test banks. Production: real accounts (needs a contract with the provider).' | t">
      <ui-select [options]="environmentOptions()" [value]="value().environment" (valueChange)="set({ environment: $event ?? 'Sandbox' })" />
    </ui-field>
    @if (isBoc()) {
      <ui-field [label]="'Client id' | t" [required]="true" [hint]="'The client id of the company\\'s app on the Bank of Cyprus developer portal.' | t">
        <input uiInput [value]="value().applicationId" (input)="set({ applicationId: $any($event.target).value })" />
      </ui-field>
      <ui-field [label]="'Client secret' | t" [required]="!keySet()" [hint]="keySet() ? ('Set. Type a new one only to replace it.' | t) : ('The app\\'s client secret; it is sent once and never shown again.' | t)">
        <input uiInput type="password" autocomplete="off" [value]="value().privateKey" (input)="set({ privateKey: $any($event.target).value, keyFileName: '' })" />
      </ui-field>
    } @else {
      <ui-field [label]="'Application id' | t" [required]="true" [hint]="'The id of the company\\'s application at the provider.' | t">
        <input uiInput [value]="value().applicationId" (input)="set({ applicationId: $any($event.target).value })" />
      </ui-field>
      <ui-field [label]="'Private key (.pem)' | t" [required]="!keySet()" [hint]="keyHint()">
        <input class="control" type="file" accept=".pem,application/x-pem-file,text/plain" (change)="pick($any($event.target))" />
      </ui-field>
    }
    @if (keyMessage()) { <p class="message" role="alert">{{ keyMessage() }}</p> }
    <ui-field [label]="'Return address' | t" [required]="true" [hint]="'Where the bank sends people back after they sign in. Add it to the application\\'s allowed addresses at the provider.' | t">
      <input uiInput [value]="value().redirectUrl" (input)="set({ redirectUrl: $any($event.target).value })" />
    </ui-field>
    <ui-field [label]="'API address' | t" [hint]="'Empty: the provider\\'s own. Another https address only for a copy of the provider (e.g. a test simulator).' | t">
      <input uiInput [value]="value().apiBase" (input)="set({ apiBase: $any($event.target).value })" />
    </ui-field>
    <ui-field [label]="'Bank response secret' | t" [hint]="responseHint()">
      <input uiInput type="password" autocomplete="off" [value]="value().responseSecret" (input)="set({ responseSecret: $any($event.target).value })" />
    </ui-field>
    <ui-field [label]="'Days a bank connection lasts' | t" [hint]="'1 to {0}; after that the account is connected again.' | t: (choices()?.maxAccessDays ?? 180)">
      <input uiInput type="number" min="1" [max]="choices()?.maxAccessDays ?? 180" [value]="value().accessDays" (input)="set({ accessDays: +$any($event.target).value })" />
    </ui-field>
    @if (isBoc()) {
      <label class="check"><input type="checkbox" [checked]="value().paymentsEnabled" (change)="set({ paymentsEnabled: $any($event.target).checked })" />
        {{ 'Take payments from the payment link of the till (sandbox only until the company holds a payment-initiation licence)' | t }}</label>
      @if (value().paymentsEnabled) {
        <ui-field [label]="'Payment return address' | t" [required]="true" [hint]="'Where the bank sends the payer back: the ERP address and /pay/bank-return. Add it to the redirect URIs of the app at the bank.' | t">
          <input uiInput [value]="value().paymentReturnUrl" (input)="set({ paymentReturnUrl: $any($event.target).value })" />
        </ui-field>
        <ui-field [label]="'Minutes a payment link lasts' | t">
          <input uiInput type="number" min="1" max="1440" [value]="value().paymentLinkMinutes" (input)="set({ paymentLinkMinutes: +$any($event.target).value })" />
        </ui-field>
      }
    }
  `,
  styles: `.check { display: flex; gap: var(--space-2); align-items: flex-start; margin: var(--space-3) 0; }`,
})
export class BankProviderFields {
  readonly value = input.required<BankProviderModel>();
  readonly choices = input<BankProviderChoices | null>(null);
  /** A key is set already (a change): the file may be left empty to keep it. */
  readonly keySet = input(false);
  readonly fingerprint = input<string | null>(null);
  /** The bank's response secret is set already: the field may be left empty to keep it. */
  readonly responseSecretSet = input(false);
  readonly valueChange = output<BankProviderModel>();

  protected readonly keyMessage = signal<string | null>(null);
  /** Bank of Cyprus: a client id and secret instead of an application key file. */
  protected readonly isBoc = computed(() => this.value().kind === 'BankOfCyprus');
  protected readonly kindOptions = computed<SelectOption[]>(() => (this.choices()?.kinds ?? [{ kind: 'EnableBanking', name: 'Enable Banking', apiBase: '' }])
    .map(k => ({ value: k.kind, label: k.name })));
  protected readonly environmentOptions = computed<SelectOption[]>(() => (this.choices()?.environments ?? ['Sandbox', 'Production'])
    .map(e => ({ value: e, label: e === 'Sandbox' ? t('Sandbox (test banks)') : t('Production (real accounts)') })));
  protected readonly keyHint = computed(() => {
    const file = this.value().keyFileName;
    if (file) {
      return t('{0} read; it is sent once and never shown again.', file);
    }
    return this.keySet() ? t('A key is set ({0}). Choose a file only to replace it.', this.fingerprint() ?? '') : t('The .pem file the provider gave when the application was registered.');
  });

  protected readonly responseHint = computed(() => this.responseSecretSet()
    ? t('Set. Type a new one only to replace it.')
    : t('Optional: when the bank sends a signed message for each payment received, the same secret as set at the bank (16 characters or more).'));

  protected set(change: Partial<BankProviderModel>): void {
    this.valueChange.emit({ ...this.value(), ...change });
  }

  protected async pick(picker: HTMLInputElement): Promise<void> {
    const file = picker.files?.[0];
    if (!file) {
      return;
    }
    const text = await file.text();
    if (!looksLikePrivateKey(text)) {
      this.keyMessage.set(t('This file is not a private key in PEM form.'));
      picker.value = '';
      return;
    }
    this.keyMessage.set(null);
    this.set({ privateKey: text, keyFileName: file.name });
  }
}
