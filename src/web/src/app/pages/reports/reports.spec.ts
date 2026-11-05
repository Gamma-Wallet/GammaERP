import { describe, expect, it } from 'vitest';
import { REPORT_DEFS, query, statementRows } from './report-defs';
import { changeOf, chartOf, DashboardItem, linkOf } from './dashboard';
import { fileName, linkedValues, monthsChart, ready, thisMonth } from './report-page';


describe('dashboard rules', () => {
  const item = (rows: DashboardItem['rows']): DashboardItem => ({ id: '1', code: 'X', name: 'X', kind: 'Monthly', value: null, previous: null, firstName: 'Sales', secondName: 'Purchases',
    documentCategoryId: 'c', secondCategoryId: 'd', negate: false, rows });

  it('moves a total by percent, and not at all from nothing', () => {
    expect(changeOf({ value: 150, previous: 100 })).toBe(50);
    expect(changeOf({ value: 90, previous: 120 })).toBe(-25);
    expect(changeOf({ value: 90, previous: -60 })).toBe(250);
    expect(changeOf({ value: 90, previous: 0 })).toBeNull();
    expect(changeOf({ value: 90, previous: null })).toBeNull();
  });

  it('draws one series, or two on a comparison', () => {
    const one = chartOf(item([{ name: '2026-10', value: 5, second: null, id: null }]), 'ALL');
    expect(one.series.map(s => s.name)).toEqual(['Sales']);
    const two = chartOf(item([{ name: '2026-10', value: 5, second: 3, id: null }]), 'ALL');
    expect(two.series).toEqual([{ name: 'Sales', values: [5] }, { name: 'Purchases', values: [3] }]);
    expect(two.unit).toBe('ALL');
  });

  it('opens the report behind each kind, one that can show a chosen cost centre', () => {
    const of = (kind: string, centre: string | null = null) => linkOf({ kind, documentCategoryId: 'c' }, '2026-10-01', '2026-10-31', centre);
    expect(of('Total')?.path).toEqual(['/reports', 'document-totals']);
    expect(of('Total', 'cc')).toEqual({ path: ['/reports', 'lines'], params: { documentCategories: 'c', from: '2026-10-01', to: '2026-10-31', costCenter: 'cc' } });
    expect(of('Balance')?.params).toEqual({ documentCategory: 'c', from: '2026-10-01', to: '2026-10-31' });
    expect(of('Drafts')).toEqual({ path: ['/documents', 'c'], params: {} });
    expect(of('TopPartners')?.path[1]).toBe('lines-by-partner');
    expect(linkOf({ kind: 'Total', documentCategoryId: null }, 'a', 'b', null)).toBeNull();
  });
});

