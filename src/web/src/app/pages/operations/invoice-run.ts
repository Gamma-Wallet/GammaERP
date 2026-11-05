import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { errorsOf } from '../../core/entities';
import { t } from '../../core/i18n';
import { TranslatePipe } from '../../core/t.pipe';
import { Button, Field, Input } from '../../ui/controls';
import { formatDate, formatNumber } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Toasts } from '../../ui/toast';

interface DueContract {
  contractId: string;
  code: string | null;
  partner: string | null;
  everyMonths: number;
  nextDate: string;
  endDate: string | null;
  total: number;
}

interface RunResult {
  made: { id: string; code: string | null; contract: string | null }[];
  refused: { contractId: string; code: string | null; reason: string }[];
}

/** The contracts due for an invoice: a person presses the run, which makes drafts to check and post. Nothing runs by itself. */
@Component({
  selector: 'app-invoice-run',
  imports: [TranslatePipe, Button, Field, Icon, Input],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Invoice run' | t }}</h1></div>
    <section class="form">
      <ui-field [label]="'Due up to' | t"><input uiInput type="date" [value]="day()" (change)="choose($any($event.target).value)" /></ui-field>
      <div class="actions"><button uiButton variant="primary" [disabled]="busy() || !rows().length" (click)="run()">{{ 'Make the invoices' | t }}</button></div>
    </section>
    <p class="muted">{{ 'Invoices are made as drafts: check them and post them under Documents.' | t }}</p>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @if (result(); as r) {
      @for (m of r.made; track m.id) { <p>{{ '{0} made from {1}.' | t: m.code : m.contract }}</p> }
      @for (x of r.refused; track x.contractId) { <p class="message">{{ x.code }}: {{ x.reason }}</p> }
    }
    @if (rows().length) {
      <table>
        <thead><tr><th>{{ 'Contract' | t }}</th><th>{{ 'Client or supplier' | t }}</th><th>{{ 'Next date' | t }}</th><th>{{ 'End date' | t }}</th><th class="num">{{ 'Every (months)' | t }}</th><th class="num">{{ 'Total' | t }}</th></tr></thead>
        <tbody>
          @for (c of rows(); track c.contractId) {
            <tr><td>{{ c.code }}</td><td>{{ c.partner }}</td><td>{{ date(c.nextDate) }}</td><td>{{ c.endDate ? date(c.endDate) : '' }}</td><td class="num">{{ c.everyMonths }}</td><td class="num">{{ n(c.total) }}</td></tr>
          }
        </tbody>
      </table>
    } @else {
      <p class="muted">{{ 'No contract is due.' | t }}</p>
    }
  `,
  styleUrl: './operations.scss',
})
export class InvoiceRun {
  private readonly http = inject(HttpClient);
  private readonly toasts = inject(Toasts);

  protected readonly n = formatNumber;
  protected readonly date = formatDate;
  protected readonly day = signal(new Date().toISOString().slice(0, 10));
  protected readonly rows = signal<DueContract[]>([]);
  protected readonly result = signal<RunResult | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);

  constructor() {
    void this.load();
  }

  protected choose(day: string): void {
    if (day) {
      this.day.set(day);
      void this.load();
    }
  }

  private async load(): Promise<void> {
    this.message.set(null);
    try {
      this.rows.set(await firstValueFrom(this.http.get<DueContract[]>('/api/v1/contract-billing/due', { params: { until: this.day() } })));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async run(): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      const result = await firstValueFrom(this.http.post<RunResult>('/api/v1/contract-billing/run', null, { params: { until: this.day() } }));
      this.result.set(result);
      this.toasts.good(t('{0} invoices made.', result.made.length));
      await this.load();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
