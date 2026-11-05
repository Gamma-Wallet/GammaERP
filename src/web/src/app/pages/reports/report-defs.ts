import { t } from '../../core/i18n';
// The reports (C3 phase 6): each one's filters, data, columns, figures, chart and exports, from the report API as it is.
import { ChartSpec } from '../../ui/chart-option';
import { formatNumber } from '../../ui/format';
import { Column } from '../../ui/table';

/** A filter a report takes; a lookup picks from a register's scope, a category from a register's categories. */
export type FilterKind = 'period' | 'day' | 'stage' | 'account' | 'costCenter' | 'partner' | 'product' | 'documentCategories' | 'outputCategories' | 'inputCategories'
  | 'productCategory' | 'documentCategory' | 'chart' | 'employee' | 'holder';

export type Row = Record<string, unknown> & { id: string };

export interface Figure {
  label: string;
  value: string;
  caption?: string;
}

/** What a report shows once loaded. */
export interface ReportView {
  rows: Row[];
  footer?: Record<string, string>;
  figures?: Figure[];
  chart?: { title: string; spec: ChartSpec };
}

export type Values = Partial<Record<FilterKind, string | string[] | null>> & { from?: string; to?: string };

/** Where a row leads: another report with its filters given, read from the row and this report's values. */
export interface ReportLink {
  report: string;
  params: Record<string, string | string[]>;
}

export interface ReportDef {
  key: string;
  filters: FilterKind[];
  /** Filters that must be given before the report loads. */
  required: FilterKind[];
  /** The API path of the data (relative to /api/v1) and its query. */
  data: (v: Values) => { path: string; params: Record<string, string | string[]> };
  /** The export paths, by kind; the PDF also takes the language. */
  xlsx?: (v: Values) => string;
  pdf?: (v: Values) => string;
  columns: Column<Row>[];
  view: (data: unknown) => ReportView;
  /** The filters it opens with, beside this month. */
  defaults?: Values;
  /** The report a row opens; without it, a row with a document opens the document. */
  open?: (row: Row, v: Values) => ReportLink;
}

const money = (n: unknown) => formatNumber(Number(n ?? 0));
const num = (key: string, label: string, width = '130px'): Column<Row> => ({ key, label: t(label), format: 'number', width, sortable: true });
const text = (key: string, label: string, width?: string): Column<Row> => ({ key, label: t(label), width, sortable: true });
const date = (key: string, label = 'Date'): Column<Row> => ({ key, label, format: 'date', width: '110px', sortable: true });

/** A query string from filters: arrays repeat their key (category=a&category=b), empty ones are left out. */
export function query(params: Record<string, string | string[] | null | undefined>): string {
  const parts: string[] = [];
  for (const [key, value] of Object.entries(params)) {
    for (const one of Array.isArray(value) ? value : [value]) {
      if (one !== null && one !== undefined && one !== '') {
        parts.push(`${encodeURIComponent(key)}=${encodeURIComponent(one)}`);
      }
    }
  }
  return parts.join('&');
}

const period = (v: Values) => ({ from: v.from ?? '', to: v.to ?? '' });

/** The filters a link carries: the empty ones are left out. */
function given(params: Record<string, unknown>): Record<string, string | string[]> {
  return Object.fromEntries(Object.entries(params).filter(([, value]) => (typeof value === 'string' && value !== '') || (Array.isArray(value) && value.length > 0))) as Record<string, string | string[]>;
}
const ids = (rows: Record<string, unknown>[]) => rows.map((r, i) => ({ ...r, id: String(r['documentId'] ?? r['accountId'] ?? r['productId'] ?? r['partnerId'] ?? i) + `-${i}` }));

/** A statement's lines in their tree: each level further in, a heading's total on its row. */
export function statementRows(rows: readonly Record<string, unknown>[]): Row[] {
  return ids(rows.map(r => ({ ...r, id: r['id'], name: '\u00a0'.repeat(Number(r['level'] ?? 0) * 4) + String(r['name'] ?? r['code'] ?? '') })));
}

/** What the statement can't show: the accounts on no line, by code. */
function unplacedFigure(accounts: { code: string | null }[]): Figure[] {
  return accounts.length ? [{ label: t('Accounts on no line'), value: String(accounts.length), caption: accounts.map(a => a.code).join(', ') }] : [];
}

