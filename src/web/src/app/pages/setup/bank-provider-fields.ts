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
    <ui-field [label]="'Application id' | t" [required]="true" [hint]="'The id of the company\\'s application at the provider.' | t">
      <input uiInput [value]="value().applicationId" (input)="set({ applicationId: $any($event.target).value })" />
    </ui-field>
    <ui-field [label]="'Private key (.pem)' | t" [required]="!keySet()" [hint]="keyHint()">
      <input class="control" type="file" accept=".pem,application/x-pem-file,text/plain" (change)="pick($any($event.target))" />
    </ui-field>
    @if (keyMessage()) { <p class="message" role="alert">{{ keyMessage() }}</p> }
    <ui-field [label]="'Return address' | t" [required]="true" [hint]="'Where the bank sends people back after they sign in. Add it to the application\\'s allowed addresses at the provider.' | t">
      <input uiInput [value]="value().redirectUrl" (input)="set({ redirectUrl: $any($event.target).value })" />
    </ui-field>
    <ui-field [label]="'Days a bank connection lasts' | t" [hint]="'1 to {0}; after that the account is connected again.' | t: (choices()?.maxAccessDays ?? 180)">
      <input uiInput type="number" min="1" [max]="choices()?.maxAccessDays ?? 180" [value]="value().accessDays" (input)="set({ accessDays: +$any($event.target).value })" />
    </ui-field>
  `,
})
export class BankProviderFields {
  readonly value = input.required<BankProviderModel>();
  readonly choices = input<BankProviderChoices | null>(null);
  /** A key is set already (a change): the file may be left empty to keep it. */
  readonly keySet = input(false);
  readonly fingerprint = input<string | null>(null);
  readonly valueChange = output<BankProviderModel>();

  protected readonly keyMessage = signal<string | null>(null);
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
