import { describe, expect, it } from 'vitest';
import { blankDocument, dayOf, DocumentForm, ledgerOf, Line, LineFill, LineKind } from '../../core/documents';
import { serialsChosen } from './document-dialogs';
import { parseSerials, serialsText } from './serial-dialog';
import { addressesOf, changedQuantities } from './document-dialogs';
import { documentToSave } from './document-editor';
import { apiSort } from './document-list';
import { amountsOf, columnsOf, newLine, totalsOf, uses } from './line-grid';

const fill = (change: Partial<LineFill> = {}): LineFill => ({
  vatGroup: 1, element: 1, unit: 1, location: 2, unitPrice: 0, discount: 0, quantity: 0, value: 0, vat: 0, defaultQuantity: null, defaultDiscount: null, ...change,
});

const kind = (id: string, f: LineFill | null): LineKind => ({
  transactionTypeId: id, code: id, name: id, scopes: [{ confId: `conf-${id}`, scopeId: `scope-${id}`, table: 'tbl_product', name: 'Products', stockDirection: 1 }],
  fill: f, from: null, to: null,
});

const form: DocumentForm = {
  typeId: 't', code: 'FSHM', name: 'Sales invoice', neverPosted: false, allowsPartialReversal: true, isReversal: false, from: null, to: null,
  paymentMethods: [], vatGroups: [{ id: 'v20', code: 'A', name: '20 %', rate: 20 }, { id: 'v0', code: 'E', name: 'Exempt', rate: 0 }],
  currencies: [{ id: 'all', code: 'ALL', name: 'Lek', isBase: true }], lineKinds: [kind('ITEM', fill({ defaultQuantity: 2 })), kind('ACCOUNT', fill({ unit: 2, discount: 2 }))],
};

describe('the day of a document\'s rate', () => {
  it('is its posting date, then its document date, then today', () => {
    expect(dayOf({ postingDate: '2026-10-05', documentDate: '2026-10-01' }, '2026-11-01')).toBe('2026-10-05');
    expect(dayOf({ postingDate: null, documentDate: '2026-10-01' }, '2026-11-01')).toBe('2026-10-01');
    expect(dayOf({}, '2026-11-01')).toBe('2026-11-01');
  });
});

describe('serial numbers', () => {
  it('reads one per line, a part number after a comma', () => {
    expect(parseSerials('A1, P1\n\n  A2 \nA3;P3')).toEqual([{ serial: 'A1', partNumber: 'P1' }, { serial: 'A2', partNumber: null }, { serial: 'A3', partNumber: 'P3' }]);
    expect(parseSerials(serialsText([{ serial: 'A1', partNumber: 'P1' }, { serial: 'A2' }])).map(s => s.serial)).toEqual(['A1', 'A2']);
  });

  it('reverses by the numbers chosen, a line with all of them whole', () => {
    const line = (id: string, ...numbers: string[]) => ({ id, typeId: 't', serials: numbers.map(serial => ({ serial })) }) as Line;
    expect(serialsChosen([line('a', 'A1', 'A2'), line('b', 'B1')], { a: ['A2'], b: ['B1'] })).toEqual({ quantities: { a: 1 }, serials: { a: ['A2'] } });
    expect(serialsChosen([line('a', 'A1')], {})).toEqual({ quantities: {}, serials: {} });
  });
});

describe('the ledger behind a balance', () => {
  it('opens the side\'s own ledger for this year so far', () => {
    expect(ledgerOf('tbl_partner', 'p1', '2026-11-01')).toEqual({ report: 'partner-ledger', params: { partner: 'p1', from: '2026-01-01', to: '2026-11-01' } });
    expect(ledgerOf('tbl_cost_center', 'c1', '2026-11-01')?.report).toBe('cost-center-ledger');
    expect(ledgerOf('tbl_employee', 'e1', '2026-11-01')?.params['employee']).toBe('e1');
  });

  it('has none for a side without a ledger', () => {
    expect(ledgerOf('tbl_chart', 'a1', '2026-11-01')).toBeNull();
  });
});

describe('document rules', () => {
  it('shows a column any kind uses, and switches a cell off where its kind does not need it', () => {
    expect(uses(null, 'unit')).toBe(true);
    expect(uses(fill({ unit: 2 }), 'unit')).toBe(false);
    expect([...columnsOf(form.lineKinds)].sort()).toEqual(['discount', 'element', 'quantity', 'unit', 'unitPrice', 'vatGroup']);
    expect([...columnsOf([form.lineKinds[1]])]).not.toContain('unit');
  });

  it('works out amounts as the server does: (price − discount) × quantity, VAT on the net', () => {
    expect(amountsOf({ typeId: 'x', price: 10, discount: 1, quantity: 3 }, 20)).toEqual({ value: 27, vat: 5.4, total: 32.4 });
    const totals = totalsOf([{ typeId: 'x', price: 10, discount: 1, quantity: 3, vatGroupId: 'v20' }, { typeId: 'x', price: 5, quantity: 2, vatGroupId: 'v0' }],
      id => (id === 'v20' ? 20 : 0));
    expect(totals).toEqual({ discount: 3, value: 37, vat: 5.4, total: 42.4 });
  });

  it('starts a line with its kind defaults, the first VAT group and configuration', () => {
    const line = newLine(form.lineKinds[0], form, 'all');
    expect([line.typeId, line.typeConfId, line.quantity, line.vatGroupId, line.currencyId]).toEqual(['ITEM', 'conf-ITEM', 2, 'v20', 'all']);
    expect(newLine(kind('X', fill({ vatGroup: 2 })), form, null).vatGroupId).toBeNull();
  });

  it('sorts the list on the server by its own column names', () => {
    expect(apiSort({ key: 'fromName', direction: 'desc' })).toEqual({ sort: 'from', descending: true });
    expect(apiSort({ key: 'value', direction: 'asc' })).toEqual({ sort: null, descending: false });
    expect(apiSort(null)).toEqual({ sort: null, descending: false });
  });

  it('reverses only the changed quantities, or whole lines when none changed', () => {
    const lines = [{ id: 'a', typeId: 'x', quantity: 5 }, { id: 'b', typeId: 'x', quantity: 2 }];
    expect(changedQuantities(lines, { a: 5, b: 1 })).toEqual({ b: 1 });
    expect(changedQuantities(lines, {})).toBeNull();
  });

  it('reads several e-mail addresses, and sends a document without the screen fields', () => {
    expect(addressesOf(' a@x.al, b@y.it; c@z.cy ')).toEqual(['a@x.al', 'b@y.it', 'c@z.cy']);
    const body = documentToSave(blankDocument('t', 'sales'), [{ key: 'k1', units: [], typeId: 'ITEM', quantity: 1 }]);
    expect(body.transactions).toEqual([{ typeId: 'ITEM', quantity: 1 }]);
    expect(body.categoryIds).toEqual(['sales']);
  });
});
