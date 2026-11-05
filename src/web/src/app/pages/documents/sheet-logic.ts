import { blankDocument, DocumentData, DocumentForm, Side } from '../../core/documents';

/** A row of a sheet's grid: one journal under the sheet with its one transaction. */
export interface SheetRow {
  /** The journal's id once saved. */
  id: string | null;
  /** The journal's document type, one under the sheet's. */
  typeId: string;
  lineTypeId: string | null;
  confId: string | null;
  elementId: string | null;
  elementName: string | null;
  locationId: string | null;
  locationName: string | null;
  debit: number | null;
  credit: number | null;
  description: string;
}

const COST_CENTERS = 'tbl_cost_center';

/** The side of a journal type that holds its location, a cost centre: the same one the server reads. */
export function locationSide(form: DocumentForm | undefined): { side: 'from' | 'to'; scope: Side } | null {
  if (form?.from?.table === COST_CENTERS) {
    return { side: 'from', scope: form.from };
  }
  return form?.to?.table === COST_CENTERS ? { side: 'to', scope: form.to } : null;
}

export function blankRow(typeId: string, after?: SheetRow): SheetRow {
  return { id: null, typeId, lineTypeId: null, confId: null, elementId: null, elementName: null, locationId: after?.locationId ?? null, locationName: after?.locationName ?? null,
    debit: null, credit: null, description: '' };
}

/** A row's amount: one side at a time, so typing a debit clears the credit. */
export function withAmount(row: SheetRow, field: 'debit' | 'credit', typed: string): SheetRow {
  const value = typed.trim() === '' ? null : Number(typed.replace(',', '.'));
  const amount = value === null || Number.isNaN(value) ? null : value;
  return { ...row, [field]: amount, ...(amount ? { [field === 'debit' ? 'credit' : 'debit']: null } : {}) };
}

/** Debit and credit of the rows and what is left between them, to the cent. */
export function sheetTotals(rows: readonly SheetRow[]): { debit: number; credit: number; difference: number } {
  const cents = (pick: (r: SheetRow) => number | null) => rows.reduce((sum, r) => sum + Math.round((pick(r) ?? 0) * 100), 0);
  const [debit, credit] = [cents(r => r.debit), cents(r => r.credit)];
  return { debit: debit / 100, credit: credit / 100, difference: (debit - credit) / 100 };
}

/** A journal as a row of the grid. */
export function rowOf(journal: DocumentData): SheetRow {
  const line = journal.transactions[0];
  return {
    id: journal.id, typeId: journal.typeId, lineTypeId: line?.typeId ?? null, confId: line?.typeConfId ?? null, elementId: line?.elementId ?? null,
    elementName: line?.elementName ?? null, locationId: line?.fromId ?? line?.toId ?? null, locationName: line?.fromName ?? line?.toName ?? null, debit: line?.debit || null,
    credit: line?.credit || null, description: journal.description?.['en'] ?? '',
  };
}

/** A row as the journal saved under the sheet: dated by the sheet, one transaction of quantity 1, no VAT, its amounts the debit and credit. */
export function journalOf(row: SheetRow, form: DocumentForm | undefined, categoryId: string): DocumentData {
  const location = locationSide(form);
  const [debit, credit] = [row.debit ?? 0, row.credit ?? 0];
  return {
    ...blankDocument(row.typeId, categoryId), id: row.id, postingDate: null, documentDate: null, description: row.description.trim() ? { en: row.description.trim() } : null,
    transactions: [{
      typeId: row.lineTypeId ?? '', typeConfId: row.confId, elementId: row.elementId, elementName: row.elementName, quantity: 1, price: debit + credit, debit, credit,
      vatGroupId: form?.vatGroups.find(v => v.rate === 0)?.id ?? null,
      // On every side that holds cost centres: a reversal swaps the sides, and the place must still be on its side.
      ...(form?.from?.table === COST_CENTERS ? { fromId: row.locationId, fromName: row.locationName } : {}),
      ...(form?.to?.table === COST_CENTERS ? { toId: row.locationId, toName: row.locationName } : {}),
    }],
  };
}