const statementColumns: Column<Row>[] = [text('code', 'Line', '90px'), text('name', 'Name'), num('amount', 'Amount', '160px'), num('previous', 'A year before', '160px')];

/** A running balance as a line chart over the posting dates. */
function balanceChart(rows: Record<string, unknown>[], title: string): ReportView['chart'] {
  const points = rows.filter(r => r['postingDate']);
  return points.length > 1 ? { title, spec: { kind: 'line', categories: points.map(r => String(r['postingDate'])), series: [{ name: 'Balance',
    values: points.map(r => Number(r['balance'] ?? 0)) }], decimals: 2 } } : undefined;
}

function ledgerView(lines: Record<string, unknown>[], extra: Figure[] = []): ReportView {
  const debit = lines.reduce((s, l) => s + Number(l['debit'] ?? 0), 0);
  const credit = lines.reduce((s, l) => s + Number(l['credit'] ?? 0), 0);
  const closing = lines.length ? Number(lines[lines.length - 1]['balance'] ?? 0) : 0;
  return {
    rows: ids(lines),
    footer: { document: t('Total'), debit: money(debit), credit: money(credit), balance: money(closing) },
    figures: [...extra, { label: t('Debit'), value: money(debit) }, { label: t('Credit'), value: money(credit) }, { label: t('Closing balance'), value: money(closing) }],
    chart: balanceChart(lines, 'Balance over the period'),
  };
}

/** Who stock is at, for the stock report's filter. */
export const HOLDERS = [{ value: 'place', label: t('Places') }, { value: 'partner', label: t('Clients and suppliers') }, { value: 'staff', label: t('Staff') }, { value: 'all', label: t('All') }];

interface LineGroup {
  id: string | null;
  name: string | null;
  quantity: number;
  value: number;
  vat: number;
  total: number;
}

const groupColumns = (label: string): Column<Row>[] => [text('name', label), { ...num('quantity', 'Quantity', '120px'), decimals: 3 }, num('value', 'Net'), num('vat', 'VAT', '110px'), num('total', 'Total')];

/** One line per client or cost centre, the largest first: the id stays on the row, for the lines behind it. */
function groupView(groups: LineGroup[], data: { value: number; vat: number; total: number }, chartTitle: string): ReportView {
  const sorted = [...groups].sort((a, b) => b.total - a.total);
  const top = sorted.slice(0, 8);
  return {
    rows: ids(sorted.map(g => ({ ...g, groupId: g.id, name: g.name ?? '—' }))), footer: { name: t('Total'), value: money(data.value), vat: money(data.vat), total: money(data.total) },
    figures: [{ label: t('Net'), value: money(data.value) }, { label: t('VAT'), value: money(data.vat) }, { label: t('Total'), value: money(data.total) }],
    chart: top.length ? { title: chartTitle, spec: { kind: 'bar', categories: top.map(g => g.name ?? '—'), series: [{ name: t('Total'), values: top.map(g => g.total) }], decimals: 2 } } : undefined,
  };
}

const linesData = (v: Values) => ({ path: 'reports/lines', params: { category: (v.documentCategories as string[]) ?? [], ...period(v) } });

