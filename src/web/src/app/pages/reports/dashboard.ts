import { TranslatePipe } from '../../core/t.pipe';
import { t } from '../../core/i18n';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { EntityApi, errorsOf } from '../../core/entities';
import { Chart } from '../../ui/chart';
import { ChartSpec } from '../../ui/chart-option';
import { Button, Field, Input } from '../../ui/controls';
import { formatDate, formatNumber } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';
import { Kpi } from '../../ui/status';
import { Column, Table } from '../../ui/table';
import { thisMonth } from './report-page';

export interface DashboardRow { name: string | null; value: number; second: number | null; id: string | null }

export interface DashboardItem {
  id: string;
  code: string | null;
  name: string | null;
  kind: string | null;
  value: number | null;
  previous: number | null;
  firstName: string | null;
  secondName: string | null;
  documentCategoryId: string | null;
  secondCategoryId: string | null;
  negate: boolean;
  rows: DashboardRow[];
}

interface DashboardAnswer { from: string; to: string; previousFrom: string; previousTo: string; currency: string | null; items: DashboardItem[] }

export interface DashboardLink { path: string[]; params: Record<string, string> }

const CARDS = ['Total', 'Balance', 'Drafts', 'Overdue', 'GrossProfit'];
const CHARTS = ['Monthly', 'MonthlyVat', 'GrossProfitByCostCenter'];

/** The percent a total moved from the period before; null when there was nothing to compare with. */
export function changeOf(item: Pick<DashboardItem, 'value' | 'previous'>): number | null {
  if (item.previous === null || item.value === null || item.previous === 0) {
    return null;
  }
  return Math.round(((item.value - item.previous) / Math.abs(item.previous)) * 1000) / 10;
}

/** Twelve months as bars: the first category and, on a comparison, the second. */
export function chartOf(item: DashboardItem, unit: string | null): ChartSpec {
  const series = [{ name: item.firstName ?? '', values: item.rows.map(r => r.value) }];
  if (item.rows.some(r => r.second !== null)) {
    series.push({ name: item.secondName ?? '', values: item.rows.map(r => r.second ?? 0) });
  }
  return { kind: 'bar', categories: item.rows.map(r => r.name ?? ''), series, decimals: 0, ...(unit ? { unit } : {}) };
}

/** The report behind an item, for its period; with a cost centre chosen, one that can show it. */
export function linkOf(item: Pick<DashboardItem, 'kind' | 'documentCategoryId'>, from: string, to: string, costCenter: string | null): DashboardLink | null {
  const category = item.documentCategoryId;
  if (!category) {
    return null;
  }
  const lines = { documentCategories: category, from, to };
  const report = (name: string, params: Record<string, string> = lines) => ({ path: ['/reports', name], params });
  switch (item.kind) {
    case 'Total': return costCenter ? report('lines', { ...lines, costCenter }) : report('document-totals');
    case 'Balance': return report('partner-statistics', { documentCategory: category, from, to });
    case 'Drafts': return { path: ['/documents', category], params: {} };
    case 'MonthlyVat': return report('vat-register');
    case 'TopPartners': return report('lines-by-partner');
    case 'Overdue': return report('open-items', { to });
    case 'ByCostCenter':
    case 'GrossProfitByCostCenter': return report('lines-by-cost-center');
    default: return report('lines', costCenter ? { ...lines, costCenter } : lines);
  }
}

/**
 * The dashboard: the cards and charts the company configured under Dashboard items, each opening the
 * report behind it. Net of VAT, reversals subtracted, in the company's currency.
 */
