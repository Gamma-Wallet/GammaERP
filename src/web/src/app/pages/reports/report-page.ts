import { TranslatePipe } from '../../core/t.pipe';
import { language, t } from '../../core/i18n';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, firstValueFrom, map } from 'rxjs';
import { DocumentApi } from '../../core/documents';
import { EntityApi, LookupScope, REGISTERS, errorsOf } from '../../core/entities';
import { itemAt } from '../../core/menu';
import { SessionService } from '../../core/session';
import { Chart } from '../../ui/chart';
import { ChartSpec } from '../../ui/chart-option';
import { Button, Field, Input } from '../../ui/controls';
import { formatDate, formatNumber, textIn } from '../../ui/format';
import { Icon } from '../../ui/icon';
import { Select, SelectOption } from '../../ui/select';
import { Kpi } from '../../ui/status';
import { Column, Table } from '../../ui/table';
import { Toasts } from '../../ui/toast';
import { FilterKind, HOLDERS, REPORT_DEFS, ReportView, Row, Values } from './report-defs';

const MULTI: readonly FilterKind[] = ['documentCategories', 'outputCategories', 'inputCategories'];

/** The first day of this month and today, as the period a report opens with. */
export function thisMonth(today = new Date()): { from: string; to: string } {
  const pad = (n: number) => String(n).padStart(2, '0');
  const day = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  return { from: day(new Date(today.getFullYear(), today.getMonth(), 1)), to: day(today) };
}

/** Whether every filter the report needs is given. */
export function ready(required: readonly FilterKind[], values: Values): boolean {
  return required.every(f => {
    const v = values[f];
    return Array.isArray(v) ? v.length > 0 : !!v;
  });
}

/** The values a link gives a report: its period and the filters it takes, on top of the ones it opens with. */
export function linkedValues(query: Record<string, string | string[]>, filters: readonly FilterKind[], base: Values): Values {
  const values: Values = { ...base };
  for (const key of ['from', 'to'] as const) {
    if (typeof query[key] === 'string') {
      values[key] = query[key];
    }
  }
  for (const filter of filters) {
    const value = query[filter];
    if (value !== undefined) {
      values[filter] = MULTI.includes(filter) && typeof value === 'string' ? [value] : value;
    }
  }
  return values;
}

/** The file name a download is saved under: the server's, else the report's. */
export function fileName(disposition: string | null, fallback: string): string {
  const star = disposition?.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
  const plain = disposition?.match(/filename="?([^";]+)"?/i)?.[1];
  return star ? decodeURIComponent(star) : (plain ?? fallback);
}

interface PartnerFigures {
  name: string | null;
  balance: number;
  firstDocument: string | null;
  lastDocument: string | null;
  months: { month: string; category: string | null; valueVat: number }[];
  topProducts: { productId: string; product: string | null; quantity: number; value: number }[];
}

/** A month × category chart of a partner's documents: one series per category, in the order they first appear. */
export function monthsChart(months: PartnerFigures['months']): ChartSpec | null {
  if (!months.length) {
    return null;
  }
  const categories = [...new Set(months.map(m => m.month))].sort();
  const names = [...new Set(months.map(m => m.category ?? '—'))];
  return { kind: 'bar', categories, decimals: 2, series: names.map(name => ({ name,
    values: categories.map(c => months.filter(m => m.month === c && (m.category ?? '—') === name).reduce((s, m) => s + m.valueVat, 0)) })) };
}

/**
 * A report (/reports/:report): its filters, the figures and a chart where they help, the table with its totals, and Excel and PDF of what is
 * shown. Partner statistics open a partner's own figures from the ranking; a row opens what stands
 * behind it (a trial balance row its account's ledger, a row with a document the document, a journal its sheet), and a report opened by such a
 * link shows at once.
 */
@Component({
  selector: 'app-report-page',
  imports: [TranslatePipe, Button, Chart, Field, Icon, Input, Kpi, Select, Table],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './report-page.html',
  styleUrl: './report-page.scss',
})
export class ReportPage {
  private readonly http = inject(HttpClient);
  private readonly entities = inject(EntityApi);
  private readonly documents = inject(DocumentApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);

