import { describe, expect, it } from 'vitest';
import { TreeListNode, TreeListRow, elementTree, nests } from './element-tree';

const row = (id: string, idSup: string | null, typeId = 'place', name = id): TreeListRow => ({ id, idSup, typeId, code: id.toUpperCase(), name, type: typeId });
const rows = [row('co', null, 'company', 'Bar Lura'), row('bar', 'co', 'pos', 'Bar'), row('hall', 'bar', 'hall', 'Salla'), row('t1', 'hall', 'table', 'Tavolina 1'),
  row('t2', 'hall', 'table', 'Tavolina 2'), row('cash', 'co', 'cash', 'Arka'), row('lone', 'elsewhere', 'cash', 'Banka')];
const ids = (nodes: TreeListNode[]): unknown[] => nodes.map(n => (n.children.length ? [n.id, ids(n.children)] : n.id));

describe('register tree', () => {
  it('nests only when a row sits under another of the rows', () => {
    expect(nests(rows)).toBe(true);
    expect(nests([row('a', null), row('b', 'elsewhere')])).toBe(false);
  });

  it('shows every row under its parent, a row whose parent is elsewhere at the top, the top level open', () => {
    const { roots, open } = elementTree(rows, '', []);
    expect(ids(roots)).toEqual([['co', [['bar', [['hall', ['t1', 't2']]]], 'cash']], 'lone']);
    expect(open).toEqual(['co']);
  });

  it('keeps a match with its parents, opened, so it is seen where it sits', () => {
    const { roots, open } = elementTree(rows, 'tavolina 2', []);
    expect(ids(roots)).toEqual([['co', [['bar', [['hall', ['t2']]]]]]]);
    expect(open.sort()).toEqual(['bar', 'co', 'hall']);
  });

  it('filters by type the same way, and a match on a parent keeps its own place', () => {
    expect(ids(elementTree(rows, '', ['cash']).roots)).toEqual([['co', ['cash']], 'lone']);
    expect(ids(elementTree(rows, 'BAR', []).roots)).toEqual([['co', ['bar']]]);
  });
});
