import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { errorsOf, RightsService } from '../../core/entities';
import { Currency, ExchangeRate, OperationsApi } from '../../core/operations';
import { Chart } from '../../ui/chart';
import { ChartSpec } from '../../ui/chart-option';
import { Button, Field, Input } from '../../ui/controls';
import { Dialogs } from '../../ui/dialog';
import { Icon } from '../../ui/icon';
import { Status } from '../../ui/status';
import { Column, Table } from '../../ui/table';
import { Toasts } from '../../ui/toast';
import { today } from './register-options';

/** The rates as a line over their dates, oldest first; none with fewer than two. */
export function rateChart(rates: readonly ExchangeRate[], code: string): ChartSpec | null {
  const sorted = [...rates].sort((a, b) => a.rateDate.localeCompare(b.rateDate));
  return sorted.length > 1 ? { kind: 'line', categories: sorted.map(r => r.rateDate), series: [{ name: code, values: sorted.map(r => r.rate) }], decimals: 4 } : null;
}

/** The first day 90 days before today, as the period rates open with. */
function ninetyDaysAgo(): string {
  const d = new Date();
  d.setDate(d.getDate() - 90);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Whether a typed code can be a currency's: three letters, as the API asks. */
export function validCode(code: string): boolean {
  return /^[a-z]{3}$/i.test(code.trim());
}

/** Currencies and their rates by day: a currency's rates over a period, a rate set for a day; a currency added, renamed or switched off. */
@Component({
  selector: 'app-currencies',
  imports: [TranslatePipe, Button, Chart, Field, Icon, Input, Status, Table],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <h1>{{ 'Currencies and exchange rates' | t }}</h1>
      @if (admin()) { <button uiButton variant="primary" (click)="adding.set(!adding())"><ui-icon name="plus" />{{ 'New currency' | t }}</button> }
    </div>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    @if (adding()) {
      <section class="form">
        <ui-field [label]="'Code' | t" [required]="true" [hint]="'Three letters, such as EUR' | t"><input uiInput [value]="newCode()" (input)="newCode.set($any($event.target).value)" /></ui-field>
        <ui-field [label]="'Name' | t"><input uiInput [value]="newName()" (input)="newName.set($any($event.target).value)" /></ui-field>
        <div class="actions"><button uiButton variant="primary" [disabled]="!validCode(newCode())" (click)="add()">{{ 'Add' | t }}</button></div>
      </section>
    }
    <div class="split">
      <nav class="list" [attr.aria-label]="'Currencies' | t">
        @for (c of currencies(); track c.id) {
          <button type="button" [class.current]="c.id === currency()?.id" (click)="choose(c)">
            <span>{{ c.code }} <span class="muted">{{ c.name }}</span></span>
            @if (c.isBase) { <ui-status kind="info">{{ 'Own' | t }}</ui-status> }
          </button>
        }
      </nav>
      <section class="result">
        @if (currency(); as c) {
          @if (admin()) {
            <section class="form">
              <ui-field [label]="'Name' | t"><input uiInput [value]="name()" (input)="name.set($any($event.target).value)" /></ui-field>
              <div class="actions">
                <button uiButton [disabled]="!name().trim() || name() === c.name" (click)="rename()">{{ 'Rename' | t }}</button>
                @if (!c.isBase) { <button uiButton (click)="switchOff()">{{ 'Switch off' | t }}</button> }
              </div>
            </section>
          }
          @if (c.isBase) {
            <p class="muted">{{ '{0} is the company\\'s own currency: its rate is always 1.' | t: (c.code) }}</p>
          } @else {
            <section class="form">
              <ui-field [label]="'From' | t"><input uiInput type="date" [value]="from()" (change)="from.set($any($event.target).value); loadRates()" /></ui-field>
              <ui-field [label]="'Up to' | t"><input uiInput type="date" [value]="to()" (change)="to.set($any($event.target).value); loadRates()" /></ui-field>
            </section>
            @if (chart(); as spec) { <ui-chart [title]="c.code + ' rate'" [spec]="spec" [height]="220" /> }
            <ui-table [columns]="columns" [rows]="rates()" [pageSize]="100" [sort]="{ key: 'rateDate', direction: 'desc' }" [ariaLabel]="'Rates' | t" [emptyText]="'No rates in this period.' | t" />
            @if (admin()) {
              <section class="form">
                <ui-field [label]="'Day' | t" [required]="true"><input uiInput type="date" [value]="day()" (input)="day.set($any($event.target).value)" /></ui-field>
                <ui-field [label]="'Rate' | t" [required]="true" [hint]="'1 ' + c.code + ' in the own currency'">
                  <input uiInput [numeric]="true" inputmode="decimal" [value]="rate()" (input)="rate.set($any($event.target).value)" />
                </ui-field>
                <div class="actions"><button uiButton variant="primary" [disabled]="!(+rate() > 0) || !day()" (click)="setRate()">{{ 'Set rate' | t }}</button></div>
              </section>
            }
          }
        } @else {
          <p class="muted">{{ 'Choose a currency.' | t }}</p>
        }
      </section>
    </div>
  `,
  styleUrl: './operations.scss',
})
export class Currencies {
  private readonly api = inject(OperationsApi);
  private readonly toasts = inject(Toasts);
  private readonly rights = inject(RightsService);
  protected readonly admin = this.rights.isAdministrator;
  private readonly dialogs = inject(Dialogs);
  protected readonly validCode = validCode;
  protected readonly adding = signal(false);
  protected readonly newCode = signal('');
  protected readonly newName = signal('');
  protected readonly name = signal('');

  protected readonly message = signal<string | null>(null);
  protected readonly currencies = signal<Currency[]>([]);
  protected readonly currency = signal<Currency | null>(null);
  protected readonly rates = signal<(ExchangeRate & { id: string })[]>([]);
  protected readonly from = signal(ninetyDaysAgo());
  protected readonly to = signal(today());
  protected readonly day = signal(today());
  protected readonly rate = signal('');
  protected readonly chart = computed(() => rateChart(this.rates(), this.currency()?.code ?? ''));
  protected readonly columns: Column<ExchangeRate>[] = [
    { key: 'rateDate', label: t('Day'), format: 'date', width: '160px', sortable: true }, { key: 'rate', label: t('Rate'), format: 'number', decimals: 4, sortable: true },
  ];

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      await this.rights.load();
      const currencies = await this.api.currencies();
      this.currencies.set(currencies);
      const first = currencies.find(c => !c.isBase) ?? currencies[0];
      if (first) {
        await this.choose(first);
      }
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async add(): Promise<void> {
    await this.change(async () => {
      const added = await this.api.saveCurrency({ id: null, code: this.newCode().trim().toUpperCase(), name: this.newName().trim() });
      this.toasts.good(t('{0} added.', added.code));
      this.adding.set(false);
      this.newCode.set('');
      this.newName.set('');
      await this.reload(added.id);
    });
  }

  protected async rename(): Promise<void> {
    const currency = this.currency();
    if (currency) {
      await this.change(async () => {
        await this.api.saveCurrency({ id: currency.id, code: null, name: this.name().trim() });
        this.toasts.good(t('{0} renamed.', currency.code));
        await this.reload(currency.id);
      });
    }
  }

  protected async switchOff(): Promise<void> {
    const currency = this.currency();
    if (currency && await this.dialogs.confirm({ title: `Switch off ${currency.code}?`, message: t('It leaves the list of currencies. A currency used on a document or a price stays.'),
      confirmLabel: t('Switch off'), danger: true })) {
      await this.change(async () => {
        await this.api.switchOffCurrency(currency.id);
        this.toasts.good(t('{0} switched off.', currency.code));
        await this.reload(null);
      });
    }
  }

  private async change(work: () => Promise<void>): Promise<void> {
    this.message.set(null);
    try {
      await work();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  private async reload(id: string | null): Promise<void> {
    const currencies = await this.api.currencies();
    this.currencies.set(currencies);
    const chosen = currencies.find(c => c.id === id) ?? currencies.find(c => !c.isBase) ?? currencies[0];
    if (chosen) {
      await this.choose(chosen);
    }
  }

  protected async choose(currency: Currency): Promise<void> {
    this.currency.set(currency);
    this.name.set(currency.name ?? '');
    await this.loadRates();
  }

  protected async loadRates(): Promise<void> {
    const currency = this.currency();
    if (!currency || currency.isBase) {
      this.rates.set([]);
      return;
    }
    try {
      this.rates.set(await this.api.rates(currency.id, this.from(), this.to()));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected async setRate(): Promise<void> {
    const currency = this.currency();
    if (!currency) {
      return;
    }
    try {
      await this.api.setRate(currency.id, this.day(), Number(this.rate().replace(',', '.')));
      this.toasts.good(t('{0}: rate for {1} set.', currency.code, this.day()));
      this.rate.set('');
      await this.loadRates();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }
}