  /** The report's key (reports/:report). */
  readonly report = input.required<string>();
  protected readonly def = computed(() => REPORT_DEFS[this.report()] ?? null);
  private readonly url = toSignal(this.router.events.pipe(filter(e => e instanceof NavigationEnd), map(() => this.router.url)), {
    initialValue: this.router.url,
  });
  protected readonly title = computed(() => itemAt(this.session.menu(), this.url())?.name ?? this.report());

  protected readonly values = signal<Values>(thisMonth());
  protected readonly options = signal<Partial<Record<FilterKind, SelectOption[]>>>({});
  protected readonly view = signal<ReportView | null>(null);
  protected readonly loading = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly partner = signal<PartnerFigures | null>(null);
  protected readonly partnerChart = computed(() => monthsChart(this.partner()?.months ?? []));
  protected readonly productColumns: Column<Row>[] = [{ key: 'product', label: t('Product') }, { key: 'quantity', label: t('Quantity'), format: 'number', decimals: 3,
    width: '120px' }, { key: 'value', label: t('Value'), format: 'number', width: '140px' }];
  protected readonly productRows = computed(() => (this.partner()?.topProducts ?? []).map(p => ({ ...p, id: p.productId }) as Row));
  protected readonly canRun = computed(() => {
    const def = this.def();
    return !!def && ready(def.required, this.values());
  });

  private scopes: LookupScope[] = [];

  constructor() {
    effect(() => {
      const def = this.def();
      untracked(() => {
        this.view.set(null);
        this.partner.set(null);
        this.message.set(null);
        const query = this.router.parseUrl(this.router.url).queryParams as Record<string, string | string[]>;
        this.values.set(def ? linkedValues(query, def.filters, { ...thisMonth(), ...def.defaults }) : thisMonth());
        if (def) {
          void this.loadOptions(def.filters);
          if (Object.keys(query).length) {
            void this.run();
          }
        }
      });
    });
  }

  private async loadOptions(filters: FilterKind[]): Promise<void> {
    this.scopes = this.scopes.length ? this.scopes : await this.entities.lookupScopes().catch(() => []);
    const scopeOf = (table: string) => this.scopes.find(s => s.table === table && !s.categoryId && !s.typeId);
    const lookup = async (table: string): Promise<SelectOption[]> => {
      const scope = scopeOf(table);
      return scope ? (await this.entities.lookup(scope.id, '', 500).catch(() => ({ items: [] }))).items
        .map(o => ({ value: o.id, label: o.name || o.code || o.id, hint: o.code ?? undefined })) : [];
    };
    const categories = (list: { id: string | null; isElement: boolean; name: Record<string, string>; code: string | null }[]): SelectOption[] =>
      list.filter(c => c.isElement && c.id).map(c => ({ value: c.id!, label: textIn(c.name), hint: c.code ?? undefined }));
    const options: Partial<Record<FilterKind, SelectOption[]>> = {};
    await Promise.all(filters.map(async f => {
      switch (f) {
        case 'account': options[f] = await lookup('tbl_chart'); break;
        case 'costCenter': options[f] = await lookup('tbl_cost_center'); break;
        case 'partner': options[f] = await lookup('tbl_partner'); break;
        case 'product': options[f] = await lookup('tbl_product'); break;
        case 'employee': options[f] = await lookup('tbl_employee'); break;
        case 'holder': options[f] = HOLDERS; break;
        case 'documentCategories':
        case 'outputCategories':
        case 'inputCategories': options[f] = categories(await this.documents.categories().catch(() => [])); break;
        case 'productCategory': options[f] = categories(await this.entities.categories(REGISTERS['products']).catch(() => [])); break;
        case 'documentCategory': options[f] = categories(await this.documents.categories().catch(() => [])); break;
        case 'chart': options[f] = categories(await this.entities.categories(REGISTERS['charts']).catch(() => [])); break;
        case 'stage': options[f] = (await firstValueFrom(this.http.get<{ stages: { id: string; name: string | null }[] }>('/api/v1/opportunities/form')).catch(() => ({ stages: [] })))
          .stages.map(s => ({ value: s.id, label: s.name ?? '' })); break;
      }
    }));
    this.options.set(options);
  }

