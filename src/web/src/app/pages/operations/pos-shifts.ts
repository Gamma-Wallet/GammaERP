import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { errorsOf } from '../../core/entities';
import { PosApi, PosShift, PosShiftSummary } from '../../core/pos';
import { formatNumber } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Column, Table } from '../../ui/table';

/** Every shift of the company's points of sale, who opened and closed it and what the cash-up found. */
@Component({
  selector: 'app-pos-shifts',
  imports: [TranslatePipe, Icon, Table],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Shifts' | t }}</h1></div>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    <ui-table [columns]="columns" [rows]="shifts()" [pageSize]="100" [ariaLabel]="'Shifts' | t" [emptyText]="'No shift yet.' | t" [activeId]="chosen()?.shift?.id ?? null" (rowClick)="open($any($event))" />
    @if (chosen(); as c) {
      <h2>{{ c.shift.pointOfSale }}</h2>
      <table>
        <tbody>
          <tr><td>{{ 'Bills' | t }}</td><td class="num">{{ c.bills }}</td></tr>
          <tr><td>{{ 'Sales' | t }}</td><td class="num">{{ n(c.sales) }}</td></tr>
          @for (p of c.payments; track p.methodId) {
            <tr><td>{{ p.method ?? ('Cash desk' | t) }}</td><td class="num">{{ n(p.amount) }}</td></tr>
          }
          <tr><td>{{ 'Money in' | t }}</td><td class="num">{{ n(c.moneyIn) }}</td></tr>
          <tr><td>{{ 'Money out' | t }}</td><td class="num">{{ n(c.moneyOut) }}</td></tr>
        </tbody>
      </table>
      @if (c.shift.note) { <p class="muted">{{ c.shift.note }}</p> }
    }
  `,
  styleUrl: './operations.scss',
})
export class PosShifts {
  private readonly api = inject(PosApi);

  protected readonly n = formatNumber;
  protected readonly message = signal<string | null>(null);
  protected readonly rows = signal<PosShift[]>([]);
  protected readonly chosen = signal<PosShiftSummary | null>(null);
  protected readonly shifts = computed(() => this.rows());
  protected readonly columns: Column<PosShift>[] = [
    { key: 'pointOfSale', label: t('Point of sale'), sortable: true }, { key: 'openedBy', label: t('Opened by') }, { key: 'openedOn', label: t('Opened'), format: 'date', width: '110px', sortable: true },
    { key: 'closedBy', label: t('Closed by') }, { key: 'closedOn', label: t('Closed'), format: 'date', width: '110px' }, { key: 'openingCash', label: t('Opening cash'), format: 'number', width: '120px' },
    { key: 'expectedCash', label: t('Expected cash'), format: 'number', width: '120px' }, { key: 'countedCash', label: t('Counted cash'), format: 'number', width: '120px' },
    { key: 'difference', label: t('Difference'), format: 'number', width: '110px' },
  ];

  constructor() {
    this.api.shifts().then(rows => this.rows.set(rows)).catch((error: unknown) => this.message.set(errorsOf(error).message));
  }

  protected async open(shift: PosShift): Promise<void> {
    try {
      this.chosen.set(await this.api.shiftDetail(shift.id));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }
}