@Component({
  selector: 'app-dashboard',
  imports: [TranslatePipe, Button, Chart, Field, Icon, Input, Kpi, RouterLink, Select, Table],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head"><h1>{{ 'Dashboard' | t }}</h1></div>
    @if (message()) { <p class="message" role="alert"><ui-icon name="critical" [size]="16" />{{ message() }}</p> }
    <section class="filters">
      <ui-field [label]="'From' | t"><input uiInput type="date" [value]="from()" (change)="from.set($any($event.target).value)" /></ui-field>
      <ui-field [label]="'Up to' | t"><input uiInput type="date" [value]="to()" (change)="to.set($any($event.target).value)" /></ui-field>
      <ui-field [label]="'Cost centre' | t"><ui-select [options]="centerOptions()" [(value)]="costCenter" /></ui-field>
      <button uiButton (click)="refresh()">{{ 'Refresh' | t }}</button>
    </section>
    @if (answer(); as a) {
      <section class="kpis">
        @for (item of cards(); track item.id) {
          <a class="card" [routerLink]="link(item)?.path" [queryParams]="link(item)?.params">
            @if (item.kind === 'Drafts') {
              <ui-kpi [label]="item.name ?? ''" [value]="count(item)" [caption]="'Not posted yet' | t" />
            } @else if (item.kind === 'Balance' || item.kind === 'Overdue') {
              <ui-kpi [label]="item.name ?? ''" [value]="amount(item)" [unit]="a.currency" [caption]="'At {0}' | t: day(to())" />
            } @else {
              <ui-kpi [label]="item.name ?? ''" [value]="amount(item)" [unit]="a.currency" [change]="change(item)" [upIsGood]="!item.negate"
                [comparedTo]="'the period before' | t" [caption]="'Nothing the period before' | t" />
            }
          </a>
        }
      </section>
      <section class="panels">
        @for (item of charts(); track item.id) {
          <div class="panel wide chart">
            <ui-chart [title]="item.name ?? ''" [spec]="chart(item)" [height]="260" />
            @if (link(item); as l) { <a class="open" [routerLink]="l.path" [queryParams]="l.params">{{ 'Open the report' | t }}</a> }
          </div>
        }
        @for (item of lists(); track item.id) {
          <div class="panel">
            <h2>{{ item.name }}</h2>
            <ui-table [columns]="columns" [rows]="rowsOf(item)" [pageSize]="10" [ariaLabel]="item.name" [emptyText]="'Nothing in this period.' | t" />
            @if (link(item); as l) { <a class="open" [routerLink]="l.path" [queryParams]="l.params">{{ 'Open the report' | t }}</a> }
          </div>
        }
      </section>
      @if (!a.items.length) { <p class="muted">{{ 'No item is configured. Add them under Reports, Dashboard items.' | t }}</p> }
    }
  `,
  styles: `
    :host { display: block; padding: var(--space-5) var(--space-6); }
    .head { margin-bottom: var(--space-4); }
    h1 { font-size: var(--title-l); }
    h2 { margin: 0 0 var(--space-3); font-size: var(--title-s); }
    .message { display: flex; align-items: center; gap: var(--space-2); color: var(--critical); }
    .filters { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--space-3); margin-bottom: var(--space-4); }
    .filters ui-field { width: 200px; }
    .kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(190px, 1fr)); gap: var(--space-4); margin-bottom: var(--space-4); }
    .card { display: grid; color: inherit; text-decoration: none; }
    .panels { display: grid; grid-template-columns: repeat(auto-fit, minmax(340px, 1fr)); gap: var(--space-4); }
    .panel { min-width: 0; padding: var(--space-4); border: 1px solid var(--line); border-radius: var(--radius-l); background: var(--surface); }
    .panel.wide { grid-column: 1 / -1; }
    /* ui-chart draws its own card, so a chart's panel is no second frame. */
    .panel.chart { padding: 0; border: 0; border-radius: 0; background: none; }
    .panel.chart .open { margin: var(--space-2) var(--space-4) 0; }
    .open { display: inline-block; margin-top: var(--space-2); font-size: var(--text-s); }
    .muted { color: var(--muted); }
    @media (max-width: 720px) { :host { padding: var(--space-4); } .panels { grid-template-columns: minmax(0, 1fr); } .filters ui-field { width: 100%; } }
  `,
})
export class Dashboard {
  private readonly http = inject(HttpClient);
  private readonly entities = inject(EntityApi);

  protected readonly message = signal<string | null>(null);
  protected readonly from = signal(thisMonth().from);
  protected readonly to = signal(thisMonth().to);
  protected readonly costCenter = signal<string | null>('');
  protected readonly centerOptions = signal<SelectOption[]>([{ value: '', label: t('All') }]);
  protected readonly answer = signal<DashboardAnswer | null>(null);
  private readonly refreshed = signal(0);

  protected readonly cards = computed(() => this.answer()?.items.filter(i => CARDS.includes(i.kind ?? '')) ?? []);
  protected readonly charts = computed(() => this.answer()?.items.filter(i => CHARTS.includes(i.kind ?? '')) ?? []);
  protected readonly lists = computed(() => this.answer()?.items.filter(i => !CARDS.includes(i.kind ?? '') && !CHARTS.includes(i.kind ?? '')) ?? []);
  protected readonly columns: Column<DashboardRow & { id: string }>[] = [{ key: 'name', label: t('Name') }, { key: 'value', label: t('Net'), format: 'number', width: '140px' }];

  constructor() {
    void this.loadCenters();
    effect(() => {
      const [from, to, costCenter] = [this.from(), this.to(), this.costCenter()];
      this.refreshed();
      untracked(() => void this.load(from, to, costCenter));
    });
  }

  private async loadCenters(): Promise<void> {
    await this.act(async () => {
      const scope = (await this.entities.lookupScopes()).find(s => s.table === 'tbl_cost_center' && !s.categoryId && !s.typeId);
      const found = scope ? (await this.entities.lookup(scope.id, '', 500)).items : [];
      this.centerOptions.set([{ value: '', label: t('All') }, ...found.map(o => ({ value: o.id, label: o.name || o.code || '', hint: o.code ?? undefined }))]);
    });
  }

  private async load(from: string, to: string, costCenter: string | null): Promise<void> {
    if (!from || !to) {
      return;
    }
    await this.act(async () => {
      const params = { from, to, ...(costCenter ? { costCenter } : {}) };
      this.answer.set(await firstValueFrom(this.http.get<DashboardAnswer>('/api/v1/dashboard', { params })));
    });
  }

  protected refresh(): void {
    this.refreshed.update(n => n + 1);
  }

  protected link(item: DashboardItem): DashboardLink | null {
    return linkOf(item, this.from(), this.to(), this.costCenter() || null);
  }

  protected day(value: string): string {
    return formatDate(value);
  }

  protected amount(item: DashboardItem): string {
    return formatNumber(item.value);
  }

  protected count(item: DashboardItem): string {
    return formatNumber(item.value, 0);
  }

  protected change(item: DashboardItem): number | null {
    return changeOf(item);
  }

  protected chart(item: DashboardItem): ChartSpec {
    return chartOf(item, this.answer()?.currency ?? null);
  }

  protected rowsOf(item: DashboardItem): (DashboardRow & { id: string })[] {
    return item.rows.map((r, i) => ({ ...r, id: r.id ?? String(i) }));
  }

  private async act(work: () => Promise<void>): Promise<void> {
    this.message.set(null);
    try {
      await work();
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }
}
