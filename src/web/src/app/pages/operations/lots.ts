import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { errorsOf } from '../../core/entities';
import { LotRow, LotStock, OperationsApi } from '../../core/operations';
import { Button, Field, Input } from '../../ui/controls';
import { Icon } from '../../ui/icon';
import { Column, Table } from '../../ui/table';
import { Tab, Tabs } from '../../ui/tabs';
import { Toasts } from '../../ui/toast';
import { today } from './register-options';

/** The day a number of days from now, as the API reads it. */
function daysFrom(start: string, days: number): string {
  const day = new Date(`${start}T00:00:00Z`);
  day.setUTCDate(day.getUTCDate() + days);
  return day.toISOString().slice(0, 10);
}

/** Lots: which batch is where and when it expires, read by hand, and lots given to stock already held. */
@Component({
  selector: 'app-lots',
  imports: [TranslatePipe, Button, Field, Icon, Input, Table, Tabs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Lots' | t }}</h1></div>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    <ui-tabs [tabs]="tabs" [(active)]="tab" [ariaLabel]="'Lots' | t" />
    @if (tab() === 'find') {
      <section class="form">
        <ui-field [label]="'Search' | t" [hint]="'A lot, product or holder' | t"><input uiInput type="search" (input)="typed($any($event.target).value)" /></ui-field>
        <ui-field [label]="'Expiring within days' | t" [hint]="'Empty: all. 0: already expired' | t"><input uiInput type="number" min="0" (input)="days.set($any($event.target).value)" /></ui-field>
      </section>
      <ui-table [columns]="columns" [rows]="rows()" [pageSize]="100" [ariaLabel]="'Lots' | t" [emptyText]="'No lot found.' | t" />
    } @else {
      <ui-table [columns]="stockColumns" [rows]="stockRows()" [pageSize]="100" [ariaLabel]="'Stock with lots' | t" [emptyText]="'No stock of a product that tracks lots.' | t" [activeId]="stockRow()?.id ?? null"
        (rowClick)="stockRow.set($any($event))" />
      @if (stockRow(); as s) {
        <h2>{{ '{0} at {1}: {2} without a lot' | t: (s.product) : (s.costCenter) : (s.quantity - s.inLots) }}</h2>
        <section class="form">
          <ui-field [label]="'Lot' | t" [required]="true"><input uiInput [value]="lot()" (input)="lot.set($any($event.target).value)" /></ui-field>
          <ui-field [label]="'Expiry date' | t"><input uiInput type="date" [value]="expiry()" (input)="expiry.set($any($event.target).value)" /></ui-field>
          <ui-field [label]="'Quantity' | t" [required]="true"><input uiInput type="number" min="0" [value]="quantity()" (input)="quantity.set($any($event.target).value)" /></ui-field>
          <div class="actions"><button uiButton variant="primary" [disabled]="!lot().trim() || !(+quantity() > 0) || busy()" (click)="assign(s)">{{ 'Give the lot' | t }}</button></div>
        </section>
      }
    }
  `,
  styleUrl: './operations.scss',
})
export class Lots {
  private readonly api = inject(OperationsApi);
  private readonly toasts = inject(Toasts);
  private timer?: ReturnType<typeof setTimeout>;

  protected readonly tabs: Tab[] = [{ id: 'find', label: t('Find') }, { id: 'assign', label: t('Assign to stock') }];
  protected readonly tab = signal<string | null>('find');
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly search = signal('');
  protected readonly days = signal('');
  private readonly lots = signal<LotRow[]>([]);
  private readonly stock = signal<LotStock[]>([]);
  protected readonly stockRow = signal<(LotStock & { id: string }) | null>(null);
  protected readonly lot = signal('');
  protected readonly expiry = signal('');
  protected readonly quantity = signal('');

  protected readonly rows = computed(() => this.lots().map(l => ({ ...l, productName: l.product ?? l.productCode })));
  protected readonly columns: Column<LotRow & { productName: string | null }>[] = [
    { key: 'lot', label: t('Lot'), sortable: true }, { key: 'productName', label: t('Product'), sortable: true }, { key: 'expiryDate', label: t('Expiry date'), format: 'date', width: '120px', sortable: true },
    { key: 'holder', label: t('Held by'), sortable: true }, { key: 'unit', label: t('Unit'), width: '100px' }, { key: 'quantity', label: t('Quantity'), format: 'number', width: '120px' },
  ];
  protected readonly stockRows = computed(() => this.stock().map(s => ({ ...s, id: `${s.costCenterId}:${s.productId}:${s.unitId}` })));
  protected readonly stockColumns: Column<LotStock & { id: string }>[] = [
    { key: 'costCenter', label: t('Place'), sortable: true }, { key: 'product', label: t('Product'), sortable: true }, { key: 'unit', label: t('Unit'), width: '100px' },
    { key: 'quantity', label: t('Quantity'), format: 'number', width: '120px' }, { key: 'inLots', label: t('With a lot'), format: 'number', width: '120px' },
  ];

  constructor() {
    effect(() => {
      const [search, days, tab] = [this.search(), this.days(), this.tab()];
      untracked(() => void this.load(search, days, tab));
    });
  }

  protected typed(value: string): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.search.set(value), 300);
  }

  private async load(search: string, days: string, tab: string | null): Promise<void> {
    this.message.set(null);
    try {
      if (tab === 'find') {
        // 0 asks for lots already expired: those whose day is before today.
        this.lots.set(await this.api.lots(search, days === '' ? null : daysFrom(today(), Number(days) === 0 ? -1 : Number(days))));
      } else {
        this.stock.set(await this.api.lotStock());
      }
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async assign(row: LotStock): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      await this.api.assignLot({ costCenterId: row.costCenterId, productId: row.productId, unitId: row.unitId, lot: this.lot().trim(), expiryDate: this.expiry() || null, quantity: Number(this.quantity()) });
      this.toasts.good(t('Lot {0} given.', this.lot().trim()));
      (this.stockRow.set(null), this.lot.set(''), this.expiry.set(''), this.quantity.set(''));
      await this.load(this.search(), this.days(), this.tab());
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