describe('report rules', () => {
  it('writes a query with repeated keys for several values, leaving empty ones out', () => {
    expect(query({ category: ['a', 'b'], from: '2026-10-01', costCenter: null, partner: '' })).toBe('category=a&category=b&from=2026-10-01');
  });

  it('opens on this month and waits for the required filters', () => {
    expect(thisMonth(new Date(2026, 9, 31))).toEqual({ from: '2026-10-01', to: '2026-10-31' });
    expect(ready(['account'], { from: '2026-10-01' })).toBe(false);
    expect(ready(['documentCategories'], { documentCategories: [] })).toBe(false);
    expect(ready(['documentCategories'], { documentCategories: ['sales'] })).toBe(true);
  });

  it('shows a statement as a tree: each level further in', () => {
    const rows = statementRows([{ id: 'a', code: 'A', name: 'Assets', level: 0, amount: 100 }, { id: 'b', code: 'A1', name: 'Cash', level: 1, amount: 100 }]);
    expect(rows.map(r => r['name'])).toEqual(['Assets', '\u00a0\u00a0\u00a0\u00a0Cash']);
    expect(new Set(rows.map(r => r.id)).size).toBe(2);
  });

  it('opens the ledger of a trial balance row for the same period and cost centre', () => {
    const open = REPORT_DEFS['trial-balance'].open!;
    const row = { id: 'x', accountId: 'a1' };
    expect(open(row, { from: '2026-01-01', to: '2026-03-31', costCenter: 'c1' })).toEqual({ report: 'ledger', params: { account: 'a1', from: '2026-01-01', to: '2026-03-31', costCenter: 'c1' } });
    expect(open(row, { from: '2026-01-01', to: '2026-03-31', costCenter: null }).params).toEqual({ account: 'a1', from: '2026-01-01', to: '2026-03-31' });
  });

  it('shows the year to date on the trial balance', () => {
    expect(REPORT_DEFS['trial-balance'].columns.map(c => c.key)).toEqual(expect.arrayContaining(['yearDebit', 'yearCredit']));
  });

  it('takes from a link only the period and the filters the report has', () => {
    const base = { from: '2026-11-01', to: '2026-11-30' };
    expect(linkedValues({ account: 'a1', from: '2026-01-01', to: '2026-03-31', other: 'x' }, ['account', 'period'], base)).toEqual({ account: 'a1', from: '2026-01-01', to: '2026-03-31' });
    expect(linkedValues({}, ['account'], base)).toEqual(base);
  });

  it('shows sales by client, the largest first, and opens the lines behind one', () => {
    const def = REPORT_DEFS['lines-by-partner'];
    const view = def.view({ byPartner: [{ id: 'p1', name: 'Small', quantity: 1, value: 100, vat: 20, total: 120 }, { id: 'p2', name: 'Large', quantity: 2, value: 500, vat: 100, total: 600 }],
      value: 600, vat: 120, total: 720 });
    expect(view.rows.map(r => r['name'])).toEqual(['Large', 'Small']);
    expect(def.open!(view.rows[0], { documentCategories: ['sales'], from: '2026-01-01', to: '2026-03-31' })).toEqual({ report: 'lines',
      params: { documentCategories: ['sales'], from: '2026-01-01', to: '2026-03-31', partner: 'p2' } });
  });

  it('takes one category from a link as a list', () => {
    expect(linkedValues({ documentCategories: 'sales' }, ['documentCategories'], {}).documentCategories).toEqual(['sales']);
  });

  it('shows stock at the places unless another holder is chosen', () => {
    expect(REPORT_DEFS['stock'].defaults).toEqual({ holder: 'place' });
    expect(REPORT_DEFS['stock'].data({ holder: 'staff' }).params['holder']).toBe('staff');
  });

  it('adds up the contracts and what remains of them', () => {
    const view = REPORT_DEFS['contracts'].view([{ documentId: 'c1', value: 1000, invoiced: 400, remaining: 600 }, { documentId: 'c2', value: 500, invoiced: 0, remaining: 500 }]);
    expect(view.footer).toMatchObject({ value: '1,500.00', invoiced: '400.00', remaining: '1,100.00' });
  });

  it('shows the last, lowest and highest purchase price, leaving out reversed purchases', () => {
    const view = REPORT_DEFS['purchase-prices'].view([{ documentId: 'a', postingDate: '2026-09-01', unitPrice: 100 }, { documentId: 'b', postingDate: '2026-10-01', unitPrice: 80 },
      { documentId: 'c', postingDate: '2026-10-02', unitPrice: 500, reversedBy: 'FKBM0' }]);
    expect(view.rows).toHaveLength(3);
    expect(view.figures!.map(f => f.value)).toEqual(['80.00', '80.00', '100.00']);
  });

  it('saves a download under the server name', () => {
    expect(fileName("attachment; filename=stock.pdf; filename*=UTF-8''stock-2026-10-31.pdf", 'x.pdf')).toBe('stock-2026-10-31.pdf');
    expect(fileName('attachment; filename="ledger.xlsx"', 'x.xlsx')).toBe('ledger.xlsx');
    expect(fileName(null, 'x.xlsx')).toBe('x.xlsx');
  });

  it('charts a partner by month with a series per document category', () => {
    const spec = monthsChart([{ month: '2026-09', category: 'Sales', valueVat: 120 }, { month: '2026-10', category: 'Sales', valueVat: 60 },
      { month: '2026-10', category: 'Cash', valueVat: 100 }])!;
    expect(spec.categories).toEqual(['2026-09', '2026-10']);
    expect(spec.series).toEqual([{ name: 'Sales', values: [120, 60] }, { name: 'Cash', values: [0, 100] }]);
    expect(monthsChart([])).toBeNull();
  });

  it('shows a trial balance balanced, a ledger closing balance and a VAT return payable or refundable', () => {
    const trial = REPORT_DEFS['trial-balance'].view({ rows: [{ accountId: 'a', code: '411' }], totalDebit: 120, totalCredit: 120, chart: 'SKK' });
    expect(trial.figures!.find(f => f.label === 'Difference')).toMatchObject({ value: '0.00', caption: 'Balanced' });

    const ledger = REPORT_DEFS['partner-ledger'].view([{ postingDate: '2026-10-01', debit: 120, credit: 0, balance: 120 },
      { postingDate: '2026-10-05', debit: 0, credit: 50, balance: 70 }]);
    expect(ledger.footer).toEqual({ document: 'Total', debit: '120.00', credit: '50.00', balance: '70.00' });
    expect(ledger.chart!.spec.series[0].values).toEqual([120, 70]);

    const vat = REPORT_DEFS['vat-return'].view({ output: [{ vatGroup: 'A', rate: 20, base: 100, vat: 20 }], input: [{ vatGroup: 'A', rate: 20, base: 200, vat: 40 }],
      outputVat: 20, inputVat: 40 });
    expect(vat.figures!.at(-1)).toMatchObject({ label: 'VAT to be refunded', value: '20.00' });
    expect(vat.rows.map(r => r['side'])).toEqual(['Output (sales)', 'Input (purchases)']);
  });
});