export const REPORT_DEFS: Record<string, ReportDef> = {
  'trial-balance': {
    key: 'trial-balance', filters: ['period', 'chart', 'costCenter'], required: [],
    data: v => ({ path: 'reports/trial-balance', params: { ...period(v), chart: (v.chart as string) ?? '', costCenter: (v.costCenter as string) ?? '' } }),
    xlsx: v => `reports/trial-balance/xlsx?${query({ ...period(v), chart: v.chart as string, costCenter: v.costCenter as string })}`,
    pdf: v => `reports/trial-balance/pdf?${query({ ...period(v), chart: v.chart as string, costCenter: v.costCenter as string })}`,
    columns: [text('code', 'Account', '110px'), text('name', 'Name'), num('openingDebit', 'Opening debit'), num('openingCredit', 'Opening credit'),
      num('debit', 'Debit'), num('credit', 'Credit'), num('closingDebit', 'Closing debit'), num('closingCredit', 'Closing credit'),
      num('yearDebit', 'Debit, year to date'), num('yearCredit', 'Credit, year to date')],
    open: (row, v) => ({ report: 'ledger', params: given({ account: row['accountId'], ...period(v), costCenter: v.costCenter }) }),
    view: data => {
      const d = data as { rows: Record<string, unknown>[]; totalDebit: number; totalCredit: number; chart: string };
      return {
        rows: ids(d.rows),
        footer: { name: t('Total'), debit: money(d.totalDebit), credit: money(d.totalCredit) },
        figures: [{ label: t('Total debit'), value: money(d.totalDebit) }, { label: t('Total credit'), value: money(d.totalCredit) },
          { label: t('Difference'), value: money(d.totalDebit - d.totalCredit), caption: d.totalDebit === d.totalCredit ? 'Balanced' : 'Not balanced' }],
      };
    },
  },
  ledger: {
    key: 'ledger', filters: ['account', 'period', 'costCenter'], required: ['account'],
    data: v => ({ path: 'reports/ledger', params: { account: v.account as string, ...period(v), costCenter: (v.costCenter as string) ?? '' } }),
    xlsx: v => `reports/ledger/xlsx?${query({ account: v.account as string, ...period(v), costCenter: v.costCenter as string })}`,
    pdf: v => `reports/ledger/pdf?${query({ account: v.account as string, ...period(v), costCenter: v.costCenter as string })}`,
    columns: [date('postingDate'), text('document', 'Document', '120px'), text('account', 'Account', '110px'), text('from', 'From'), text('to', 'To'),
      num('debit', 'Debit'), num('credit', 'Credit'), num('balance', 'Balance')],
    view: data => {
      const d = data as { lines: Record<string, unknown>[]; opening: number };
      return ledgerView(d.lines, [{ label: t('Opening balance'), value: money(d.opening) }]);
    },
  },
  'partner-ledger': {
    key: 'partner-ledger', filters: ['partner', 'period'], required: ['partner'],
    data: v => ({ path: `partners/${v.partner}/ledger`, params: period(v) }),
    xlsx: v => `partners/${v.partner}/ledger/xlsx?${query(period(v))}`,
    pdf: v => `partners/${v.partner}/ledger/pdf?${query(period(v))}`,
    columns: [date('postingDate'), text('code', 'Document', '130px'), text('category', 'Category'), num('debit', 'Debit'), num('credit', 'Credit'), num('balance', 'Balance')],
    view: data => ledgerView(data as Record<string, unknown>[]),
  },
  'staff-ledger': {
    key: 'staff-ledger', filters: ['employee', 'period'], required: ['employee'],
    data: v => ({ path: `employees/${v.employee}/ledger`, params: period(v) }),
    columns: [date('postingDate'), text('document', 'Document'), num('debit', 'Debit'), num('credit', 'Credit'), num('balance', 'Balance')],
    view: data => ledgerView(data as Record<string, unknown>[]),
  },
  'cost-center-ledger': {
    key: 'cost-center-ledger', filters: ['costCenter', 'period'], required: ['costCenter'],
    data: v => ({ path: `cost-centers/${v.costCenter}/ledger`, params: period(v) }),
    xlsx: v => `cost-centers/${v.costCenter}/ledger/xlsx?${query(period(v))}`,
    pdf: v => `cost-centers/${v.costCenter}/ledger/pdf?${query(period(v))}`,
    columns: [date('postingDate'), text('document', 'Document'), num('debit', 'Debit'), num('credit', 'Credit'), num('balance', 'Balance')],
    view: data => ledgerView(data as Record<string, unknown>[]),
  },
  'vat-register': {
    key: 'vat-register', filters: ['documentCategories', 'period'], required: ['documentCategories'],
    data: v => ({ path: 'reports/vat-register', params: { category: (v.documentCategories as string[]) ?? [], ...period(v) } }),
    xlsx: v => `reports/vat-register/xlsx?${query({ category: v.documentCategories as string[], ...period(v) })}`,
    pdf: v => `reports/vat-register/pdf?${query({ category: v.documentCategories as string[], ...period(v) })}`,
    columns: [date('postingDate'), text('document', 'Document', '120px'), text('externalNumber', 'Their number', '120px'), text('documentType', 'Type'), text('partner', 'Partner'),
      text('taxNumber', 'Tax number', '110px'), text('city', 'City', '110px'), text('costCenter', 'Cost centre'), num('base', 'Base'), num('vat', 'VAT'), num('total', 'Total')],
    view: data => {
      const d = data as { lines: Record<string, unknown>[]; totals: { vatGroup: string | null; base: number; vat: number }[]; base: number; vat: number };
      // Each line carries its base and VAT (a reversal counts negative).
      const lines = d.lines.map(l => ({ ...l, total: Number(l['base'] ?? 0) + Number(l['vat'] ?? 0) }));
      const { base, vat } = d;
      return {
        rows: ids(lines), footer: { partner: t('Total'), base: money(base), vat: money(vat), total: money(base + vat) },
        figures: [{ label: t('Taxable base'), value: money(base) }, { label: t('VAT'), value: money(vat) },
          ...d.totals.map(row => ({ label: row.vatGroup ?? t('No VAT group'), value: money(row.vat), caption: `base ${money(row.base)}` }))],
      };
    },
  },
  'vat-return': {
    key: 'vat-return', filters: ['outputCategories', 'inputCategories', 'period'], required: [],
    data: v => ({ path: 'reports/vat-return', params: { output: (v.outputCategories as string[]) ?? [], input: (v.inputCategories as string[]) ?? [], ...period(v) } }),
    xlsx: v => `reports/vat-return/xlsx?${query({ output: v.outputCategories as string[], input: v.inputCategories as string[], ...period(v) })}`,
    pdf: v => `reports/vat-return/pdf?${query({ output: v.outputCategories as string[], input: v.inputCategories as string[], ...period(v) })}`,
    columns: [text('side', 'Side', '120px'), text('vatGroup', 'VAT group'), num('rate', 'Rate %', '100px'), num('base', 'Base'), num('vat', 'VAT')],
    view: data => {
      const d = data as { output: Record<string, unknown>[]; input: Record<string, unknown>[]; outputVat: number; inputVat: number };
      const rows = [...d.output.map(r => ({ ...r, side: 'Output (sales)' })), ...d.input.map(r => ({ ...r, side: 'Input (purchases)' }))];
      const owed = d.outputVat - d.inputVat;
      return {
        rows: ids(rows),
        figures: [{ label: t('Output VAT'), value: money(d.outputVat) }, { label: t('Input VAT'), value: money(d.inputVat) },
          { label: owed >= 0 ? 'VAT payable' : 'VAT to be refunded', value: money(Math.abs(owed)) }],
      };
    },
  },
  'balance-sheet': {
    key: 'balance-sheet', filters: ['day', 'chart'], required: [],
    data: v => ({ path: 'reports/balance-sheet', params: { to: v.to ?? '', chart: (v.chart as string) ?? '' } }),
    columns: statementColumns,
    view: data => {
      const d = data as { rows: Record<string, unknown>[]; assets: number; liabilitiesAndEquity: number; difference: number; unplaced: { code: string | null }[] };
      return {
        rows: statementRows(d.rows),
        figures: [{ label: t('Assets'), value: money(d.assets) }, { label: t('Liabilities and equity'), value: money(d.liabilitiesAndEquity) },
          { label: t('Difference'), value: money(d.difference), caption: d.difference === 0 ? 'The two sides meet' : 'The two sides do not meet' }, ...unplacedFigure(d.unplaced)],
      };
    },
  },
  'profit-loss': {
    key: 'profit-loss', filters: ['period', 'chart'], required: [],
    data: v => ({ path: 'reports/profit-loss', params: { ...period(v), chart: (v.chart as string) ?? '' } }),
    columns: statementColumns,
    view: data => {
      const d = data as { rows: Record<string, unknown>[]; result: number; previousResult: number; unplaced: { code: string | null }[] };
      return {
        rows: statementRows(d.rows),
        figures: [{ label: d.result >= 0 ? 'Profit' : 'Loss', value: money(Math.abs(d.result)) }, { label: t('A year before'), value: money(d.previousResult) }, ...unplacedFigure(d.unplaced)],
      };
    },
  },
  'vat-return-form': {
    key: 'vat-return-form', filters: ['period'], required: [],
    data: v => ({ path: 'reports/vat-return/form', params: period(v) }),
    columns: [text('code', 'Box', '100px'), text('name', 'Name'), text('side', 'Side', '160px'), num('value', 'Value')],
    view: data => {
      const d = data as { boxes: Record<string, unknown>[]; outputVat: number; inputVat: number; owed: number };
      return {
        rows: ids(d.boxes.map(b => ({ ...b, side: b['isInput'] ? 'Input (purchases)' : 'Output (sales)' }))),
        figures: [{ label: t('Output VAT'), value: money(d.outputVat) }, { label: t('Input VAT'), value: money(d.inputVat) },
          { label: d.owed >= 0 ? 'VAT payable' : 'VAT to be refunded', value: money(Math.abs(d.owed)) }],
      };
    },
  },
  'unbalanced-scenarios': {
    key: 'unbalanced-scenarios', filters: [], required: [],
    data: () => ({ path: 'posting/unbalanced-scenarios', params: {} }),
    columns: [text('documentType', 'Document type', '260px'), text('scenario', 'Scenario', '220px'), text('reason', 'Why it cannot balance')],
    view: data => ({ rows: ids(data as Record<string, unknown>[]) }),
  },
  stock: {
    key: 'stock', filters: ['holder', 'costCenter', 'productCategory'], required: [], defaults: { holder: 'place' },
    data: v => ({ path: 'reports/stock', params: { holder: (v.holder as string) ?? '', costCenter: (v.costCenter as string) ?? '', productCategory: (v.productCategory as string) ?? '' } }),
    xlsx: v => `reports/stock/xlsx?${query({ holder: v.holder as string, costCenter: v.costCenter as string, productCategory: v.productCategory as string })}`,
    pdf: v => `reports/stock/pdf?${query({ holder: v.holder as string, costCenter: v.costCenter as string, productCategory: v.productCategory as string })}`,
    columns: [text('costCenter', 'Held by'), text('code', 'Item', '110px'), text('product', 'Name'), text('unit', 'Unit', '100px'),
      { ...num('quantity', 'Quantity'), decimals: 3 }, num('unitCost', 'Unit cost'), num('value', 'Value')],
    view: data => {
      const rows = data as Record<string, unknown>[];
      const value = rows.reduce((s, r) => s + Number(r['value'] ?? 0), 0);
      return { rows: ids(rows), footer: { product: t('Total'), value: money(value) },
        figures: [{ label: t('Items held'), value: String(new Set(rows.map(r => r['productId'])).size) }, { label: t('Stock value'), value: money(value) },
          { label: t('Below zero'), value: String(rows.filter(r => Number(r['quantity']) < 0).length), caption: 'lines' }] };
    },
  },
  'item-tracking': {
    key: 'item-tracking', filters: ['product', 'period'], required: ['product'],
    data: v => ({ path: 'reports/item-tracking', params: { product: v.product as string, ...period(v) } }),
    xlsx: v => `reports/item-tracking/xlsx?${query({ product: v.product as string, ...period(v) })}`,
    pdf: v => `reports/item-tracking/pdf?${query({ product: v.product as string, ...period(v) })}`,
    columns: [date('postingDate'), text('document', 'Document', '120px'), text('documentType', 'Type'), text('from', 'From'), text('to', 'To'),
      { ...num('quantity', 'Quantity', '110px'), decimals: 3 }, text('unit', 'Unit', '90px'), num('value', 'Value')],
    view: data => {
      const rows = data as Record<string, unknown>[];
      return { rows: ids(rows), figures: [{ label: t('Movements'), value: String(rows.length) },
        { label: t('Quantity moved'), value: formatNumber(rows.reduce((s, r) => s + Number(r['quantity'] ?? 0), 0), 3) }] };
    },
  },
  lines: {
    key: 'lines', filters: ['documentCategories', 'period', 'costCenter', 'partner'], required: ['documentCategories'],
    data: v => ({ path: 'reports/lines', params: { category: (v.documentCategories as string[]) ?? [], ...period(v), costCenter: (v.costCenter as string) ?? '',
      partner: (v.partner as string) ?? '' } }),
    xlsx: v => `reports/lines/xlsx?${query({ category: v.documentCategories as string[], ...period(v), costCenter: v.costCenter as string, partner: v.partner as string })}`,
    pdf: v => `reports/lines/pdf?${query({ category: v.documentCategories as string[], ...period(v), costCenter: v.costCenter as string, partner: v.partner as string })}`,
    columns: [date('postingDate'), text('document', 'Document', '120px'), text('partner', 'Partner'), text('costCenter', 'Cost centre'), text('item', 'Item'),
      { ...num('quantity', 'Quantity', '100px'), decimals: 3 }, num('value', 'Net'), num('vat', 'VAT', '110px'), num('total', 'Total')],
    view: data => {
      const d = data as { lines: Record<string, unknown>[]; byPartner: { name: string | null; total: number }[]; value: number; vat: number; total: number };
      const top = [...d.byPartner].sort((a, b) => b.total - a.total).slice(0, 8);
      return {
        rows: ids(d.lines), footer: { item: t('Total'), value: money(d.value), vat: money(d.vat), total: money(d.total) },
        figures: [{ label: t('Net'), value: money(d.value) }, { label: t('VAT'), value: money(d.vat) }, { label: t('Total'), value: money(d.total) }],
        chart: top.length ? { title: t('Total by partner'), spec: { kind: 'bar', categories: top.map(p => p.name ?? '—'), series: [{ name: t('Total'), values: top.map(p => p.total) }],
          decimals: 2 } } : undefined,
      };
    },
  },
  'purchase-prices': {
    // What a product was bought at: each purchase line, oldest first; a reversed purchase stays, with its reversal beside it.
    key: 'purchase-prices', filters: ['product', 'period'], required: ['product'],
    data: v => ({ path: 'reports/purchase-prices', params: { product: v.product as string, ...period(v) } }),
    columns: [date('postingDate'), text('document', 'Document', '120px'), text('supplier', 'Supplier'), { ...num('quantity', 'Quantity', '110px'), decimals: 3 }, text('unit', 'Unit', '90px'),
      { ...num('unitPrice', 'Unit price'), decimals: 4 }, num('value', 'Value'), text('reversedBy', 'Reversed by', '130px')],
    view: data => {
      const rows = data as Record<string, unknown>[];
      const prices = rows.filter(r => !r['reversedBy']).map(r => Number(r['unitPrice'] ?? 0));
      return { rows: ids(rows), figures: prices.length ? [{ label: t('Last price'), value: money(prices[prices.length - 1]) }, { label: t('Lowest'), value: money(Math.min(...prices)) },
        { label: t('Highest'), value: money(Math.max(...prices)) }] : [],
        chart: prices.length > 1 ? { title: t('Unit price over the period'), spec: { kind: 'line', categories: rows.filter(r => !r['reversedBy']).map(r => String(r['postingDate'])),
          series: [{ name: 'Unit price', values: prices }], decimals: 2 } } : undefined };
    },
  },
  'lines-by-partner': {
    key: 'lines-by-partner', filters: ['documentCategories', 'period'], required: ['documentCategories'],
    data: linesData,
    columns: groupColumns(t('Client or supplier')),
    open: (row, v) => ({ report: 'lines', params: given({ documentCategories: v.documentCategories, ...period(v), partner: row['groupId'] }) }),
    view: data => {
      const d = data as { byPartner: LineGroup[]; value: number; vat: number; total: number };
      return groupView(d.byPartner, d, 'Total by client or supplier');
    },
  },
  ageing: {
    key: 'ageing', filters: ['day'], required: [],
    data: v => ({ path: 'reports/ageing', params: { to: v.to ?? '' } }),
    columns: [text('partner', 'Client or supplier'), text('side', 'Side', '110px'), num('notDue', 'Not due'), num('days30', '1–30 days'), num('days60', '31–60 days'),
      num('days90', '61–90 days'), num('over90', 'Over 90 days'), num('total', 'Total')],
    open: (row, v) => ({ report: 'open-items', params: given({ to: v.to, partner: row['partnerId'] }) }),
    view: data => {
      const rows = data as { partnerId: string; partner: string | null; receivable: boolean; notDue: number; days30: number; days60: number; days90: number; over90: number; total: number }[];
      const sum = (receivable: boolean) => rows.filter(r => r.receivable === receivable).reduce((all, r) => all + r.total, 0);
      return {
        rows: ids(rows.map(r => ({ ...r, side: r.receivable ? t('Client') : t('Supplier') }))),
        figures: [{ label: t('Clients owe'), value: money(sum(true)) }, { label: t('Owed to suppliers'), value: money(sum(false)) }],
      };
    },
  },
  'open-items': {
    key: 'open-items', filters: ['day', 'partner'], required: [],
    data: v => ({ path: 'reports/open-items', params: { to: v.to ?? '', partner: (v.partner as string) ?? '' } }),
    columns: [text('code', 'Document', '130px'), text('partner', 'Client or supplier'), text('side', 'Side', '110px'), { key: 'date', label: t('Date'), format: 'date', width: '110px' },
      { key: 'dueDate', label: t('Due date'), format: 'date', width: '110px' }, num('total', 'Total'), num('open', 'Open amount'), num('daysOverdue', 'Days late', '100px')],
    view: data => {
      const rows = data as { id: string; receivable: boolean; open: number }[];
      return { rows: ids(rows.map(r => ({ ...r, documentId: r.id, side: r.receivable ? t('Client') : t('Supplier') }))), footer: { code: t('Total'), open: money(rows.reduce((all, r) => all + r.open, 0)) } };
    },
  },
  'opportunity-forecast': {
    key: 'opportunity-forecast', filters: [], required: [],
    data: () => ({ path: 'opportunities/forecast', params: {} }),
    xlsx: () => 'opportunities/forecast/xlsx',
    columns: [text('month', 'Month', '120px'), text('currency', 'Currency', '110px'), num('open', 'Open amount'), num('weighted', 'Weighted by chance'), num('won', 'Won'),
      num('invoiced', 'Invoiced')],
    view: data => ({ rows: ids(data as Row[]) }),
  },
  'opportunity-documents': {
    key: 'opportunity-documents', filters: ['period', 'stage'], required: [],
    data: v => ({ path: 'opportunities/documents-report', params: given({ ...period(v), stage: v.stage }) }),
    xlsx: v => `opportunities/documents-report/xlsx?${query({ ...period(v), stage: (v.stage as string) ?? null })}`,
    columns: [text('opportunity', 'Opportunity'), text('stage', 'Stage', '140px'), text('partner', 'Partner'), text('code', 'Document', '120px'), text('type', 'Type'),
      date('date'), text('state', 'State', '100px'), num('totalLcu', 'Total')],
    view: data => ({ rows: ids((data as Row[]).map(r => ({ ...r, state: r['invoice'] ? t('Invoice') : r['posted'] ? t('Posted') : t('Draft') }))) }),
  },
  'lines-by-user': {
    key: 'lines-by-user', filters: ['documentCategories', 'period'], required: ['documentCategories'],
    data: linesData,
    columns: groupColumns(t('Waiter')),
    view: data => {
      const d = data as { byUser: LineGroup[]; value: number; vat: number; total: number };
      return groupView(d.byUser, d, t('Total by waiter'));
    },
  },
  'lines-by-cost-center': {
    key: 'lines-by-cost-center', filters: ['documentCategories', 'period'], required: ['documentCategories'],
    data: linesData,
    columns: groupColumns(t('Cost centre')),
    open: (row, v) => ({ report: 'lines', params: given({ documentCategories: v.documentCategories, ...period(v), costCenter: row['groupId'] }) }),
    view: data => {
      const d = data as { byCostCenter: LineGroup[]; value: number; vat: number; total: number };
      return groupView(d.byCostCenter, d, 'Total by cost centre');
    },
  },
  transfers: {
    key: 'transfers', filters: ['documentCategories', 'period', 'costCenter'], required: ['documentCategories'],
    data: v => ({ path: 'reports/lines', params: { category: (v.documentCategories as string[]) ?? [], ...period(v), costCenter: (v.costCenter as string) ?? '' } }),
    columns: [date('postingDate'), text('document', 'Document', '120px'), text('from', 'From'), text('to', 'To'), text('item', 'Item'),
      { ...num('quantity', 'Quantity', '100px'), decimals: 3 }, text('unit', 'Unit', '90px'), num('cost', 'Value')],
    view: data => {
      const d = data as { lines: Record<string, unknown>[] };
      const value = d.lines.reduce((s, l) => s + Number(l['cost'] ?? 0), 0);
      return { rows: ids(d.lines), footer: { item: t('Total'), cost: money(value) }, figures: [{ label: t('Lines'), value: String(d.lines.length) }, { label: t('Value'), value: money(value) }] };
    },
  },
  'document-totals': {
    key: 'document-totals', filters: ['documentCategories', 'period'], required: ['documentCategories'],
    data: v => ({ path: 'reports/document-totals', params: { category: (v.documentCategories as string[]) ?? [], ...period(v) } }),
    columns: [text('type', 'Document type'), { ...num('documents', 'Documents', '110px'), decimals: 0 }, num('value', 'Net'), num('vat', 'VAT', '110px'), num('total', 'Total')],
    view: data => {
      const d = data as { rows: Record<string, unknown>[]; documents: number; value: number; vat: number; total: number };
      return { rows: ids(d.rows), footer: { type: t('Total'), documents: String(d.documents), value: money(d.value), vat: money(d.vat), total: money(d.total) },
        figures: [{ label: t('Documents'), value: String(d.documents) }, { label: t('Net'), value: money(d.value) }, { label: t('VAT'), value: money(d.vat) }, { label: t('Total'), value: money(d.total) }] };
    },
  },
  contracts: {
    // Contracts are never posted, so drafts count; what was invoiced comes from the documents made from each.
    key: 'contracts', filters: ['documentCategories', 'partner', 'period'], required: ['documentCategories'],
    data: v => ({ path: 'reports/contracts', params: { category: (v.documentCategories as string[]) ?? [], partner: (v.partner as string) ?? '', ...period(v) } }),
    columns: [date('postingDate'), text('document', 'Document', '120px'), text('type', 'Type'), text('partner', 'Client or supplier'), date('validUntil', 'Ends'),
      num('value', 'Value'), num('invoiced', 'Invoiced'), num('remaining', 'Remaining')],
    view: data => {
      const rows = data as Record<string, unknown>[];
      const sum = (key: string) => rows.reduce((s, r) => s + Number(r[key] ?? 0), 0);
      return { rows: ids(rows), footer: { partner: t('Total'), value: money(sum('value')), invoiced: money(sum('invoiced')), remaining: money(sum('remaining')) },
        figures: [{ label: t('Contracts'), value: String(rows.length) }, { label: t('Value'), value: money(sum('value')) }, { label: t('Invoiced'), value: money(sum('invoiced')) }, { label: t('Remaining'), value: money(sum('remaining')) }] };
    },
  },
  'partner-statistics': {
    // Partners ranked by their documents of one document category (sales, purchases…).
    key: 'partner-statistics', filters: ['documentCategory', 'period'], required: ['documentCategory'],
    data: v => ({ path: 'statistics/partners', params: { category: v.documentCategory as string, ...period(v), pageSize: '500' } }),
    xlsx: v => `statistics/partners/xlsx?${query({ category: v.documentCategory as string, ...period(v) })}`,
    pdf: v => `statistics/partners/pdf?${query({ category: v.documentCategory as string, ...period(v) })}`,
    columns: [text('name', 'Partner'), { ...num('documents', 'Documents', '110px'), decimals: 0 }, num('debit', 'Debit'), num('credit', 'Credit'), num('balance', 'Balance')],
    view: data => {
      const d = data as { items: Record<string, unknown>[] };
      const rows = d.items.map(r => ({ ...r, balance: Number(r['debit'] ?? 0) - Number(r['credit'] ?? 0), documents: Number(r['documents'] ?? 0) }));
      const balance = rows.reduce((s, r) => s + r.balance, 0);
      return { rows: ids(rows), footer: { name: t('Total'), balance: money(balance) },
        figures: [{ label: t('Partners'), value: String(rows.length) }, { label: t('Total balance'), value: money(balance) }] };
    },
  },
};
