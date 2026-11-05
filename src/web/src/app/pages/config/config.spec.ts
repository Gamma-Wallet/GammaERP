import { describe, expect, it } from 'vitest';
import { ConfigSchema } from '../../core/config';
import { Category, ElementType } from '../../core/entities';
import { distinctChoices, formColumns, numberOf } from './config-row-dialog';
import { cellOf, shownColumns } from './config-table';
import { groupTypes } from './document-types';
import { codedText, columnLabel } from './labels';
import { rightsOf, rowsOf } from './rights';

const schema: ConfigSchema = {
  model: 'transaction-fills',
  columns: [
    { name: 'documentTypeTransactionTypeId', kind: 'id', required: false, isLink: true, linkModel: 'document-type-transaction-types' },
    { name: 'vatGroup', kind: 'integer', required: false, isLink: false, linkModel: null },
    { name: 'element', kind: 'integer', required: false, isLink: false, linkModel: null },
    { name: 'defaultQuantity', kind: 'number', required: false, isLink: false, linkModel: null },
    { name: 'name', kind: 'translated', required: true, isLink: false, linkModel: null },
    { name: 'code', kind: 'text', required: false, isLink: false, linkModel: null },
    { name: 'extra', kind: 'json', required: false, isLink: false, linkModel: null },
  ],
};

const category = (id: string, isElement = true): Category => ({ id, idSup: null, active: true, isElement, code: id, name: { en: id }, description: null, sortOrder: 0 });
const type = (id: string, categoryIds: string[]): ElementType => ({
  id, idSup: null, active: true, isElement: false, code: id, codeStart: null, codeEnd: null, codeCurrent: null, name: { en: id }, description: null, sortOrder: 0,
  categoryIds, properties: [],
});

import { comesWith, menuTree } from './rights';

describe('menu rights', () => {
  const rows = [{ id: 'a', parentId: null }, { id: 'b', parentId: 'a' }, { id: 'c', parentId: 'b' }, { id: 'd', parentId: null }];

  it('builds the whole menu as a tree', () => {
    const tree = menuTree(rows);
    expect(tree.map(n => n.id)).toEqual(['a', 'd']);
    expect((tree[0].children[0] as { id: string }).id).toBe('b');
  });

  it('gives what sits under a folder with the folder', () => {
    expect(comesWith(rows, 'c', new Set(['a']))).toBe(true);
    expect(comesWith(rows, 'a', new Set(['a']))).toBe(false);
    expect(comesWith(rows, 'd', new Set(['a']))).toBe(false);
  });
});

describe('config choices', () => {
  const options = [{ value: 'a', label: 'Accounts' }, { value: 'b', label: 'Accounts' }, { value: 'c', label: 'Staff' }, { value: 'd', label: 'Accounts', hint: '401' }];

  it('offers rows that read the same once', () => {
    expect(distinctChoices(options, null).map(o => o.value)).toEqual(['a', 'c', 'd']);
  });

  it('leaves out a row with nothing to read by, unless the row holds it', () => {
    const blank = [{ value: 'a', label: '—' }, { value: 'b', label: 'Staff' }];
    expect(distinctChoices(blank, null).map(o => o.value)).toEqual(['b']);
    expect(distinctChoices(blank, 'a').map(o => o.value)).toEqual(['a', 'b']);
  });

  it('keeps the one the row already holds', () => {
    expect(distinctChoices(options, 'b').map(o => o.value)).toEqual(['b', 'c', 'd']);
  });
});

describe('configuration screens', () => {
  it('names columns for people', () => {
    expect(columnLabel('vatGroupId')).toBe('VAT group');
    expect(columnLabel('valueVatLcu')).toBe('Value VAT LCU');
    expect(columnLabel('defaultQuantity')).toBe('Default quantity');
  });

  it('shows coded values by name, and the number when it has none', () => {
    expect(codedText('transaction-fills', 'vatGroup', 2)).toBe('Not needed');
    expect(codedText('document-type-from', 'costLayers', 1)).toBe('Take (oldest first)');
    expect(codedText('transaction-fills', 'vatGroup', 9)).toBe('9');
    expect(codedText('vat-groups', 'rate', 20)).toBeNull();
  });

  it('shows code and name first, never the fixed or JSON columns, in tables and forms', () => {
    const fixed = { documentTypeTransactionTypeId: 'k1' };
    expect(shownColumns(schema, fixed)).toEqual(['code', 'name', 'vatGroup', 'element', 'defaultQuantity']);
    expect(formColumns(schema, fixed).map(c => c.name)).toEqual(['vatGroup', 'element', 'defaultQuantity', 'name', 'code']);
  });

  it('writes a cell as people read it, a link by its target name', () => {
    const names = { documentTypeTransactionTypeId: new Map([['k1', 'ITEM · Item']]) };
    expect(cellOf('transaction-fills', schema, 'vatGroup', 1, names)).toBe('Chosen from the list');
    expect(cellOf('transaction-fills', schema, 'name', { sq: 'Artikull', en: 'Item' }, names)).toBe('Item');
    expect(cellOf('transaction-fills', schema, 'defaultQuantity', 2, names)).toBe('2.00');
    expect(cellOf('transaction-fills', schema, 'documentTypeTransactionTypeId', 'k1', names)).toBe('ITEM · Item');
    expect(numberOf('')).toBeNull();
    expect(numberOf('1,5')).toBe(1.5);
  });

  it('groups document types by their first category, the others last', () => {
    const groups = groupTypes([category('sales'), category('folder', false), category('purchases')], [type('FSHM', ['sales']), type('FBM', ['purchases', 'sales']),
      type('X', [])]);
    expect(groups.map(g => [g.category.id, g.types.map(t => t.id)])).toEqual([['sales', ['FSHM', 'FBM']], ['none', ['X']]]);
  });

  it('ticks the rights grid from the saved rights and saves only rows with a right', () => {
    const rows = rowsOf([{ table: 'tbl_partner', entity: 'Partners', categories: [category('clients'), category('suppliers'), category('all', false)] }],
      [{ table: 'tbl_partner', categoryId: 'clients', readAll: true, readOwn: false, write: true, forward: false }]);
    expect(rows.map(r => [r.categoryId, r.readAll, r.write])).toEqual([['clients', true, true], ['suppliers', false, false]]);
    expect(rightsOf(rows)).toEqual([{ table: 'tbl_partner', categoryId: 'clients', readAll: true, readOwn: false, write: true, forward: false }]);
  });
});
