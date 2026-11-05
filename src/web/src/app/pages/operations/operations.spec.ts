import { describe, expect, it } from 'vitest';
import { rateChart } from './currencies';
import { validCode } from './currencies';
import { runTotals, undoMessage } from './depreciation';
import { withoutSerial } from './serials';
import { sizeText } from './files';
import { short } from './production';
import { changeTitle, lastWeek } from './user-activity';
import { lastText, openingText, resultText, stepTree } from './migration';
import { hasScreen } from './migration-file';

describe('operations rules', () => {
  it('counts the stock still without a serial number', () => {
    expect(withoutSerial({ quantity: 5, serials: 2 })).toBe(3);
    expect(withoutSerial({ quantity: 2, serials: 3 })).toBe(0);
  });

  it('starts user activity on the last seven days and names the change it shows', () => {
    expect(lastWeek(new Date('2026-11-02T10:00:00Z'))).toEqual({ from: '2026-10-27', to: '2026-11-02' });
    expect(changeTitle({ action: 'Changed', record: 'C1 Bar Lura', document: null })).toBe('Changed: C1 Bar Lura');
    expect(changeTitle({ action: 'Deleted', record: null, document: 'FSHM50' })).toBe('Deleted: FSHM50');
    expect(changeTitle({ action: 'Added', record: null, document: null })).toBe('Added: this row');
  });

  it('says what a checked file will do, and what the last import did', () => {
    expect(resultText({ rows: 3, added: 2, updated: 1, errors: [] })).toBe('3 rows checked: 2 to add, 1 to update.');
    expect(resultText({ rows: 3, added: 2, updated: 0, errors: [{ row: 2, column: 'Type', message: 'x' }] })).toBe('1 problem to fix. Nothing was saved.');
    expect(resultText({ rows: 3, added: 0, updated: 0, errors: [{ row: 2, column: null, message: 'x' }, { row: 3, column: null, message: 'y' }] })).toBe('2 problems to fix. Nothing was saved.');
    expect(lastText(null)).toBe('');
    expect(openingText({ account: '890', name: 'Bilanci i çeljes', balance: 0 })).toBe('890 Bilanci i çeljes stands at zero.');
    expect(openingText({ account: '890', name: null, balance: -400 })).toContain('It must end at zero.');
    expect(lastText({ at: '2026-11-02T10:00:00Z', user: 'admin', fileName: 'a.xlsx', added: 2, updated: 1 })).toContain('admin: 2 added, 1 updated');
  });

  it('builds the steps into a tree, numbered as it reads', () => {
    const tree = stepTree([{ code: 'A', parent: null }, { code: 'B', parent: 'A' }, { code: 'C', parent: 'B' }, { code: 'D', parent: null }]);
    expect(tree.map(s => s.code)).toEqual(['A', 'D']);
    expect(tree[1].number).toBe(4);
    expect((tree[0].children[0] as { code: string; number: number }).number).toBe(2);
  });

  it('finds a screen at any depth of the menu', () => {
    const menu = [{ screen: null, children: [{ screen: 'migration', children: [] }] }];
    expect(hasScreen(menu, 'migration')).toBe(true);
    expect(hasScreen(menu, 'dashboard')).toBe(false);
  });

  it('takes a currency code of three letters', () => {
    expect(['NOK', 'nok', ' NOK '].map(validCode)).toEqual([true, true, true]);
    expect(['NO', 'NOKK', 'N0K', ''].map(validCode)).toEqual([false, false, false, false]);
  });

  it('says what undoing a run does, by its journal', () => {
    expect(undoMessage({ date: '2026-10-31', document: null, posted: false })).toContain('The run of 31/10/2026 is removed');
    expect(undoMessage({ date: '2026-10-31', document: 'FAA3', posted: true })).toContain('Journal FAA3 is reversed on 31/10/2026');
    expect(undoMessage({ date: '2026-10-31', document: 'FAA3', posted: false })).toContain('not posted, is removed');
  });

  it('flags an ingredient its place holds too little of, counting the waste', () => {
    expect(short({ productId: 'p', name: 'Flour', unitId: 'kg', quantity: 2, wasteQuantity: 0.5, held: 2.4 })).toBe(true);
    expect(short({ productId: 'p', name: 'Flour', unitId: 'kg', quantity: 2, wasteQuantity: 0.5, held: 2.5 })).toBe(false);
    expect(short({ productId: 'p', name: 'Flour', unitId: 'kg', quantity: 2, wasteQuantity: 0, held: null })).toBe(false);
  });

  it('adds up a depreciation run', () => {
    const line = (amount: number, before: number) => ({ assetValueId: 'a', productId: 'p', productName: 'Oven', costCenterId: null, from: '2026-10-01', to: '2026-10-31',
      days: 31, amount, bookValueBefore: before, bookValueAfter: before - amount });
    expect(runTotals([line(100, 1200), line(50, 600)])).toEqual({ amount: 150, before: 1800, after: 1650 });
  });

  it('draws rates oldest first, and only from two of them', () => {
    const rate = (day: string, value: number) => ({ id: day, currencyId: 'eur', rateDate: day, rate: value });
    const spec = rateChart([rate('2026-10-30', 100.5), rate('2026-10-01', 101.2)], 'EUR')!;
    expect(spec.categories).toEqual(['2026-10-01', '2026-10-30']);
    expect(spec.series[0].values).toEqual([101.2, 100.5]);
    expect(rateChart([rate('2026-10-01', 1)], 'EUR')).toBeNull();
  });

  it('writes sizes people read', () => {
    expect([sizeText(512), sizeText(2048), sizeText(3 * 1024 * 1024)]).toEqual(['512 B', '2.0 KB', '3.0 MB']);
  });
});
