import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { errorsOf } from '../../core/entities';
import { t } from '../../core/i18n';
import { TranslatePipe } from '../../core/t.pipe';
import { Button, Field, Input } from '../../ui/controls';
import { formatDate, formatNumber } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { OperationsApi } from '../../core/operations';
import { Select, SelectOption } from '../../ui/select';
import { Toasts } from '../../ui/toast';

interface YearClosingState {
  from: string;
  to: string;
  closedUntil: string | null;
  drafts: number;
  debit: number;
  credit: number;
  closed: boolean;
  canClose: boolean;
  currencyId: string | null;
  currency: string | null;
}

/** Closing a fiscal year: it checks the year is finished and locks it; nothing is written to the books. Closed periods moves the lock back. */
@Component({
  selector: 'app-year-closing',
  imports: [TranslatePipe, Button, Field, Icon, Input, Select],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Close the year' | t }}</h1></div>
    <section class="form">
      <ui-field [label]="'A day in the year' | t"><input uiInput type="date" [value]="day()" (change)="choose($any($event.target).value)" /></ui-field>
    </section>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @if (year(); as y) {
      <p>{{ 'Fiscal year {0} to {1}' | t: date(y.from) : date(y.to) }}</p>
      <p>{{ 'Documents not posted: {0}' | t: y.drafts }}</p>
      <p>{{ 'Debit {0}, credit {1}' | t: n(y.debit) : n(y.credit) }}</p>
      @if (y.closed) {
        <p><strong>{{ 'This year is closed.' | t }}</strong> {{ 'An administrator can move it back under Closed periods.' | t }}</p>
      } @else {
        <p class="muted">{{ 'Closing locks the year: nothing can be posted on its days. Nothing is written to the books.' | t }}</p>
        <section class="form">
          <ui-field [label]="nextLabel | t" [hint]="nextHint | t: (y.currency ?? '—')">
            <ui-select [options]="currencies()" [value]="nextCurrency() ?? y.currencyId" (valueChange)="nextCurrency.set($event)" />
          </ui-field>
        </section>
        <div class="actions"><button uiButton variant="danger" [disabled]="busy() || !y.canClose" (click)="close()">{{ 'Close the year' | t }}</button></div>
      }
    }
  `,
  styleUrl: './operations.scss',
})
export class YearClosing {
  private readonly http = inject(HttpClient);
  private readonly toasts = inject(Toasts);

  protected readonly n = formatNumber;
  protected readonly date = formatDate;
  protected readonly day = signal(new Date(new Date().getFullYear() - 1, 11, 31, 12).toISOString().slice(0, 10));
  protected readonly year = signal<YearClosingState | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);
  /** The company's currencies, to keep the books in another one from the next year. */
  protected readonly currencies = signal<SelectOption[]>([]);
  protected readonly nextCurrency = signal<string | null>(null);
  protected readonly nextLabel = "Next year's currency";
  protected readonly nextHint = "Today: {0}. Another one needs its exchange rate of the next year's first day; the old year stays as written.";
  private readonly api = inject(OperationsApi);

  constructor() {
    void this.load();
    void this.api.currencies().then(list => this.currencies.set(list.map(c => ({ value: c.id, label: c.code ?? c.name ?? c.id, hint: c.name ?? undefined })))).catch(() => undefined);
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
      this.year.set(await firstValueFrom(this.http.get<YearClosingState>('/api/v1/year-closing', { params: { day: this.day() } })));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async close(): Promise<void> {
    this.busy.set(true);
    this.message.set(null);
    try {
      const next = this.nextCurrency();
      const params: Record<string, string> = { day: this.day() };
      if (next && next !== this.year()?.currencyId) {
        params['nextCurrency'] = next;
      }
      this.year.set(await firstValueFrom(this.http.post<YearClosingState>('/api/v1/year-closing', null, { params })));
      this.toasts.good(t('This year is closed.'));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    } finally {
      this.busy.set(false);
    }
  }
}
