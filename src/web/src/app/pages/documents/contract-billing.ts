import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { errorsOf } from '../../core/entities';
import { t } from '../../core/i18n';
import { TranslatePipe } from '../../core/t.pipe';
import { Button, Field, Input } from '../../ui/controls';
import { Toasts } from '../../ui/toast';

/** A saved contract's billing plan: every so many months from a next date; empty stops the billing. */
@Component({
  selector: 'app-contract-billing',
  imports: [TranslatePipe, Button, Field, Input],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="billing" [attr.aria-label]="'Billing plan' | t">
      <ui-field [label]="'Every (months)' | t"><input uiInput type="number" min="1" max="60" [value]="every() ?? ''" (input)="changed = true; every.set($any($event.target).value === '' ? null : +$any($event.target).value)" /></ui-field>
      <ui-field [label]="'Next date' | t"><input uiInput type="date" [value]="next() ?? ''" (input)="changed = true; next.set($any($event.target).value || null)" /></ui-field>
      <button uiButton (click)="save()">{{ 'Save the billing plan' | t }}</button>
      @if (message()) { <span class="message" role="alert">{{ message() }}</span> }
    </section>
  `,
  styles: `.billing { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--space-3); margin: var(--space-4) 0; } .message { color: var(--critical); }`,
})
export class ContractBillingPlan {
  private readonly http = inject(HttpClient);
  private readonly toasts = inject(Toasts);

  readonly contractId = input.required<string>();
  protected readonly every = signal<number | null>(null);
  protected readonly next = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  /** The plan was changed since it was read or saved: the document's Save saves it too. */
  protected changed = false;

  /** Saves the plan if it was changed (the contract's Save calls this). */
  async saveIfChanged(): Promise<void> {
    if (this.changed) {
      await this.save();
    }
  }

  constructor() {
    effect(() => void this.load(this.contractId()));
  }

  private async load(id: string): Promise<void> {
    const plan = await firstValueFrom(this.http.get<{ everyMonths: number; nextDate: string } | null>(`/api/v1/contract-billing/${id}`)).catch(() => null);
    this.every.set(plan?.everyMonths ?? null);
    this.next.set(plan?.nextDate ?? null);
  }

  protected async save(): Promise<void> {
    this.message.set(null);
    try {
      await firstValueFrom(this.http.put(`/api/v1/contract-billing/${this.contractId()}`, { everyMonths: this.every(), nextDate: this.next() }));
      this.changed = false;
      this.toasts.good(t('The billing plan is saved.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }
}
