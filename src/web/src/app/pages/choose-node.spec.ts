import { describe, expect, it } from 'vitest';
import { placeRows } from './choose-node';

const place = (id: string, parentId: string | null) => ({ id, code: id, name: id, companyId: null, companyName: null, parentId });

describe('choosing where to work', () => {
  it('lists the places as a tree: each under the place above it, a place with none above it at the top', () => {
    const rows = placeRows([place('group', null), place('roma', 'group'), place('roma-cash', 'roma'), place('tirana', 'group'), place('lone', 'elsewhere')]);
    expect(rows.map(r => `${r.depth}:${r.place.id}`)).toEqual(['0:group', '1:roma', '2:roma-cash', '1:tirana', '0:lone']);
  });
});
