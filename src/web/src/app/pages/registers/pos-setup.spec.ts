import { describe, expect, it } from 'vitest';
import { ElementType } from '../../core/entities';
import { PosPlace } from '../../core/pos';
import { placeRows, typesUnder } from './pos-setup';

const place = (id: string, parentId: string | null) => ({ id, parentId, name: id }) as PosPlace;
const type = (id: string, idSup: string | null, isElement = true) => ({ id, idSup, isElement, active: true }) as unknown as ElementType;

describe('setting up a point of sale', () => {
  it('lists its places as a tree', () => {
    const rows = placeRows([place('hall', null), place('t1', 'hall'), place('t2', 'hall'), place('terrace', null)]);
    expect(rows.map(r => `${r.depth}:${r.place.id}`)).toEqual(['0:hall', '1:t1', '1:t2', '0:terrace']);
  });

  it('offers what goes under a row: the children of its type, past folders', () => {
    const types = [type('restaurant', 'pos'), type('hall', 'restaurant'), type('counter', 'restaurant'), type('table', 'hall'), type('folder', 'hall', false), type('sofa', 'folder')];
    expect(typesUnder(types, 'restaurant').map(t => t.id)).toEqual(['hall', 'counter']);
    expect(typesUnder(types, 'hall').map(t => t.id)).toEqual(['table', 'sofa']);
    expect(typesUnder(types, 'table')).toEqual([]);
  });
});