  protected set(kind: FilterKind | 'from' | 'to', value: string | string[] | null): void {
    this.values.update(v => ({ ...v, [kind]: value }));
  }

  protected one(kind: FilterKind): string | null {
    const v = this.values()[kind];
    return typeof v === 'string' ? v : null;
  }

  protected many(kind: FilterKind): string[] {
    const v = this.values()[kind];
    return Array.isArray(v) ? v : [];
  }

  protected isMulti(kind: FilterKind): boolean {
    return MULTI.includes(kind);
  }

  protected label(kind: FilterKind): string {
    return { period: t('Period'), day: t('At'), account: t('Account'), costCenter: t('Cost centre'), partner: t('Partner'), product: t('Product'), documentCategories: t('Document categories'),
      outputCategories: t('Sales (output VAT)'), inputCategories: t('Purchases (input VAT)'), productCategory: t('Product category'), documentCategory: t('Document category'),
      chart: t('Chart of accounts'), employee: t('Staff'), holder: t('Held by'), stage: t('Stage') }[kind];
  }

  protected async run(): Promise<void> {
    const def = this.def();
    if (!def || !this.canRun()) {
      return;
    }
    this.loading.set(true);
    this.message.set(null);
    this.partner.set(null);
    try {
      const { path, params } = def.data(this.values());
      let http = new HttpParams();
      for (const [key, value] of Object.entries(params)) {
        for (const one of Array.isArray(value) ? value : [value]) {
          if (one) {
            http = http.append(key, one);
          }
        }
      }
      this.view.set(def.view(await firstValueFrom(this.http.get(`/api/v1/${path}`, { params: http }))));
    } catch (error) {
      this.message.set(errorsOf(error).message);
      this.view.set(null);
    } finally {
      this.loading.set(false);
    }
  }

  protected async download(kind: 'xlsx' | 'pdf'): Promise<void> {
    const def = this.def();
    const path = kind === 'xlsx' ? def?.xlsx?.(this.values()) : def?.pdf?.(this.values());
    if (!path) {
      return;
    }
    const url = kind === 'pdf' ? `${path}${path.includes('?') ? '&' : '?'}language=${language()}` : path;
    try {
      const response = await firstValueFrom(this.http.get(`/api/v1/${url}`, { responseType: 'blob', observe: 'response' })) as HttpResponse<Blob>;
      const link = document.createElement('a');
      link.href = URL.createObjectURL(response.body!);
      link.download = fileName(response.headers.get('Content-Disposition'), `${this.report()}.${kind}`);
      link.click();
      setTimeout(() => URL.revokeObjectURL(link.href), 10_000);
    } catch (error) {
      this.toasts.critical(errorsOf(error).message);
    }
  }

  protected async openRow(row: Row): Promise<void> {
    if (this.report() === 'partner-statistics') {
      return this.openPartner(row);
    }
    const link = this.def()?.open?.(row, this.values());
    if (link) {
      await this.router.navigate(['/reports', link.report], { queryParams: link.params });
    } else if (typeof row['documentId'] === 'string') {
      await this.openDocument(row['documentId']);
    }
  }

  /** A document where it lives: a journal in its sheet, under the document's own category. */
  private async openDocument(id: string): Promise<void> {
    try {
      const document = await this.documents.get(id);
      const shown = document.idSup ? await this.documents.get(document.idSup) : document;
      await this.router.navigate(['/documents', shown.categoryIds[0], shown.id]);
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  private async openPartner(row: Row): Promise<void> {
    const v = this.values();
    try {
      const params: Record<string, string> = {};
      if (v.from) {
        params['from'] = v.from;
      }
      if (v.to) {
        params['to'] = v.to;
      }
      this.partner.set(await firstValueFrom(this.http.get<PartnerFigures>(`/api/v1/partners/${row['partnerId']}/statistics`, { params })));
    } catch (error) {
      this.message.set(errorsOf(error).message);
    }
  }

  protected money(value: number): string {
    return formatNumber(value);
  }

  protected day(value: string | null): string {
    return formatDate(value);
  }
}

