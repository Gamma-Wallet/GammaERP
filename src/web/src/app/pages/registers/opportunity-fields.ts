import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EntityApi, OpportunityFields as Fields } from '../../core/entities';
import { TranslatePipe } from '../../core/t.pipe';
import { Field, Input } from '../../ui/controls';
import { Select, SelectOption } from '../../ui/select';
import { registerOptions } from '../operations/register-options';

/** An opportunity's own fields on its register's panel: client, stage, amount, currency, expected close and owner. */
@Component({
  selector: 'app-opportunity-fields',
  imports: [TranslatePipe, Field, Input, Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-field class="wide" [label]="'Client or lead' | t" [required]="true">
      <ui-select [options]="partners()" [searchable]="true" [value]="value()?.partnerId ?? null" [disabled]="disabled()" (valueChange)="set({ partnerId: $event })" />
    </ui-field>
    <ui-field [label]="'Stage' | t" [required]="true">
      <ui-select [options]="stages()" [value]="value()?.stageId ?? null" [disabled]="disabled()" (valueChange)="set({ stageId: $event })" />
    </ui-field>
    <ui-field [label]="'Amount' | t"><input uiInput type="number" [value]="value()?.amount ?? ''" [disabled]="disabled()" (input)="set({ amount: $any($event.target).value === '' ? null : +$any($event.target).value })" /></ui-field>
    <ui-field [label]="'Currency' | t">
      <ui-select [options]="currencies()" [value]="value()?.currencyId ?? null" [disabled]="disabled()" (valueChange)="set({ currencyId: $event })" />
    </ui-field>
    <ui-field [label]="'Expected close' | t"><input uiInput type="date" [value]="value()?.expectedClose ?? ''" [disabled]="disabled()" (input)="set({ expectedClose: $any($event.target).value || null })" /></ui-field>
    <ui-field [label]="'Owner' | t">
      <ui-select [options]="users()" [searchable]="true" [value]="value()?.ownerUserId ?? null" [disabled]="disabled()" (valueChange)="set({ ownerUserId: $event })" />
    </ui-field>
    @if (lost()) {
      <ui-field class="wide" [label]="'Reason it was lost' | t" [required]="true">
        <input uiInput [value]="value()?.lostReason ?? ''" [disabled]="disabled()" (input)="set({ lostReason: $any($event.target).value })" />
      </ui-field>
    }
  `,
  styles: `:host { display: contents; }`,
})
export class OpportunityFieldsForm {
  private readonly http = inject(HttpClient);
  private readonly entities = inject(EntityApi);

  readonly value = input<Fields | null | undefined>(null);
  readonly disabled = input(false);
  readonly changed = output<Fields>();
  protected readonly partners = signal<SelectOption[]>([]);
  protected readonly users = signal<SelectOption[]>([]);
  protected readonly stages = signal<SelectOption[]>([]);
  protected readonly currencies = signal<SelectOption[]>([]);
  private readonly lostStages = signal<string[]>([]);
  protected readonly lost = computed(() => this.lostStages().includes(this.value()?.stageId ?? ''));

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    const form = await firstValueFrom(this.http.get<{ stages: { id: string; name: string; outcome: number }[]; currencies: { id: string; code: string }[] }>('/api/v1/opportunities/form'));
    this.stages.set(form.stages.map(s => ({ value: s.id, label: s.name })));
    this.lostStages.set(form.stages.filter(s => s.outcome === 2).map(s => s.id));
    this.currencies.set(form.currencies.map(c => ({ value: c.id, label: c.code })));
    const scopes = await this.entities.lookupScopes().catch(() => []);
    this.partners.set(await registerOptions(this.entities, scopes, 'tbl_partner'));
    this.users.set(await registerOptions(this.entities, scopes, 'tbl_user'));
  }

  protected set(change: Partial<Fields>): void {
    this.changed.emit({ ...this.value(), ...change });
  }
}
