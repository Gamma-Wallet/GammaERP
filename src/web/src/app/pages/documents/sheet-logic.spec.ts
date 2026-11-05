import { describe, expect, it } from 'vitest';
import { DocumentForm } from '../../core/documents';
import { blankRow, journalOf, locationSide, rowOf, sheetTotals, SheetRow, withAmount } from './sheet-logic';

const form = (from: string | null, to: string | null) => ({
  from: from ? { scopeId: 'from', table: from, name: '' } : null, to: to ? { scopeId: 'to', table: to, name: '' } : null,
  vatGroups: [{ id: 'v20', code: null, name: '20', rate: 20 }, { id: 'v0', code: null, name: '0', rate: 0 }],
}) as unknown as DocumentForm;
const row = (debit: number | null, credit: number | null): SheetRow => ({ ...blankRow('entry'), debit, credit });

describe('a sheet of journals', () => {
  it('finds the location on the side that holds cost centres', () => {
    expect(locationSide(form('tbl_cost_center', 'tbl_cost_center'))?.side).toBe('from');
    expect(locationSide(form('tbl_partner', 'tbl_cost_center'))?.side).toBe('to');
    expect(locationSide(form('tbl_partner', null))).toBeNull();
  });

  it('keeps a row to one side: typing a debit clears the credit, clearing it does not', () => {
    expect(withAmount(row(null, 50), 'debit', '12,5')).toMatchObject({ debit: 12.5, credit: null });
    expect(withAmount(row(null, 50), 'debit', '')).toMatchObject({ debit: null, credit: 50 });
    expect(withAmount(row(10, null), 'credit', 'x')).toMatchObject({ debit: 10, credit: null });
  });

  it('adds the rows up to the cent and shows what is left', () => {
    expect(sheetTotals([row(0.1, null), row(0.2, null), row(null, 0.3)])).toEqual({ debit: 0.3, credit: 0.3, difference: 0 });
    expect(sheetTotals([row(100, null), row(null, 90)]).difference).toBe(10);
  });

  it('a new row keeps the location of the one before', () => {
    expect(blankRow('entry', { ...row(1, null), locationId: 'hq', locationName: 'Zyra' })).toMatchObject({ locationId: 'hq', debit: null });
  });

  it('saves a row as one journal with one transaction at no VAT, dated by the sheet, and reads it back', () => {
    const typed: SheetRow = { ...row(100, null), lineTypeId: 'account', confId: 'c', elementId: 'rent', elementName: 'Rent', locationId: 'hq', locationName: 'Zyra', description: ' Tetor ' };
    const journal = journalOf(typed, form('tbl_cost_center', null), 'journals');
    expect(journal.transactions).toHaveLength(1);
    expect(journal.transactions[0]).toMatchObject({ typeId: 'account', quantity: 1, price: 100, debit: 100, credit: 0, vatGroupId: 'v0', fromId: 'hq' });
    expect(journal.postingDate).toBeNull();
    expect(rowOf({ ...journal, id: 'j' })).toMatchObject({ id: 'j', elementId: 'rent', locationId: 'hq', debit: 100, description: 'Tetor' });
    expect(journalOf(typed, form('tbl_cost_center', 'tbl_cost_center'), 'journals').transactions[0]).toMatchObject({ fromId: 'hq', toId: 'hq' });
  });
});
